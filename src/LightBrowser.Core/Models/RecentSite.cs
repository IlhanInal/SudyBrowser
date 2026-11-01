namespace LightBrowser.Core.Models;

/// <summary>A visited site shown as a shortcut on the home page.</summary>
public sealed record RecentSite(string Url, string Title, string FaviconUrl);
