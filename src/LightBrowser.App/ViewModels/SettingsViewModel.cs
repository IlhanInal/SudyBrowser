using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SudyBrowser.App.Models;
using SudyBrowser.App.Services;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Services;

namespace SudyBrowser.App.ViewModels;

public enum SettingsSubView
{
    Main = 0,
    Bookmarks = 1,
    History = 2,
    Themes = 3,
    ClearHistory = 4,
}

public enum HistoryClearRange
{
    LastHalfHour = 0,
    LastHour = 1,
    LastDay = 2,
    LastWeek = 3,
    All = 4,
}

public sealed record ClearHistoryOption(string Label, HistoryClearRange Range);

/// <summary>
/// Manages the multi-view Settings popup (Main 6-tile grid, Yer İmleri, Arama Geçmişi, Geçmişi Temizle, Renk Temaları).
/// Sub-view collections are populated lazily on demand and cleared when the popup closes to keep RAM minimal.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private const string PackPrefix = "pack://application:,,,/Resources/";
    private const int MaxHistoryDisplayCount = 80;
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    static SettingsViewModel()
    {
        // Ensure the "pack://" URI scheme is registered when running inside headless unit tests.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
    }

    private readonly ThemeService _themeService;
    private readonly BookmarkService? _bookmarks;
    private readonly IHistoryStore? _historyStore;
    private readonly FaviconCacheService? _faviconCache;
    private readonly DownloadsViewModel? _downloads;
    private readonly ISettingsStore? _settingsStore;
    private readonly List<SavedPageItemViewModel> _allHistoryItems = new();

    private BackgroundOption _selectedBackground;
    private ThemeOption _selectedTheme;
    private bool _isOpen;
    private bool _onlineSuggestionsEnabled;
    private bool _isSafeModeEnabled;
    private bool _isPerformanceModeEnabled;
    private SettingsSubView _currentView = SettingsSubView.Main;
    private int _currentPageIndex;
    private string _historySearchText = string.Empty;
    private CancellationTokenSource? _loadCts;

    public SettingsViewModel(
        ThemeService themeService,
        BookmarkService? bookmarks = null,
        IHistoryStore? historyStore = null,
        FaviconCacheService? faviconCache = null,
        DownloadsViewModel? downloads = null,
        ISettingsStore? settingsStore = null)
    {
        _themeService = themeService;
        _bookmarks = bookmarks;
        _historyStore = historyStore;
        _faviconCache = faviconCache;
        _downloads = downloads;
        _settingsStore = settingsStore;

        Backgrounds = new[]
        {
            Background("Yeşil Tepeler", "mountain"),
            Background("Nehir Vadisi", "coast"),
            Background("Orman ve Deniz", "forest"),
            Background("Karlı Dağlar", "lake"),
            Background("Kış Kampı", "snow"),
            Background("Fiyort", "valley"),
            Background("Açık Gri Grad.", "gradient_silver"),
            Background("Gece Mavisi Grad.", "gradient_midnight"),
            Background("Gün Batımı Grad.", "gradient_sunset"),
            Background("Zümrüt Yeşili Grad.", "gradient_emerald"),
        };
        Themes = new[]
        {
            Theme("Açık gri", "Light", "#D9D9D9"),
            Theme("Renkli (mor)", "Colorful", "#8E73E8"),
            Theme("Kırmızı", "red", "#ce3c37"),
            Theme("Dark", "dark", "#070707"),
        };
        ClearHistoryOptions = new[]
        {
            new ClearHistoryOption("Son yarım saat", HistoryClearRange.LastHalfHour),
            new ClearHistoryOption("Son bir saat", HistoryClearRange.LastHour),
            new ClearHistoryOption("Son bir gün", HistoryClearRange.LastDay),
            new ClearHistoryOption("Son bir hafta", HistoryClearRange.LastWeek),
            new ClearHistoryOption("Hepsi", HistoryClearRange.All),
        };

        var loaded = _settingsStore?.Load();
        if (loaded is not null)
        {
            _selectedTheme = Themes.FirstOrDefault(t => string.Equals(t.Key, loaded.ThemeFile, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
            _selectedBackground = Backgrounds.FirstOrDefault(b => string.Equals(b.Key, loaded.BackgroundFile, StringComparison.OrdinalIgnoreCase)) ?? Backgrounds[0];
            _onlineSuggestionsEnabled = loaded.OnlineSuggestionsEnabled;
        }
        else
        {
            _selectedBackground = Backgrounds[0];
            _selectedTheme = Themes[0];
        }

        _themeService.Apply(_selectedTheme);

        if (_bookmarks is not null)
        {
            _bookmarks.Changed += OnBookmarksChanged;
        }

        OpenCommand = new RelayCommand(_ =>
        {
            CurrentView = SettingsSubView.Main;
            IsOpen = !IsOpen;
        });
        CloseCommand = new RelayCommand(_ => IsOpen = false);
        BackToMainCommand = new RelayCommand(_ =>
        {
            if (CurrentView == SettingsSubView.ClearHistory)
            {
                SwitchView(SettingsSubView.History);
            }
            else
            {
                SwitchView(SettingsSubView.Main);
            }
        });

        OpenBookmarksCommand = new RelayCommand(_ => SwitchView(SettingsSubView.Bookmarks));
        OpenHistoryCommand = new RelayCommand(_ => SwitchView(SettingsSubView.History));
        OpenThemesCommand = new RelayCommand(_ => SwitchView(SettingsSubView.Themes));
        OpenDownloadsCommand = new RelayCommand(_ =>
        {
            IsOpen = false;
            if (_downloads is not null)
            {
                _downloads.IsOpen = true;
            }
        });

        ToggleSafeModeCommand = new RelayCommand(_ => IsSafeModeEnabled = !IsSafeModeEnabled);
        TogglePerformanceModeCommand = new RelayCommand(_ => IsPerformanceModeEnabled = !IsPerformanceModeEnabled);

        PreviousPageCommand = new RelayCommand(_ =>
        {
            if (CurrentPageIndex > 0)
            {
                CurrentPageIndex--;
            }
        });
        NextPageCommand = new RelayCommand(_ =>
        {
            CurrentPageIndex = 0;
        });

        OpenSavedPageCommand = new RelayCommand(OnOpenSavedPage);
        RemoveBookmarkItemCommand = new RelayCommand(OnRemoveBookmarkItem);
        RemoveHistoryItemCommand = new RelayCommand(async p => await OnRemoveHistoryItemAsync(p));
        HistoryClearActionCommand = new RelayCommand(async _ => await OnHistoryClearActionAsync());
        ClearHistoryByRangeCommand = new RelayCommand(async p => await OnClearHistoryByRangeAsync(p));

        SelectThemeCommand = new RelayCommand(p =>
        {
            if (p is ThemeOption theme)
            {
                SelectedTheme = theme;
            }
        });
        SelectBackgroundCommand = new RelayCommand(p =>
        {
            if (p is BackgroundOption bg)
            {
                SelectedBackground = bg;
            }
        });
    }

    /// <summary>Raised when the user clicks a bookmark or history pill to navigate to that URL.</summary>
    public event EventHandler<string>? SavedPageRequested;

    public IReadOnlyList<BackgroundOption> Backgrounds { get; }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public IReadOnlyList<ClearHistoryOption> ClearHistoryOptions { get; }

    public ObservableCollection<SavedPageItemViewModel> BookmarkItems { get; } = new();

    public ObservableCollection<SavedPageItemViewModel> HistoryItems { get; } = new();

    public bool HasBookmarkItems => BookmarkItems.Count > 0;

    public bool HasHistoryItems => HistoryItems.Count > 0;

    public bool HasSelectedHistoryItems => _allHistoryItems.Any(i => i.IsSelected);

    public string ClearHistoryButtonText =>
        HasSelectedHistoryItems ? "Seçilen Verileri Temizle" : "Geçmişi Temizle";

    public string HistorySearchText
    {
        get => _historySearchText;
        set
        {
            if (SetProperty(ref _historySearchText, value))
            {
                OnPropertyChanged(nameof(IsHistorySearchEmpty));
                ApplyHistoryFilter();
            }
        }
    }

    public bool IsHistorySearchEmpty => string.IsNullOrEmpty(_historySearchText);

    public ICommand OpenCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand BackToMainCommand { get; }

    public ICommand OpenBookmarksCommand { get; }

    public ICommand OpenHistoryCommand { get; }

    public ICommand OpenThemesCommand { get; }

    public ICommand OpenDownloadsCommand { get; }

    public ICommand ToggleSafeModeCommand { get; }

    public ICommand TogglePerformanceModeCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand OpenSavedPageCommand { get; }

    public ICommand RemoveBookmarkItemCommand { get; }

    public ICommand RemoveHistoryItemCommand { get; }

    public ICommand HistoryClearActionCommand { get; }

    public ICommand ClearHistoryByRangeCommand { get; }

    public ICommand SelectThemeCommand { get; }

    public ICommand SelectBackgroundCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (SetProperty(ref _isOpen, value))
            {
                if (!value)
                {
                    ReleaseSubViewMemory();
                    CurrentView = SettingsSubView.Main;
                }
            }
        }
    }

    public SettingsSubView CurrentView
    {
        get => _currentView;
        private set
        {
            if (SetProperty(ref _currentView, value))
            {
                OnPropertyChanged(nameof(IsMainView));
                OnPropertyChanged(nameof(IsBookmarksView));
                OnPropertyChanged(nameof(IsHistoryView));
                OnPropertyChanged(nameof(IsThemesView));
                OnPropertyChanged(nameof(IsClearHistoryView));
            }
        }
    }

    public bool IsMainView => CurrentView == SettingsSubView.Main;

    public bool IsBookmarksView => CurrentView == SettingsSubView.Bookmarks;

    public bool IsHistoryView => CurrentView == SettingsSubView.History;

    public bool IsThemesView => CurrentView == SettingsSubView.Themes;

    public bool IsClearHistoryView => CurrentView == SettingsSubView.ClearHistory;

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        private set => SetProperty(ref _currentPageIndex, value);
    }

    /// <summary>When enabled, visits are not recorded to history/recent sites and online suggestions are paused.</summary>
    public bool IsSafeModeEnabled
    {
        get => _isSafeModeEnabled;
        set
        {
            if (SetProperty(ref _isSafeModeEnabled, value) && value)
            {
                OnlineSuggestionsEnabled = false;
            }
        }
    }

    /// <summary>When enabled, aggressively trims unused memory caches.</summary>
    public bool IsPerformanceModeEnabled
    {
        get => _isPerformanceModeEnabled;
        set
        {
            if (SetProperty(ref _isPerformanceModeEnabled, value) && value)
            {
                GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
            }
        }
    }

    /// <summary>
    /// Whether typed text is sent to the online suggestion service. Off by default because it leaves the machine.
    /// </summary>
    public bool OnlineSuggestionsEnabled
    {
        get => _onlineSuggestionsEnabled;
        set
        {
            if (SetProperty(ref _onlineSuggestionsEnabled, value))
            {
                SaveSettings();
            }
        }
    }

    public BackgroundOption SelectedBackground
    {
        get => _selectedBackground;
        set
        {
            if (value is not null && SetProperty(ref _selectedBackground, value))
            {
                SaveSettings();
            }
        }
    }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is not null && SetProperty(ref _selectedTheme, value))
            {
                _themeService.Apply(value);
                SaveSettings();
            }
        }
    }

    public void SwitchView(SettingsSubView view)
    {
        CancelPendingLoads();
        CurrentView = view;

        switch (view)
        {
            case SettingsSubView.Main:
                ReleaseSubViewMemory();
                break;
            case SettingsSubView.Bookmarks:
                ClearHistoryMemory();
                PopulateBookmarks();
                break;
            case SettingsSubView.History:
                BookmarkItems.Clear();
                OnPropertyChanged(nameof(HasBookmarkItems));
                _ = PopulateHistoryAsync();
                break;
            case SettingsSubView.ClearHistory:
                // Keep history view context ready when returning via Back button.
                break;
            case SettingsSubView.Themes:
                ReleaseSubViewMemory();
                break;
        }
    }

    public void Dispose()
    {
        if (_bookmarks is not null)
        {
            _bookmarks.Changed -= OnBookmarksChanged;
        }

        CancelPendingLoads();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore?.Save(new UserSettings(
                ThemeFile: _selectedTheme?.Key ?? "Light",
                BackgroundFile: _selectedBackground?.Key ?? "mountain",
                OnlineSuggestionsEnabled: _onlineSuggestionsEnabled));
        }
        catch
        {
            // Best effort - do not crash UI thread if storage write fails
        }
    }

    private void OnBookmarksChanged(object? sender, EventArgs e)
    {
        if (IsOpen && IsBookmarksView)
        {
            PopulateBookmarks();
        }
    }

    private void PopulateBookmarks()
    {
        CancelPendingLoads();
        BookmarkItems.Clear();

        if (_bookmarks is null)
        {
            OnPropertyChanged(nameof(HasBookmarkItems));
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;

        foreach (var entry in _bookmarks.Items)
        {
            ImageSource? cached = null;
            var resolvedSync = _faviconCache?.TryGetCached(entry.Url, entry.FaviconUrl, out cached) ?? true;
            var itemVm = new SavedPageItemViewModel(entry.Url, entry.Title, entry.FaviconUrl, string.Empty, cached);
            BookmarkItems.Add(itemVm);

            if (!resolvedSync)
            {
                _ = LoadFaviconAsync(itemVm, cts.Token);
            }
        }

        OnPropertyChanged(nameof(HasBookmarkItems));
    }

    private async Task PopulateHistoryAsync()
    {
        CancelPendingLoads();
        _allHistoryItems.Clear();
        HistoryItems.Clear();
        _historySearchText = string.Empty;
        OnPropertyChanged(nameof(HistorySearchText));
        OnPropertyChanged(nameof(IsHistorySearchEmpty));
        OnPropertyChanged(nameof(HasHistoryItems));
        NotifySelectionStateChanged();

        if (_historyStore is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;

        try
        {
            var entries = await _historyStore.GetRecentAsync(MaxHistoryDisplayCount, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || (CurrentView != SettingsSubView.History && CurrentView != SettingsSubView.ClearHistory))
            {
                return;
            }

            _allHistoryItems.Clear();
            var nowLocal = DateTime.Now;
            foreach (var entry in entries)
            {
                var timeText = FormatHistoryTimeOnly(entry.LastVisitUtc);
                var dateHeader = FormatHistoryDayHeader(entry.LastVisitUtc, nowLocal);
                ImageSource? cached = null;
                var resolvedSync = _faviconCache?.TryGetCached(entry.Url, entry.FaviconUrl, out cached) ?? true;
                var itemVm = new SavedPageItemViewModel(
                    entry.Url,
                    entry.Title,
                    entry.FaviconUrl,
                    timeText,
                    cached,
                    dateHeader,
                    showDateHeader: false,
                    onSelectionChanged: NotifySelectionStateChanged);

                _allHistoryItems.Add(itemVm);

                if (!resolvedSync)
                {
                    _ = LoadFaviconAsync(itemVm, token);
                }
            }

            ApplyHistoryFilter();
        }
        catch (OperationCanceledException)
        {
            // Expected when switching views or closing the popup.
        }
    }

    private void ApplyHistoryFilter()
    {
        HistoryItems.Clear();
        var query = _historySearchText.Trim();
        string? lastHeader = null;

        foreach (var item in _allHistoryItems)
        {
            if (query.Length > 0
                && !item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !item.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            item.ShowDateHeader = !string.IsNullOrEmpty(item.DateHeader)
                                  && !string.Equals(item.DateHeader, lastHeader, StringComparison.Ordinal);
            lastHeader = item.DateHeader;
            HistoryItems.Add(item);
        }

        OnPropertyChanged(nameof(HasHistoryItems));
        NotifySelectionStateChanged();
    }

    private void NotifySelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedHistoryItems));
        OnPropertyChanged(nameof(ClearHistoryButtonText));
    }

    private async Task OnHistoryClearActionAsync()
    {
        var selected = _allHistoryItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            // Open the "Geçmişi Temizle" time-range screen
            SwitchView(SettingsSubView.ClearHistory);
            return;
        }

        // Delete all selected history items
        foreach (var item in selected)
        {
            _allHistoryItems.Remove(item);
        }

        ApplyHistoryFilter();

        if (_historyStore is not null)
        {
            foreach (var item in selected)
            {
                await _historyStore.DeleteAsync(item.Url).ConfigureAwait(false);
            }
        }
    }

    private async Task OnClearHistoryByRangeAsync(object? parameter)
    {
        var range = parameter switch
        {
            ClearHistoryOption opt => opt.Range,
            HistoryClearRange r => r,
            _ => HistoryClearRange.All,
        };

        var nowUtc = DateTime.UtcNow;
        var cutoffUtc = range switch
        {
            HistoryClearRange.LastHalfHour => nowUtc.AddMinutes(-30),
            HistoryClearRange.LastHour => nowUtc.AddHours(-1),
            HistoryClearRange.LastDay => nowUtc.AddDays(-1),
            HistoryClearRange.LastWeek => nowUtc.AddDays(-7),
            _ => DateTime.MinValue,
        };

        if (_historyStore is not null)
        {
            await _historyStore.DeleteSinceAsync(cutoffUtc).ConfigureAwait(true);
        }

        // Return to History screen and refresh the remaining list
        SwitchView(SettingsSubView.History);
    }

    private async Task LoadFaviconAsync(SavedPageItemViewModel item, CancellationToken token)
    {
        if (_faviconCache is null)
        {
            return;
        }

        try
        {
            var icon = await _faviconCache.GetOrFetchAsync(item.Url, item.FaviconUrl, token).ConfigureAwait(false);
            if (icon is null || token.IsCancellationRequested)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                if (!token.IsCancellationRequested)
                {
                    item.Favicon = icon;
                }
            }
            else
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        item.Favicon = icon;
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // View closed or switched.
        }
    }

    private void OnOpenSavedPage(object? parameter)
    {
        var url = parameter switch
        {
            SavedPageItemViewModel item => item.Url,
            string s => s,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        IsOpen = false;
        SavedPageRequested?.Invoke(this, url);
    }

    private void OnRemoveBookmarkItem(object? parameter)
    {
        if (parameter is not SavedPageItemViewModel item)
        {
            return;
        }

        _bookmarks?.Remove(item.Url);
        BookmarkItems.Remove(item);
        OnPropertyChanged(nameof(HasBookmarkItems));
    }

    private async Task OnRemoveHistoryItemAsync(object? parameter)
    {
        if (parameter is not SavedPageItemViewModel item)
        {
            return;
        }

        _allHistoryItems.Remove(item);
        ApplyHistoryFilter();

        if (_historyStore is not null)
        {
            await _historyStore.DeleteAsync(item.Url).ConfigureAwait(false);
        }
    }

    private void ClearHistoryMemory()
    {
        _allHistoryItems.Clear();
        HistoryItems.Clear();
        _historySearchText = string.Empty;
        OnPropertyChanged(nameof(HistorySearchText));
        OnPropertyChanged(nameof(IsHistorySearchEmpty));
        OnPropertyChanged(nameof(HasHistoryItems));
        NotifySelectionStateChanged();
    }

    private void ReleaseSubViewMemory()
    {
        CancelPendingLoads();
        BookmarkItems.Clear();
        OnPropertyChanged(nameof(HasBookmarkItems));
        ClearHistoryMemory();
    }

    private void CancelPendingLoads()
    {
        if (_loadCts is not null)
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = null;
        }
    }

    public static string FormatHistoryTimeOnly(DateTime utcTime)
    {
        if (utcTime == DateTime.MinValue)
        {
            return string.Empty;
        }

        var local = utcTime.Kind == DateTimeKind.Utc ? utcTime.ToLocalTime() : utcTime;
        return local.ToString("HH:mm", TurkishCulture);
    }

    public static string FormatHistoryDayHeader(DateTime utcTime, DateTime nowLocal)
    {
        if (utcTime == DateTime.MinValue)
        {
            return string.Empty;
        }

        var local = utcTime.Kind == DateTimeKind.Utc ? utcTime.ToLocalTime() : utcTime;
        var fullDate = local.ToString("d MMMM yyyy dddd", TurkishCulture);
        if (local.Date == nowLocal.Date)
        {
            return $"Bugün - {fullDate}";
        }

        if (local.Date == nowLocal.Date.AddDays(-1))
        {
            return $"Dün - {fullDate}";
        }

        return fullDate;
    }

    public static string FormatHistoryTimestamp(DateTime utcTime, DateTime nowLocal)
    {
        if (utcTime == DateTime.MinValue)
        {
            return string.Empty;
        }

        var local = utcTime.Kind == DateTimeKind.Utc ? utcTime.ToLocalTime() : utcTime;
        if (local.Date == nowLocal.Date)
        {
            return $"Bugün {local:HH:mm}";
        }

        if (local.Date == nowLocal.Date.AddDays(-1))
        {
            return $"Dün {local:HH:mm}";
        }

        return local.ToString("dd.MM.yyyy HH:mm");
    }

    private static BackgroundOption Background(string name, string file) => new(
        name,
        file,
        new Uri($"{PackPrefix}Backgrounds/{file}.jpg"),
        new Uri($"{PackPrefix}Backgrounds/{file}_thumb.jpg"));

    private static ThemeOption Theme(string name, string file, string swatchColor)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(swatchColor)!;
        brush.Freeze();
        return new ThemeOption(name, file, new Uri($"{PackPrefix}Themes/{file}.xaml"), brush);
    }
}
