using SudyBrowser.Core.Models;
using SudyBrowser.Infrastructure.Storage;
using SudyBrowser.Infrastructure.Suggestions;
using Microsoft.Extensions.Logging.Abstractions;

namespace SudyBrowser.Tests;

public sealed class SqliteHistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sudybrowser-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteHistoryStore _store;

    public SqliteHistoryStoreTests()
    {
        _store = new SqliteHistoryStore(Path.Combine(_directory, "history.db"), NullLogger<SqliteHistoryStore>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder cleanup is best effort.
        }
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsNothing()
    {
        Assert.Empty(await _store.SearchAsync("y", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Recorded_PageIsFoundByHostPrefixWithoutWww()
    {
        await _store.RecordVisitAsync("https://www.youtube.com/", "YouTube", true);

        var result = await _store.SearchAsync("you", 10, CancellationToken.None);

        var entry = Assert.Single(result);
        Assert.Equal("https://www.youtube.com/", entry.Url);
        Assert.Equal("YouTube", entry.Title);
        Assert.Equal(1, entry.VisitCount);
    }

    [Fact]
    public async Task Search_MatchesTitleAndAddressSubstrings()
    {
        await _store.RecordVisitAsync("https://example.org/docs/guide", "Handbook", true);

        Assert.Single(await _store.SearchAsync("handbook", 10, CancellationToken.None));
        Assert.Single(await _store.SearchAsync("docs/gu", 10, CancellationToken.None));
        Assert.Empty(await _store.SearchAsync("missing", 10, CancellationToken.None));
    }

    [Fact]
    public async Task RepeatedVisit_IncrementsCount_TitleOnlyUpdate_DoesNot()
    {
        await _store.RecordVisitAsync("https://a.com/", "A", true, "https://a.com/favicon.ico");
        await _store.RecordVisitAsync("https://a.com/", "A", true);
        await _store.RecordVisitAsync("https://a.com/", "A renamed", false);

        var entry = Assert.Single(await _store.SearchAsync("a.com", 10, CancellationToken.None));
        Assert.Equal(2, entry.VisitCount);
        Assert.Equal("A renamed", entry.Title);
        Assert.Equal("https://a.com/favicon.ico", entry.FaviconUrl);
    }

    [Fact]
    public async Task Search_OrdersByVisitCount_AndHonoursLimit()
    {
        await _store.RecordVisitAsync("https://site-a.com/", "A", true);
        for (var i = 0; i < 3; i++)
        {
            await _store.RecordVisitAsync("https://site-b.com/", "B", true);
        }

        await _store.RecordVisitAsync("https://site-c.com/", "C", true);

        var result = await _store.SearchAsync("site", 2, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("https://site-b.com/", result[0].Url);
    }

    [Theory]
    [InlineData("100%")]
    [InlineData("a_b")]
    [InlineData("it's")]
    public async Task LikeWildcardsAndQuotes_AreTreatedAsPlainText(string query)
    {
        await _store.RecordVisitAsync("https://plain.com/abc", "Plain page", true);

        Assert.Empty(await _store.SearchAsync(query, 10, CancellationToken.None));
    }

    [Fact]
    public async Task EscapedWildcardCharacter_StillMatchesLiteralText()
    {
        await _store.RecordVisitAsync("https://x.com/", "Sale 100% off", true);

        Assert.Single(await _store.SearchAsync("100%", 10, CancellationToken.None));
    }

    [Fact]
    public async Task CancelledSearch_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.SearchAsync("a", 10, cts.Token));
    }

    [Fact]
    public async Task UnusableDatabasePath_NeverThrows_AndReturnsEmpty()
    {
        // A file where the directory should be makes opening impossible.
        var blocker = Path.Combine(Path.GetTempPath(), "sudybrowser-blocker-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(blocker, "x");
        try
        {
            using var broken = new SqliteHistoryStore(Path.Combine(blocker, "history.db"), NullLogger<SqliteHistoryStore>.Instance);

            await broken.RecordVisitAsync("https://a.com/", "A", true);
            Assert.Empty(await broken.SearchAsync("a", 10, CancellationToken.None));
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}

public sealed class HttpSearchSuggestionProviderTests : IDisposable
{
    private readonly System.Net.HttpListener _listener = new();
    private readonly string _prefix;
    private Func<System.Net.HttpListenerContext, Task> _handler = _ => Task.CompletedTask;

    public HttpSearchSuggestionProviderTests()
    {
        var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        port.Start();
        var number = ((System.Net.IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();

        _prefix = $"http://localhost:{number}/";
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    await _handler(context);
                    context.Response.Close();
                }
                catch (Exception)
                {
                    return;
                }
            }
        });
    }

    public void Dispose() => _listener.Close();

    private HttpSearchSuggestionProvider Provider(TimeSpan? timeout = null) => new(
        new SearchSuggestionOptions(_prefix + "?q={0}", timeout ?? TimeSpan.FromSeconds(2), 3),
        NullLogger<HttpSearchSuggestionProvider>.Instance);

    private static async Task Write(System.Net.HttpListenerResponse response, string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        response.ContentType = "application/json";
        await response.OutputStream.WriteAsync(bytes);
    }

    [Fact]
    public async Task ParsesSuggestions_AndHonoursMaxResults()
    {
        _handler = c => Write(c.Response, """["wea",["weather","weather today","weather tomorrow","weather week"]]""");
        using var provider = Provider();

        var result = await provider.GetSuggestionsAsync("wea", CancellationToken.None);

        Assert.Equal(new[] { "weather", "weather today", "weather tomorrow" }, result);
    }

    [Fact]
    public async Task ServerError_YieldsEmptyList()
    {
        _handler = c =>
        {
            c.Response.StatusCode = 500;
            return Task.CompletedTask;
        };
        using var provider = Provider();

        Assert.Empty(await provider.GetSuggestionsAsync("wea", CancellationToken.None));
    }

    [Fact]
    public async Task MalformedBody_YieldsEmptyList()
    {
        _handler = c => Write(c.Response, "<html>not json</html>");
        using var provider = Provider();

        Assert.Empty(await provider.GetSuggestionsAsync("wea", CancellationToken.None));
    }

    [Fact]
    public async Task Unreachable_YieldsEmptyList()
    {
        _listener.Close();
        using var provider = Provider();

        Assert.Empty(await provider.GetSuggestionsAsync("wea", CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_YieldsEmptyList()
    {
        _handler = _ => Task.Delay(1500);
        using var provider = Provider(TimeSpan.FromMilliseconds(200));

        Assert.Empty(await provider.GetSuggestionsAsync("wea", CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        _handler = _ => Task.Delay(1500);
        using var provider = Provider();
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetSuggestionsAsync("wea", cts.Token));
    }

    [Fact]
    public async Task QueryIsEscapedIntoASingleParameter()
    {
        string? rawQuery = null;
        _handler = c =>
        {
            rawQuery = c.Request.Url!.Query;
            return Write(c.Response, """["x",[]]""");
        };
        using var provider = Provider();

        await provider.GetSuggestionsAsync("a&b c", CancellationToken.None);

        Assert.Equal("?q=a%26b%20c", rawQuery);
    }
}