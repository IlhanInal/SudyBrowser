namespace SudyBrowser.Core.Policies;

/// <summary>Text helpers shared by suggestion matching, de-duplication and inline completion.</summary>
public static class UrlText
{
    private const string HttpPrefix = "http://";
    private const string HttpsPrefix = "https://";
    private const string WwwPrefix = "www.";

    /// <summary>Removes the http(s) scheme.</summary>
    public static string StripScheme(string text)
    {
        if (text.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return text[HttpsPrefix.Length..];
        }

        return text.StartsWith(HttpPrefix, StringComparison.OrdinalIgnoreCase) ? text[HttpPrefix.Length..] : text;
    }

    /// <summary>Removes the scheme and a leading "www.".</summary>
    public static string StripSchemeAndWww(string text)
    {
        var withoutScheme = StripScheme(text);
        return withoutScheme.StartsWith(WwwPrefix, StringComparison.OrdinalIgnoreCase)
            ? withoutScheme[WwwPrefix.Length..]
            : withoutScheme;
    }

    /// <summary>Lower-cased, trimmed, scheme/"www." free form of what the user typed.</summary>
    public static string NormalizeQuery(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : StripSchemeAndWww(text.Trim()).ToLowerInvariant();

    /// <summary>Key under which two addresses count as the same suggestion.</summary>
    public static string DedupeKey(string url) => StripSchemeAndWww(url).TrimEnd('/').ToLowerInvariant();

    /// <summary>The host part of an already scheme-less, "www."-less address.</summary>
    public static string HostOf(string strippedUrl)
    {
        var end = strippedUrl.AsSpan().IndexOfAny('/', '?', '#');
        return end < 0 ? strippedUrl : strippedUrl[..end];
    }
}
