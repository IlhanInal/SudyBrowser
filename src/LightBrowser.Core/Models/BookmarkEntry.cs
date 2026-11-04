namespace SudyBrowser.Core.Models;

/// <summary>A user-saved bookmark entry.</summary>
public sealed record BookmarkEntry(
    string Url,
    string Title,
    string FaviconUrl,
    DateTime CreatedAtUtc);
