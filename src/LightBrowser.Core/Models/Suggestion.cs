using SudyBrowser.Core.Policies;

namespace SudyBrowser.Core.Models;

public enum SuggestionSource
{
    /// <summary>A page the user visited before.</summary>
    History,

    /// <summary>A bookmarked page. Reserved: bookmarks are not implemented yet.</summary>
    Bookmark,

    /// <summary>A built-in well-known site.</summary>
    Site,

    /// <summary>A search query suggested by the online provider.</summary>
    Search,
}

/// <summary>
/// One row of the address bar suggestion list.
/// <paramref name="Text"/> is what is put into the address bar when the row is chosen
/// (a URL, or the query for <see cref="SuggestionSource.Search"/>); <paramref name="Url"/> is empty for searches.
/// </summary>
public sealed record Suggestion(string Text, string Title, string Url, SuggestionSource Source, string FaviconUrl = "")
{
    public bool IsSearch => Source == SuggestionSource.Search;

    /// <summary>The address without scheme and trailing slash, as shown in the list.</summary>
    public string DisplayUrl => Url.Length == 0 ? string.Empty : UrlText.StripScheme(Url).TrimEnd('/');
}
