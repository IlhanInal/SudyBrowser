namespace SudyBrowser.Core.Models;

/// <summary>A visited page as stored in the browsing history.</summary>
public sealed record HistoryEntry(string Url, string Title, int VisitCount, DateTime LastVisitUtc, string FaviconUrl = "");

