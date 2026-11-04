using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using SudyBrowser.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.Infrastructure.Suggestions;

/// <summary>Endpoint and limits of the online suggestion service. <see cref="UrlTemplate"/> takes the escaped query as {0}.</summary>
public sealed record SearchSuggestionOptions(string UrlTemplate, TimeSpan Timeout, int MaxResults)
{
    /// <summary>Google's suggestion endpoint, answering with <c>["query",["a","b",...]]</c>.</summary>
    public static SearchSuggestionOptions Google { get; } = new(
        "https://suggestqueries.google.com/complete/search?client=firefox&q={0}",
        TimeSpan.FromSeconds(3),
        8);
}

/// <summary>
/// Fetches query suggestions over HTTPS. Sends no cookies. Network and parse failures yield an empty list;
/// only cancellation propagates, so a superseded request is simply dropped by the caller.
/// </summary>
public sealed class HttpSearchSuggestionProvider : ISearchSuggestionProvider, IDisposable
{
    private readonly SearchSuggestionOptions _options;
    private readonly ILogger<HttpSearchSuggestionProvider> _logger;
    private readonly HttpClient _client;

    public HttpSearchSuggestionProvider(SearchSuggestionOptions options, ILogger<HttpSearchSuggestionProvider> logger)
    {
        _options = options;
        _logger = logger;
        _client = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { Timeout = options.Timeout };
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        try
        {
            var url = string.Format(_options.UrlTemplate, Uri.EscapeDataString(query));
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Parse(document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
                                       or InvalidOperationException or FormatException)
        {
            // Offline, timed out or an unexpected reply: suggestions are optional, the browser keeps working.
            _logger.LogDebug(ex, "Online suggestions unavailable");
            return Array.Empty<string>();
        }
    }

    public void Dispose() => _client.Dispose();

    private IReadOnlyList<string> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array
            || root.GetArrayLength() < 2
            || root[1].ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>(_options.MaxResults);
        foreach (var item in root[1].EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
            {
                results.Add(text);
                if (results.Count == _options.MaxResults)
                {
                    break;
                }
            }
        }

        return results;
    }
}
