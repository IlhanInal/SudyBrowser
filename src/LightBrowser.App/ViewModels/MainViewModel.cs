using System.Collections.ObjectModel;
using System.Windows.Input;
using LightBrowser.App.Models;
using LightBrowser.Core.Interfaces;
using LightBrowser.Core.Models;
using LightBrowser.Core.Services;
using Microsoft.Extensions.Logging;

namespace LightBrowser.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly TabManager _tabManager;
    private readonly IAddressParser _addressParser;
    private readonly RecentSites _recentSites;
    private readonly ILogger<MainViewModel> _logger;
    private TabViewModel? _activeTab;
    private string _homeSearchText = string.Empty;
    private IReadOnlyList<ShortcutSlot> _shortcuts = Array.Empty<ShortcutSlot>();

    public MainViewModel(
        TabManager tabManager,
        IAddressParser addressParser,
        RecentSites recentSites,
        StatusViewModel status,
        SettingsViewModel settings,
        ILogger<MainViewModel> logger)
    {
        _tabManager = tabManager;
        _addressParser = addressParser;
        _recentSites = recentSites;
        _logger = logger;
        Status = status;
        Settings = settings;

        _tabManager.TabOpened += OnTabOpened;
        _tabManager.TabClosed += OnTabClosed;
        _tabManager.ActiveTabChanged += OnActiveTabChanged;
        _recentSites.Changed += OnRecentSitesChanged;
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
    }

    public StatusViewModel Status { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>The fixed three home page tiles; unused slots have no site.</summary>
    public IReadOnlyList<ShortcutSlot> Shortcuts
    {
        get => _shortcuts;
        private set => SetProperty(ref _shortcuts, value);
    }

    /// <summary>Text of the home page search box.</summary>
    public string HomeSearchText
    {
        get => _homeSearchText;
        set => SetProperty(ref _homeSearchText, value);
    }

    public ICommand HomeSearchCommand { get; }

    public ICommand OpenShortcutCommand { get; }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (SetProperty(ref _activeTab, value))
            {
                OnPropertyChanged(nameof(SelectedTab));
            }
        }
    }

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

        foreach (var tab in Tabs)
        {
            tab.Tab.StateChanged -= OnTabStateChanged;
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
        slot?.Site is { } site ? NavigateAsync(ActiveTab, site.Url) : Task.CompletedTask;

    private void OnTabOpened(object? sender, BrowserTab tab)
    {
        tab.StateChanged += OnTabStateChanged;
        Tabs.Add(new TabViewModel(tab));
    }

    private void OnTabClosed(object? sender, BrowserTab tab)
    {
        tab.StateChanged -= OnTabStateChanged;
        var viewModel = Tabs.FirstOrDefault(t => ReferenceEquals(t.Tab, tab));
        if (viewModel is not null)
        {
            Tabs.Remove(viewModel);
            viewModel.Dispose();
        }
    }

    private void OnTabStateChanged(object? sender, NavigationState state)
    {
        if (!state.IsLoading)
        {
            _recentSites.Record(state.Url, state.Title, state.FaviconUrl);
        }
    }

    private void OnRecentSitesChanged(object? sender, EventArgs e) => RebuildShortcuts();

    private void RebuildShortcuts() =>
        Shortcuts = Enumerable.Range(0, RecentSites.MaxCount)
            .Select(i => new ShortcutSlot(i < _recentSites.Items.Count ? _recentSites.Items[i] : null))
            .ToList();

    private void OnActiveTabChanged(object? sender, BrowserTab? active)
    {
        foreach (var tab in Tabs)
        {
            tab.IsActive = ReferenceEquals(tab.Tab, active);
        }

        ActiveTab = Tabs.FirstOrDefault(t => t.IsActive);
    }
}
