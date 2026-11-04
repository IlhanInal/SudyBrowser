namespace SudyBrowser.Infrastructure;

/// <summary>Well-known per-user locations used by the application.</summary>
public static class AppDataPaths
{
    private const string AppFolderName = "SudyBrowser";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string WebViewUserData { get; } = Path.Combine(Root, "WebView2");

    public static string Logs { get; } = Path.Combine(Root, "logs");
}
