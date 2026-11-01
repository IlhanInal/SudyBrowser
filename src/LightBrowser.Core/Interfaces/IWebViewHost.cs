using LightBrowser.Core.Models;

namespace LightBrowser.Core.Interfaces;

/// <summary>
/// Adapter around a single web view (one per tab). Hides the concrete engine from Core and ViewModels.
/// All members must be used from the UI thread. Disposing releases the underlying engine resources.
/// </summary>
public interface IWebViewHost : IDisposable
{
    /// <summary>Opaque UI element the view layer places in the visual tree.</summary>
    object View { get; }

    NavigationState State { get; }

    event EventHandler<NavigationState>? StateChanged;

    Task NavigateAsync(Uri uri);

    void GoBack();

    void GoForward();

    void Reload();
}
