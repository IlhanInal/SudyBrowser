using SudyBrowser.Core.Models;

namespace SudyBrowser.App.Models;

/// <summary>One of the home page shortcut tiles backed by a bookmark; <see cref="Bookmark"/> is null if empty.</summary>
public sealed record ShortcutSlot(BookmarkEntry? Bookmark)
{
    public bool HasSite => Bookmark is not null;

    public string Title => Bookmark?.Title ?? string.Empty;

    public string Url => Bookmark?.Url ?? string.Empty;

    public string FaviconUrl => Bookmark?.FaviconUrl ?? string.Empty;
}
