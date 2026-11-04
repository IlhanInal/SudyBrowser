using System.Collections.ObjectModel;
using System.Windows.Input;
using SudyBrowser.App.Models;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Services;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly TabManager _tabManager;
    private readonly IAddressParser _addressParser;
    private readonly RecentSites _recentSites;
    private readonly HistoryRecorder _history;
    private readonly BookmarkService? _bookmarks;
    private readonly ILogger<MainViewModel> _logger;
    private TabViewModel? _activeTab;
    private string _homeSearchText = string.Empty;
    private IReadOnlyList<ShortcutSlot> _shortcuts = Array.Empty<ShortcutSlot>();

    public MainViewModel(
        TabManager tabManager,
        IAddressParser addressParser,
        RecentSites recentSites,
        HistoryRecorder history,
        OmniboxViewModel omnibox,
        DownloadsViewModel downloads,
        StatusViewModel status,
        SettingsViewModel settings,
        ILogger<MainViewModel> logger,
        BookmarkService? bookmarks = null)
    {
        _tabManager = tabManager;
        _addressParser = addressParser;
        _recentSites = recentSites;
        _history = history;
        _bookmarks = bookmarks;
        _logger = logger;
        Omnibox = omnibox;
        Downloads = downloads;
        Status = status;
        Settings = settings;

        _tabManager.TabOpened += OnTabOpened;
        _tabManager.TabClosed += OnTabClosed;
        _tabManager.ActiveTabChanged += OnActiveTabChanged;
        _recentSites.Changed += OnRecentSitesChanged;
        Settings.SavedPageRequested += OnSavedPageRequested;
        if (_bookmarks is not null)
        {
            _bookmarks.Changed += OnBookmarksChanged;
        }

        RebuildShortcuts();

        NewTabCommand = new RelayCommand(_ => _tabManager.OpenTab());
        CloseTabCommand = new RelayCommand(CloseTab);
        CloseActiveTabCommand = new RelayCommand(_ => CloseTab(ActiveTab), _ => ActiveTab is not null);
        GoBackCommand = new RelayCommand(_ => ActiveTab?.Tab.GoBack(), _ => ActiveTab?.CanGoBack == true);
        GoForwardCommand = new RelayCommand(_ => ActiveTab?.Tab.GoForward(), _ => ActiveTab?.CanGoForward == true);
        ReloadCommand = new RelayCommand(_ => ActiveTab?.Tab.Reload(), _ => ActiveTab is not null);
        NavigateCommand = new RelayCommand(async _ => await NavigateAsync(), _ => ActiveTab is not null);
        HomeSearchCommand = new RelayCommand(async _ => await SearchFromHomeAsync(), _ => ActiveTab is not null);
        OpenShortcutCommand = new RelayCommand(async p => await OpenShortcutAsync(p as ShortcutSlot));
        AcceptSuggestionCommand = new RelayCommand(async p => await AcceptSuggestionAsync(p));
        ToggleBookmarkCommand = new RelayCommand(_ => ToggleActiveTabBookmark(), _ => CanBookmarkActiveTab());
    }

    public StatusViewModel Status { get; }

    public OmniboxViewModel Omnibox { get; }

    public DownloadsViewModel Downloads { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Up to 6 bookmarked shortcut tiles on the home page; empty when user has no bookmarks.</summary>
    public IReadOnlyList<ShortcutSlot> Shortcuts
    {
        get => _shortcuts;
        private set => SetProperty(ref _shortcuts, value);
    }

    /// <summary>True when at least one bookmark shortcut exists to display.</summary>
    public bool HasShortcuts => Shortcuts.Count > 0;

    /// <summary>Text of the home page search box.</summary>
    public string HomeSearchText
    {
        get => _homeSearchText;
        set => SetProperty(ref _homeSearchText, value);
    }

    public ICommand HomeSearchCommand { get; }

    public ICommand OpenShortcutCommand { get; }

    /// <summary>Opens the suggestion passed as command parameter (mouse selection in the list).</summary>
    public ICommand AcceptSuggestionCommand { get; }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (SetProperty(ref _activeTab, value))
            {
                OnPropertyChanged(nameof(SelectedTab));
                OnPropertyChanged(nameof(IsActiveTabBookmarked));
            }
        }
    }

    public bool IsActiveTabBookmarked =>
        _bookmarks is not null
        && ActiveTab is { } tab
        && _bookmarks.IsBookmarked(!string.IsNullOrWhiteSpace(tab.Tab.State.Url) ? tab.Tab.State.Url : tab.AddressText);

    public ICommand ToggleBookmarkCommand { get; }

    /// <summary>Two-way selection target for the tab strip; selecting a tab activates it in the manager.</summary>
    public TabViewModel? SelectedTab
    {
        get => ActiveTab;
        set
        {
            if (value is not null)
            {
                _tabManager.SelectTab(value.Tab);
            }
            else
            {
                // A tab was removed from the strip; re-assert the real selection.
                OnPropertyChanged();
            }
        }
    }

    public ICommand NewTabCommand { get; }

    /// <summary>Closes the tab passed as command parameter.</summary>
    public ICommand CloseTabCommand { get; }

    public ICommand CloseActiveTabCommand { get; }

    public ICommand GoBackCommand { get; }

    public ICommand GoForwardCommand { get; }

    public ICommand ReloadCommand { get; }

    public ICommand NavigateCommand { get; }

    /// <summary>Opens the initial blank tab.</summary>
    public void Start() => _tabManager.OpenTab();

    public void Dispose()
    {
        _tabManager.TabOpened -= OnTabOpened;
        _tabManager.TabClosed -= OnTabClosed;
        _tabManager.ActiveTabChanged -= OnActiveTabChanged;
        _recentSites.Changed -= OnRecentSitesChanged;
        Settings.SavedPageRequested -= OnSavedPageRequested;
        if (_bookmarks is not null)
        {
            _bookmarks.Changed -= OnBookmarksChanged;
        }

        Omnibox.Dispose();
        Downloads.Dispose();
        Settings.Dispose();

        foreach (var tab in Tabs)
        {
            tab.Tab.StateChanged -= OnTabStateChanged;
            tab.Tab.DownloadStarted -= OnTabDownloadStarted;
            tab.Dispose();
        }

        Tabs.Clear();
        _tabManager.Dispose();
        Status.Dispose();
    }

    private void CloseTab(object? parameter)
    {
        if (parameter is not TabViewModel tab)
        {
            return;
        }

        _tabManager.CloseTab(tab.Tab);

        // Keep the window usable after the last tab is closed.
        if (_tabManager.Tabs.Count == 0)
        {
            _tabManager.OpenTab();
        }
    }

    private Task NavigateAsync() => NavigateAsync(ActiveTab, ActiveTab?.AddressText);

    private async Task NavigateAsync(TabViewModel? tab, string? text)
    {
        Omnibox.Close();
        var input = _addressParser.Parse(text);
        if (tab is null || input is null)
        {
            return;
        }

        // Reveal the web view first: a hidden WebView2 cannot finish initializing.
        tab.IsHome = false;
        try
        {
            await tab.Tab.NavigateAsync(input.Target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Navigation to {Target} failed", input.Target);
        }
    }

    private async Task SearchFromHomeAsync()
    {
        var text = HomeSearchText;
        if (ActiveTab is { } tab && !string.IsNullOrWhiteSpace(text))
        {
            tab.AddressText = text;
            await NavigateAsync(tab, text);
            HomeSearchText = string.Empty;
        }
    }

    private Task OpenShortcutAsync(ShortcutSlot? slot) =>
        slot is { HasSite: true, Url: { Length: > 0 } url }
            ? NavigateAsync(ActiveTab, url)
            : Task.CompletedTask;

    private void OnTabOpened(object? sender, BrowserTab tab)
    {
        tab.StateChanged += OnTabStateChanged;
        tab.DownloadStarted += OnTabDownloadStarted;
        Tabs.Add(new TabViewModel(tab));
    }

    private void OnTabClosed(object? sender, BrowserTab tab)
    {
        tab.StateChanged -= OnTabStateChanged;
        tab.DownloadStarted -= OnTabDownloadStarted;
        _history.Forget(tab.Id);
        var viewModel = Tabs.FirstOrDefault(t => ReferenceEquals(t.Tab, tab));
        if (viewModel is not null)
        {
            Tabs.Remove(viewModel);
            viewModel.Dispose();
        }
    }

    private void OnTabDownloadStarted(object? sender, IDownloadOperation operation) =>
        Downloads.AddDownload(operation);

    private void OnTabStateChanged(object? sender, NavigationState state)
    {
        if (!Settings.IsSafeModeEnabled)
        {
            if (!state.IsLoading)
            {
                _recentSites.Record(state.Url, state.Title, state.FaviconUrl);
            }

            if (sender is BrowserTab tab)
            {
                _history.Record(tab.Id, state);
            }
        }

        OnPropertyChanged(nameof(IsActiveTabBookmarked));
    }

    private void OnBookmarksChanged(object? sender, EventArgs e)
    {
        RebuildShortcuts();
        OnPropertyChanged(nameof(IsActiveTabBookmarked));
    }

    private async void OnSavedPageRequested(object? sender, string url)
    {
        if (ActiveTab is null)
        {
            _tabManager.OpenTab();
        }

        if (ActiveTab is { } tab)
        {
            tab.AddressText = url;
            await NavigateAsync(tab, url);
        }
    }

    private bool CanBookmarkActiveTab()
    {
        if (_bookmarks is null || ActiveTab is not { } tab)
        {
            return false;
        }

        var url = !string.IsNullOrWhiteSpace(tab.Tab.State.Url) ? tab.Tab.State.Url : tab.AddressText;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private void ToggleActiveTabBookmark()
    {
        if (_bookmarks is null || ActiveTab is not { } tab)
        {
            return;
        }

        var state = tab.Tab.State;
        var url = !string.IsNullOrWhiteSpace(state.Url) ? state.Url : tab.AddressText;
        var title = !string.IsNullOrWhiteSpace(state.Title) ? state.Title : tab.Title;
        _bookmarks.Toggle(url, title, state.FaviconUrl);
        OnPropertyChanged(nameof(IsActiveTabBookmarked));
    }

    private void OnRecentSitesChanged(object? sender, EventArgs e)
    {
        // Recent sites changed
    }

    private void RebuildShortcuts()
    {
        var items = _bookmarks?.Items.Take(6).Select(b => new ShortcutSlot(b)).ToList() ?? new List<ShortcutSlot>();
        Shortcuts = items;
        OnPropertyChanged(nameof(HasShortcuts));
    }

    private Task AcceptSuggestionAsync(object? parameter)
    {
        var suggestion = parameter switch
        {
            SuggestionItemViewModel item => item.Suggestion,
            Suggestion raw => raw,
            _ => null,
        };

        if (suggestion is null || ActiveTab is not { } tab)
        {
            return Task.CompletedTask;
        }

        tab.AddressText = suggestion.Text;
        return NavigateAsync(tab, suggestion.Text);
    }

    private void OnActiveTabChanged(object? sender, BrowserTab? active)
    {
        Omnibox.Close();
        foreach (var tab in Tabs)
        {
            tab.IsActive = ReferenceEquals(tab.Tab, active);
        }

        ActiveTab = Tabs.FirstOrDefault(t => t.IsActive);
    }
}
