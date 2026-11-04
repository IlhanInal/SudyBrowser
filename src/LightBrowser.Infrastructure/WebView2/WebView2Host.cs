using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using WpfWebView2 = Microsoft.Web.WebView2.Wpf.WebView2;

namespace SudyBrowser.Infrastructure.WebView2;

/// <summary>
/// <see cref="IWebViewHost"/> backed by a WPF WebView2 control that uses the shared environment.
/// Core initialization completes once the control is attached to a window; navigation requests
/// issued earlier simply await that initialization.
/// </summary>
public sealed class WebView2Host : IWebViewHost
{
    private readonly WpfWebView2 _control;
    private readonly ILogger<WebView2Host> _logger;
    private readonly Task _initialization;
    private CoreWebView2? _core;
    private bool _isLoading;
    private bool _disposed;

    public WebView2Host(CoreWebView2Environment environment, ILogger<WebView2Host> logger)
    {
        _logger = logger;
        _control = new WpfWebView2();
        _initialization = InitializeAsync(environment);
    }

    public object View => _control;

    public NavigationState State { get; private set; } = NavigationState.Empty;

    public event EventHandler<NavigationState>? StateChanged;

    public event EventHandler<IDownloadOperation>? DownloadStarted;

    public async Task NavigateAsync(Uri uri)
    {
        await _initialization;
        if (_disposed || _core is null)
        {
            return;
        }

        _logger.LogInformation("Navigating to {Uri}", uri);
        _core.Navigate(uri.AbsoluteUri);
    }

    public void GoBack()
    {
        if (_core?.CanGoBack == true)
        {
            _core.GoBack();
        }
    }

    public void GoForward()
    {
        if (_core?.CanGoForward == true)
        {
            _core.GoForward();
        }
    }

    public void Reload() => _core?.Reload();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_core is not null)
        {
            _core.NavigationStarting -= OnNavigationStarting;
            _core.NavigationCompleted -= OnNavigationCompleted;
            _core.SourceChanged -= OnCoreChanged;
            _core.DocumentTitleChanged -= OnCoreChanged;
            _core.HistoryChanged -= OnCoreChanged;
            _core.FaviconChanged -= OnCoreChanged;
            _core.DownloadStarting -= OnDownloadStarting;
            _core.NewWindowRequested -= OnNewWindowRequested;
            _core.ProcessFailed -= OnProcessFailed;
            _core = null;
        }

        _control.Dispose();
    }

    private async Task InitializeAsync(CoreWebView2Environment environment)
    {
        try
        {
            _control.DefaultBackgroundColor = System.Drawing.Color.FromArgb(246, 246, 246);
            await _control.EnsureCoreWebView2Async(environment);
            if (_disposed)
            {
                return;
            }

            _core = _control.CoreWebView2;
            _core.Settings.IsStatusBarEnabled = false;
            _core.NavigationStarting += OnNavigationStarting;
            _core.NavigationCompleted += OnNavigationCompleted;
            _core.SourceChanged += OnCoreChanged;
            _core.DocumentTitleChanged += OnCoreChanged;
            _core.HistoryChanged += OnCoreChanged;
            _core.FaviconChanged += OnCoreChanged;
            _core.DownloadStarting += OnDownloadStarting;
            _core.NewWindowRequested += OnNewWindowRequested;
            _core.ProcessFailed += OnProcessFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2 control initialization failed");
            throw;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _isLoading = true;
        Publish();
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _isLoading = false;
        if (!e.IsSuccess)
        {
            _logger.LogWarning("Navigation failed: {Status}", e.WebErrorStatus);
        }

        Publish();
    }

    private void OnCoreChanged(object? sender, object e) => Publish();

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // Hide WebView2's built-in download dialog; SudyBrowser uses its own lightweight downloads popup.
        e.Handled = true;
        var adapter = new WebView2DownloadOperation(e.DownloadOperation);
        DownloadStarted?.Invoke(this, adapter);
    }

    // Popups and target=_blank links are loaded in the same tab for now; multi-tab popups are future work.
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _core?.Navigate(e.Uri);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e) =>
        _logger.LogError("WebView2 process failed: {Kind} ({Reason})", e.ProcessFailedKind, e.Reason);

    private void Publish()
    {
        if (_core is null)
        {
            return;
        }

        State = new NavigationState(
            _core.Source ?? string.Empty,
            _core.DocumentTitle ?? string.Empty,
            _core.CanGoBack,
            _core.CanGoForward,
            _isLoading,
            _core.FaviconUri ?? string.Empty);
        StateChanged?.Invoke(this, State);
    }
}
