namespace SudyBrowser.Core.Interfaces;

/// <summary>Owns the shared browser engine environment and creates per-tab hosts from it.</summary>
public interface IWebViewHostFactory
{
    /// <summary>Creates the shared engine environment. Must complete before <see cref="Create"/>.</summary>
    /// <exception cref="BrowserEngineUnavailableException">The engine runtime is missing or failed to start.</exception>
    Task InitializeAsync();

    IWebViewHost Create();

    /// <summary>Returns active browser process IDs for memory monitoring.</summary>
    IReadOnlyList<int> GetProcessIds() => Array.Empty<int>();
}
