using System.Windows.Input;
using LightBrowser.Core.Models;

namespace LightBrowser.App.ViewModels;

/// <summary>Presentation state for one <see cref="BrowserTab"/>.</summary>
public sealed class TabViewModel : ObservableObject, IDisposable
{
    private const string NewTabTitle = "Yeni sekme";
    private const string BlankPage = "about:blank";

    private string _title = NewTabTitle;
    private string _addressText = string.Empty;
    private string _faviconUrl = string.Empty;
    private string _lastUrl = string.Empty;
    private bool _isLoading;
    private bool _canGoBack;
    private bool _canGoForward;
    private bool _isActive;
    private bool _isHome = true;

    public TabViewModel(BrowserTab tab)
    {
        Tab = tab;
        Tab.StateChanged += OnTabStateChanged;
    }

    public BrowserTab Tab { get; }

    /// <summary>Opaque UI element supplied by the web view adapter.</summary>
    public object View => Tab.View;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>Editable address bar text. Overwritten whenever the tab navigates to a new URL.</summary>
    public string AddressText
    {
        get => _addressText;
        set => SetProperty(ref _addressText, value);
    }

    public string FaviconUrl
    {
        get => _faviconUrl;
        private set
        {
            if (SetProperty(ref _faviconUrl, value))
            {
                OnPropertyChanged(nameof(HasFavicon));
            }
        }
    }

    public bool HasFavicon => !string.IsNullOrEmpty(_faviconUrl);

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        private set => SetProperty(ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        private set => SetProperty(ref _canGoForward, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(IsWebViewVisible));
                OnPropertyChanged(nameof(WebViewVisibility));
            }
        }
    }

    /// <summary>True until the tab first navigates; the native home page is shown instead of the web view.</summary>
    public bool IsHome
    {
        get => _isHome;
        set
        {
            if (SetProperty(ref _isHome, value))
            {
                OnPropertyChanged(nameof(IsWebViewVisible));
                OnPropertyChanged(nameof(WebViewVisibility));
            }
        }
    }

    /// <summary>The web view is hidden behind the home page while <see cref="IsHome"/> is true.</summary>
    public bool IsWebViewVisible => IsActive && !IsHome;

    /// <summary>Pre-warms the web view with Hidden while on the home page so HWND initialization is instant.</summary>
    public System.Windows.Visibility WebViewVisibility =>
        !IsActive ? System.Windows.Visibility.Collapsed : (IsHome ? System.Windows.Visibility.Hidden : System.Windows.Visibility.Visible);

    public void Dispose() => Tab.StateChanged -= OnTabStateChanged;

    private void OnTabStateChanged(object? sender, NavigationState state)
    {
        var hasPage = !string.IsNullOrEmpty(state.Url) && state.Url != BlankPage;
        if (hasPage)
        {
            IsHome = false;
        }

        Title = string.IsNullOrWhiteSpace(state.Title) || state.Title == BlankPage ? NewTabTitle : state.Title;
        FaviconUrl = hasPage ? state.FaviconUrl : string.Empty;
        IsLoading = state.IsLoading;
        CanGoBack = state.CanGoBack;
        CanGoForward = state.CanGoForward;

        if (state.Url != _lastUrl)
        {
            _lastUrl = state.Url;
            AddressText = state.Url == BlankPage ? string.Empty : state.Url;
        }

        // Back/forward availability changes outside of user input, so ask WPF to re-query commands.
        CommandManager.InvalidateRequerySuggested();
    }
}
