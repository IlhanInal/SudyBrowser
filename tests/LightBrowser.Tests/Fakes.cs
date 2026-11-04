using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;

namespace SudyBrowser.Tests;

internal sealed class FakeWebViewHost : IWebViewHost
{
    public object View { get; } = new();

    public NavigationState State { get; private set; } = NavigationState.Empty;

    public bool IsDisposed { get; private set; }

    public List<Uri> Navigations { get; } = new();

    public event EventHandler<NavigationState>? StateChanged;

    public event EventHandler<IDownloadOperation>? DownloadStarted;

    public Task NavigateAsync(Uri uri)
    {
        Navigations.Add(uri);
        return Task.CompletedTask;
    }

    public void GoBack()
    {
    }

    public void GoForward()
    {
    }

    public void Reload()
    {
    }

    public void Raise(NavigationState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void RaiseDownload(IDownloadOperation operation) =>
        DownloadStarted?.Invoke(this, operation);

    public void Dispose() => IsDisposed = true;
}

internal sealed class FakeWebViewHostFactory : IWebViewHostFactory
{
    public List<FakeWebViewHost> Created { get; } = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public IWebViewHost Create()
    {
        var host = new FakeWebViewHost();
        Created.Add(host);
        return host;
    }
}
