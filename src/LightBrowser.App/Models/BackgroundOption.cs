namespace SudyBrowser.App.Models;

/// <summary>A selectable home page background (full image plus a small preview).</summary>
public sealed record BackgroundOption(string Name, string Key, Uri ImageUri, Uri ThumbnailUri);
