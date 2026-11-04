using System.IO;
using SudyBrowser.App.Services;
using SudyBrowser.App.ViewModels;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;
using SudyBrowser.Core.Services;
using SudyBrowser.Infrastructure.Suggestions;
using Microsoft.Extensions.Logging.Abstractions;

namespace SudyBrowser.Tests;

internal sealed class FakeFaviconFetcher : IFaviconFetcher
{
    public Dictionary<string, byte[]?> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, TaskCompletionSource<byte[]?>> Gates { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> RequestedUrls { get; } = new();

    public async Task<byte[]?> FetchBytesAsync(string faviconUrl, CancellationToken cancellationToken)
    {
        lock (RequestedUrls)
        {
            RequestedUrls.Add(faviconUrl);
        }

        if (Gates.TryGetValue(faviconUrl, out var gate))
        {
            return await gate.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Responses.TryGetValue(faviconUrl, out var bytes) ? bytes : null;
    }
}

public class FaviconTests
{
    // Valid 1x1 transparent PNG (67 bytes).
    internal static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData("https://www.sahibinden.com/", null, new[] { "https://www.sahibinden.com/favicon.ico" })]
    [InlineData("https://sahibinden.com/", null, new[] { "https://sahibinden.com/favicon.ico", "https://www.sahibinden.com/favicon.ico" })]
    [InlineData("https://github.com/torvalds/linux", "https://github.githubassets.com/favicons/favicon.png", new[] { "https://github.githubassets.com/favicons/favicon.png", "https://github.com/favicon.ico", "https://www.github.com/favicon.ico" })]
    [InlineData("about:blank", null, new string[0])]
    [InlineData("file:///C:/test.html", "javascript:alert(1)", new string[0])]
    public void FaviconUrlPolicy_ResolvesSafeHttpCandidatesOnly(string pageUrl, string? explicitFavicon, string[] expected)
    {
        var candidates = FaviconUrlPolicy.GetCandidateUrls(pageUrl, explicitFavicon);
        Assert.Equal(expected, candidates.ToArray());
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=1", "youtube.com")]
    [InlineData("https://youtube.com/", "youtube.com")]
    [InlineData("https://github.com/", "github.com")]
    [InlineData("invalid-url", "")]
    public void FaviconUrlPolicy_NormalizesSiteKey(string url, string expectedKey)
    {
        Assert.Equal(expectedKey, FaviconUrlPolicy.GetSiteCacheKey(url));
    }

    [Fact]
    public async Task CacheService_DownloadsOnce_AndServesSubsequentRequestsFromMemory()
    {
        var fetcher = new FakeFaviconFetcher();
        fetcher.Responses["https://www.youtube.com/favicon.ico"] = ValidPngBytes;
        var cache = new FaviconCacheService(fetcher, NullLogger<FaviconCacheService>.Instance);

        var first = await cache.GetOrFetchAsync("https://www.youtube.com/watch?v=1", null, CancellationToken.None);
        var second = await cache.GetOrFetchAsync("https://youtube.com/feed", null, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.True(first!.IsFrozen);
        Assert.Single(fetcher.RequestedUrls);
        Assert.True(cache.TryGetCached("https://www.youtube.com/", null, out var fromSyncCache));
        Assert.Same(first, fromSyncCache);
    }

    [Fact]
    public async Task CacheService_DeduplicatesConcurrentRequestsForSameSite()
    {
        var fetcher = new FakeFaviconFetcher();
        var gate = new TaskCompletionSource<byte[]?>();
        fetcher.Gates["https://github.com/favicon.ico"] = gate;
        var cache = new FaviconCacheService(fetcher, NullLogger<FaviconCacheService>.Instance);

        var t1 = cache.GetOrFetchAsync("https://github.com/a", null, CancellationToken.None);
        var t2 = cache.GetOrFetchAsync("https://github.com/b", null, CancellationToken.None);

        gate.SetResult(ValidPngBytes);
        var results = await Task.WhenAll(t1, t2);

        Assert.NotNull(results[0]);
        Assert.Same(results[0], results[1]);
        Assert.Single(fetcher.RequestedUrls);
    }

    [Fact]
    public async Task CacheService_RemembersFailedSite_AndDoesNotHammerNetwork()
    {
        var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var fetcher = new FakeFaviconFetcher(); // returns null for everything
        var cache = new FaviconCacheService(
            fetcher,
            NullLogger<FaviconCacheService>.Instance,
            failureCooldown: TimeSpan.FromMinutes(10),
            utcNow: () => now);

        var first = await cache.GetOrFetchAsync("https://www.unreachable.example/", null, CancellationToken.None);
        var callCountAfterFirst = fetcher.RequestedUrls.Count;

        var second = await cache.GetOrFetchAsync("https://www.unreachable.example/page2", null, CancellationToken.None);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(callCountAfterFirst, fetcher.RequestedUrls.Count);
    }

    [Fact]
    public async Task CacheService_EvictsLeastRecentlyUsedEntriesWhenFull()
    {
        var fetcher = new FakeFaviconFetcher();
        for (var i = 0; i < 12; i++)
        {
            fetcher.Responses[$"https://www.site{i}.com/favicon.ico"] = ValidPngBytes;
        }

        var cache = new FaviconCacheService(fetcher, NullLogger<FaviconCacheService>.Instance, maxEntries: 8);

        for (var i = 0; i < 12; i++)
        {
            await cache.GetOrFetchAsync($"https://www.site{i}.com/", null, CancellationToken.None);
        }

        Assert.Equal(8, cache.CachedCount);
        Assert.False(cache.TryGetCached("https://www.site0.com/", null, out _));
        Assert.True(cache.TryGetCached("https://www.site11.com/", null, out var latest));
        Assert.NotNull(latest);
    }

    [Fact]
    public async Task Omnibox_AssignsFaviconToWebsiteRows_NeverToSearchSuggestions()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

        var history = new FakeHistoryStore();
        history.Entries.Add(new HistoryEntry(
            "https://www.sahibinden.com/",
            "Sahibinden",
            5,
            DateTime.UtcNow,
            "https://www.sahibinden.com/favicon.ico"));

        var online = new FakeSuggestionProvider { Reply = new[] { "sahibinden kiralık", "barcelona" } };
        var fetcher = new FakeFaviconFetcher();
        fetcher.Responses["https://www.sahibinden.com/favicon.ico"] = ValidPngBytes;

        var favicons = new FaviconCacheService(fetcher, NullLogger<FaviconCacheService>.Instance);
        var settings = new SettingsViewModel(new ThemeService()) { OnlineSuggestionsEnabled = true };
        var service = new SuggestionService(history, online);
        using var omnibox = new OmniboxViewModel(
            service,
            new AddressParser(),
            settings,
            NullLogger<OmniboxViewModel>.Instance,
            favicons);

        omnibox.TextTyped("sah", allowInline: false);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline
               && (!omnibox.Suggestions.Any(s => s.IsSearch)
                   || !omnibox.Suggestions.Any(s => !s.IsSearch && s.HasFavicon)))
        {
            await Task.Delay(15);
        }

        var siteRow = Assert.Single(omnibox.Suggestions, s => s.Url == "https://www.sahibinden.com/");
        Assert.True(siteRow.HasFavicon);
        Assert.NotNull(siteRow.Favicon);

        var searchRows = omnibox.Suggestions.Where(s => s.IsSearch).ToList();
        Assert.NotEmpty(searchRows);
        Assert.All(searchRows, s =>
        {
            Assert.False(s.HasFavicon);
            Assert.Null(s.Favicon);
        });
    }

    [Fact]
    public async Task Omnibox_RapidTyping_DoesNotAttachStaleFaviconOrBlockList()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

        var history = new FakeHistoryStore();
        var online = new FakeSuggestionProvider();
        var fetcher = new FakeFaviconFetcher();
        var slowGate = new TaskCompletionSource<byte[]?>();
        fetcher.Gates["https://www.youtube.com/favicon.ico"] = slowGate;
        fetcher.Responses["https://github.com/favicon.ico"] = ValidPngBytes;

        var favicons = new FaviconCacheService(fetcher, NullLogger<FaviconCacheService>.Instance);
        var settings = new SettingsViewModel(new ThemeService());
        var service = new SuggestionService(history, online);
        using var omnibox = new OmniboxViewModel(
            service,
            new AddressParser(),
            settings,
            NullLogger<OmniboxViewModel>.Instance,
            favicons);

        // 1. User types "you" — list opens immediately even though YouTube's favicon is hanging.
        omnibox.TextTyped("you", allowInline: false);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!omnibox.IsOpen && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(omnibox.IsOpen);
        var youtubeRow = omnibox.Suggestions.First(s => s.Url.Contains("youtube.com"));
        Assert.False(youtubeRow.HasFavicon);

        // 2. User quickly replaces text with "git".
        omnibox.TextTyped("git", allowInline: false);
        deadline = DateTime.UtcNow.AddSeconds(2);
        while ((!omnibox.Suggestions.Any(s => s.Url.Contains("github.com") && s.HasFavicon))
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        // 3. Now release the old slow YouTube favicon; it must not corrupt the current "git" list.
        slowGate.SetResult(ValidPngBytes);
        await Task.Delay(50);

        Assert.DoesNotContain(omnibox.Suggestions, s => s.Url.Contains("youtube.com"));
        Assert.Contains(omnibox.Suggestions, s => s.Url.Contains("github.com") && s.HasFavicon);
    }

    [Fact]
    public async Task HttpFaviconFetcher_RejectsHtmlAndOversizedResponses_AndHandlesOffline()
    {
        var portListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portListener.Start();
        var port = ((System.Net.IPEndPoint)portListener.LocalEndpoint).Port;
        portListener.Stop();

        using var listener = new System.Net.HttpListener();
        var prefix = $"http://localhost:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        Func<System.Net.HttpListenerContext, Task> handler = _ => Task.CompletedTask;
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    await handler(ctx);
                    ctx.Response.Close();
                }
                catch
                {
                    return;
                }
            }
        });

        try
        {
            using var fetcher = new HttpFaviconFetcher(
                TimeSpan.FromSeconds(2),
                maxBytes: 1024,
                maxRedirects: 2,
                NullLogger<HttpFaviconFetcher>.Instance);

            // 1. Valid PNG image succeeds.
            handler = async ctx =>
            {
                ctx.Response.ContentType = "image/png";
                await ctx.Response.OutputStream.WriteAsync(ValidPngBytes);
            };
            var okBytes = await fetcher.FetchBytesAsync(prefix + "favicon.ico", CancellationToken.None);
            Assert.NotNull(okBytes);

            // 2. HTML error page disguised as 200 OK is rejected.
            handler = async ctx =>
            {
                ctx.Response.ContentType = "text/html";
                var html = System.Text.Encoding.UTF8.GetBytes("<html>not an icon</html>");
                await ctx.Response.OutputStream.WriteAsync(html);
            };
            Assert.Null(await fetcher.FetchBytesAsync(prefix + "favicon.ico", CancellationToken.None));

            // 3. Oversized payload (> 1024 bytes) is aborted and rejected.
            handler = async ctx =>
            {
                ctx.Response.ContentType = "image/png";
                var huge = new byte[4096];
                Array.Copy(ValidPngBytes, huge, ValidPngBytes.Length);
                await ctx.Response.OutputStream.WriteAsync(huge);
            };
            Assert.Null(await fetcher.FetchBytesAsync(prefix + "favicon.ico", CancellationToken.None));

            // 4. Non-HTTP scheme is rejected without touching the network.
            Assert.Null(await fetcher.FetchBytesAsync("file:///c:/favicon.ico", CancellationToken.None));

            // 5. Offline / closed listener returns null gracefully.
            listener.Close();
            Assert.Null(await fetcher.FetchBytesAsync(prefix + "favicon.ico", CancellationToken.None));
        }
        finally
        {
            if (listener.IsListening)
            {
                listener.Close();
            }
        }
    }
}
