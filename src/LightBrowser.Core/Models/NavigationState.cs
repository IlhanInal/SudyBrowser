namespace LightBrowser.Core.Models;

/// <summary>Immutable snapshot of a tab's navigation status as reported by its web view.</summary>
public sealed record NavigationState(
    string Url,
    string Title,
    bool CanGoBack,
    bool CanGoForward,
    bool IsLoading,
    string FaviconUrl = "")
{
    public static NavigationState Empty { get; } = new(string.Empty, string.Empty, false, false, false);
}
