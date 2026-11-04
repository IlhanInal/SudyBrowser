using SudyBrowser.Core.Interfaces;

namespace SudyBrowser.Core.Models;

/// <summary>A browser tab. Owns exactly one <see cref="IWebViewHost"/> and disposes it with the tab.</summary>
public sealed class BrowserTab : IDisposable
{
    private readonly IWebViewHost _host;
    private bool _disposed;

    public BrowserTab(IWebViewHost host)
    {
        _host = host;
        _host.StateChanged += OnHostStateChanged;
        _host.DownloadStarted += OnHostDownloadStarted;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public object View => _host.View;

    public NavigationState State => _host.State;

    public event EventHandler<NavigationState>? StateChanged;

    public event EventHandler<IDownloadOperation>? DownloadStarted;

    public Task NavigateAsync(Uri uri) => _host.NavigateAsync(uri);

    public void GoBack() => _host.GoBack();

    public void GoForward() => _host.GoForward();

    public void Reload() => _host.Reload();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.StateChanged -= OnHostStateChanged;
        _host.DownloadStarted -= OnHostDownloadStarted;
        _host.Dispose();
    }

    private void OnHostStateChanged(object? sender, NavigationState state) => StateChanged?.Invoke(this, state);

    private void OnHostDownloadStarted(object? sender, IDownloadOperation operation) =>
        DownloadStarted?.Invoke(this, operation);
}
