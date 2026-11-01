namespace LightBrowser.App.Models;

/// <summary>A selectable home page background (full image plus a small preview).</summary>
public sealed record BackgroundOption(string Name, Uri ImageUri, Uri ThumbnailUri);
