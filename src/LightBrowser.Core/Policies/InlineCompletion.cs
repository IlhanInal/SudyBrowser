using SudyBrowser.Core.Models;

namespace SudyBrowser.Core.Policies;

/// <summary>Decides which part of the top suggestion may be completed inline inside the address bar.</summary>
public static class InlineCompletion
{
    /// <summary>
    /// Returns the text that follows <paramref name="typed"/> in the top suggestion's address, or null when
    /// the suggestion does not start with what was typed. The caller shows it selected after the typed text.
    /// </summary>
    public static string? GetTail(string typed, Suggestion? top)
    {
        if (top is null || top.IsSearch || typed.Length == 0 || typed.Any(char.IsWhiteSpace))
        {
            return null;
        }

        string display;
        if (typed.Contains("://", StringComparison.Ordinal))
        {
            display = top.Url;
        }
        else if (typed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            display = UrlText.StripScheme(top.Url);
        }
        else
        {
            display = UrlText.StripSchemeAndWww(top.Url);
        }

        // A bare trailing slash is noise: "youtube.com" is nicer than "youtube.com/".
        var body = UrlText.StripScheme(display);
        if (body.Length > 0 && body.IndexOf('/') == body.Length - 1)
        {
            display = display[..^1];
        }

        return display.Length > typed.Length && display.StartsWith(typed, StringComparison.OrdinalIgnoreCase)
            ? display[typed.Length..]
            : null;
    }
}
