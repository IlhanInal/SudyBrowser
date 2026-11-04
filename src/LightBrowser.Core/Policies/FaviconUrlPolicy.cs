namespace SudyBrowser.Core.Policies;

/// <summary>
/// Resolves candidate HTTP/HTTPS favicon URLs for a site without sending text to third-party services.
/// </summary>
public static class FaviconUrlPolicy
{
    private const string FaviconPath = "/favicon.ico";

    /// <summary>Returns true if <paramref name="url"/> is an absolute HTTP or HTTPS URI with a valid host.</summary>
    public static bool IsSafeHttpUrl(string? url, out Uri? parsedUri)
    {
        if (!string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host))
        {
            parsedUri = uri;
            return true;
        }

        parsedUri = null;
        return false;
    }

    /// <summary>
    /// Normalized cache key for a website's favicon (lowercase host without a leading "www."),
    /// or empty string if <paramref name="pageOrFaviconUrl"/> is not a valid web URL.
    /// </summary>
    public static string GetSiteCacheKey(string? pageOrFaviconUrl)
    {
        if (!IsSafeHttpUrl(pageOrFaviconUrl, out var uri) || uri is null)
        {
            return string.Empty;
        }

        var host = uri.IdnHost;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && host.Length > 4)
        {
            host = host[4..];
        }

        return host.ToLowerInvariant();
    }

    /// <summary>
    /// Builds an ordered, de-duplicated list of candidate favicon URLs to try for a given site:
    /// 1) Explicit <paramref name="knownFaviconUrl"/> reported by WebView2 or stored in history, if valid.
    /// 2) <c>{scheme}://{authority}/favicon.ico</c> of <paramref name="pageUrl"/>.
    /// 3) If the host has no subdomain (e.g. <c>sahibinden.com</c>), <c>https://www.{host}/favicon.ico</c> as fallback.
    /// </summary>
    public static IReadOnlyList<string> GetCandidateUrls(string? pageUrl, string? knownFaviconUrl = null)
    {
        var candidates = new List<string>(3);

        if (IsSafeHttpUrl(knownFaviconUrl, out var explicitUri) && explicitUri is not null)
        {
            candidates.Add(explicitUri.AbsoluteUri);
        }

        if (IsSafeHttpUrl(pageUrl, out var pageUri) && pageUri is not null)
        {
            var originFavicon = pageUri.GetLeftPart(UriPartial.Authority) + FaviconPath;
            if (!candidates.Contains(originFavicon, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(originFavicon);
            }

            if (pageUri.HostNameType == UriHostNameType.Dns
                && !pageUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                && pageUri.Host.Count(c => c == '.') == 1)
            {
                var wwwFavicon = $"{pageUri.Scheme}://www.{pageUri.Authority}{FaviconPath}";
                if (!candidates.Contains(wwwFavicon, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(wwwFavicon);
                }
            }
        }

        return candidates;
    }
}
