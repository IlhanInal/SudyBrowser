using System.Net.Http;
using System.Net.Http.Headers;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Policies;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.Infrastructure.Suggestions;

/// <summary>
/// Downloads raw favicon bytes over HTTP/HTTPS with strict security and memory guardrails:
/// - Only HTTP/HTTPS URLs are allowed (including across redirects).
/// - Cookies are disabled.
/// - Redirects are capped (default 3).
/// - Rejects HTML/JSON/text responses and oversized payloads (default 128 KB max).
/// - Never throws on network or validation failure; only propagates caller cancellation.
/// </summary>
public sealed class HttpFaviconFetcher : IFaviconFetcher, IDisposable
{
    public const int DefaultMaxBytes = 128 * 1024;
    public const int DefaultMaxRedirects = 3;

    private readonly HttpClient _client;
    private readonly ILogger<HttpFaviconFetcher> _logger;
    private readonly int _maxBytes;

    public HttpFaviconFetcher(ILogger<HttpFaviconFetcher> logger)
        : this(TimeSpan.FromSeconds(2.5), DefaultMaxBytes, DefaultMaxRedirects, logger)
    {
    }

    public HttpFaviconFetcher(
        TimeSpan timeout,
        int maxBytes,
        int maxRedirects,
        ILogger<HttpFaviconFetcher> logger)
    {
        _logger = logger;
        _maxBytes = maxBytes;

        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = maxRedirects,
        };

        _client = new HttpClient(handler) { Timeout = timeout };
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*", 1.0));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SudyBrowser/1.0");
    }

    public async Task<byte[]?> FetchBytesAsync(string faviconUrl, CancellationToken cancellationToken)
    {
        if (!FaviconUrlPolicy.IsSafeHttpUrl(faviconUrl, out var requestUri) || requestUri is null)
        {
            return null;
        }

        try
        {
            using var response = await _client
                .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // Ensure any final redirect target is still HTTP/HTTPS.
            if (response.RequestMessage?.RequestUri is { } finalUri
                && finalUri.Scheme != Uri.UriSchemeHttp
                && finalUri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            if (!IsAllowedContentType(response.Content.Headers.ContentType?.MediaType))
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is { } contentLength
                && (contentLength <= 0 || contentLength > _maxBytes))
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream(
                response.Content.Headers.ContentLength is { } len && len > 0 ? (int)len : 2048);

            var buffer = new byte[4096];
            var totalRead = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead > _maxBytes)
                {
                    return null;
                }

                memory.Write(buffer, 0, read);
            }

            return totalRead > 0 && LooksLikeBitmapImage(memory.GetBuffer(), totalRead)
                ? memory.ToArray()
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Favicon fetch failed for {Url}", faviconUrl);
            return null;
        }
    }

    public void Dispose() => _client.Dispose();

    private static bool IsAllowedContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return true;
        }

        // Exclude SVG (not decoded by WIC BitmapImage) and non-binary text/html/json bodies.
        if (mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("binary/octet-stream", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Quick magic-byte check to reject HTML/text disguised with a permissive Content-Type header.</summary>
    private static bool LooksLikeBitmapImage(byte[] bytes, int length)
    {
        if (length < 4)
        {
            return false;
        }

        // ICO (00 00 01 00) or CUR (00 00 02 00)
        if (bytes[0] == 0x00 && bytes[1] == 0x00 && (bytes[2] == 0x01 || bytes[2] == 0x02) && bytes[3] == 0x00)
        {
            return true;
        }

        // PNG (89 50 4E 47)
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return true;
        }

        // JPEG (FF D8 FF)
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return true;
        }

        // GIF ("GIF8")
        if (bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'8')
        {
            return true;
        }

        // BMP ("BM")
        if (bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return true;
        }

        // RIFF WEBP ("RIFF" ... "WEBP")
        if (length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return true;
        }

        return false;
    }
}
