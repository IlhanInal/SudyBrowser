using SudyBrowser.App.Services;
using SudyBrowser.App.ViewModels;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;
using SudyBrowser.Core.Services;
using SudyBrowser.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace SudyBrowser.Tests;

public sealed class SettingsAndBookmarksTests
{
    private sealed class InMemoryBookmarkStore : IBookmarkStore
    {
        public List<BookmarkEntry> Saved { get; private set; } = new();

        public IReadOnlyList<BookmarkEntry> Load() => Saved.ToList();

        public void Save(IReadOnlyList<BookmarkEntry> bookmarks) => Saved = bookmarks.ToList();
    }

    private sealed class InMemoryRecentSitesStore : IRecentSitesStore
    {
        public List<RecentSite> Saved { get; } = new();

        public IReadOnlyList<RecentSite> Load() => Saved;

        public void Save(IReadOnlyList<RecentSite> sites)
        {
            Saved.Clear();
            Saved.AddRange(sites);
        }
    }

    private sealed class FakeMetricsProvider : ISystemMetricsProvider
    {
        public SystemMetrics Read() => new(0, 0, 0, 0);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public UserSettings Current { get; set; } = new();

        public UserSettings Load() => Current;

        public void Save(UserSettings settings) => Current = settings;
    }

    private sealed class FakeHistoryStore : IHistoryStore
    {
        private readonly List<HistoryEntry> _entries = new();

        public Task RecordVisitAsync(string url, string title, bool countVisit, string faviconUrl = "", CancellationToken cancellationToken = default)
        {
            _entries.RemoveAll(e => e.Url == url);
            _entries.Insert(0, new HistoryEntry(url, title, 1, DateTime.UtcNow, faviconUrl));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HistoryEntry>> SearchAsync(string normalizedQuery, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HistoryEntry>>(
                _entries.Where(e => e.Url.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                                 || e.Title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                        .Take(limit)
                        .ToList());

        public Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HistoryEntry>>(_entries.Take(limit).ToList());

        public Task DeleteAsync(string url, CancellationToken cancellationToken = default)
        {
            _entries.RemoveAll(e => e.Url == url);
            return Task.CompletedTask;
        }

        public Task DeleteSinceAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
        {
            if (cutoffUtc <= DateTime.MinValue)
            {
                _entries.Clear();
            }
            else
            {
                _entries.RemoveAll(e => e.LastVisitUtc >= cutoffUtc);
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public void BookmarkService_ToggleAddsAndRemovesBookmark()
    {
        var store = new InMemoryBookmarkStore();
        var service = new BookmarkService(store);

        var added = service.Toggle("https://www.trendyol.com/", "trendyol.com", "https://www.trendyol.com/favicon.ico");
        Assert.True(added);
        Assert.True(service.IsBookmarked("https://trendyol.com"));
        Assert.Single(service.Items);
        Assert.Single(store.Saved);

        var stillBookmarked = service.Toggle("https://trendyol.com/", "trendyol.com");
        Assert.False(stillBookmarked);
        Assert.False(service.IsBookmarked("https://www.trendyol.com/"));
        Assert.Empty(service.Items);
    }

    [Fact]
    public void JsonBookmarkStore_RoundTripsBookmarksWithoutHardcodedPaths()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"lb-bookmarks-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonBookmarkStore(tempFile, NullLogger<JsonBookmarkStore>.Instance);
            var service = new BookmarkService(store);
            service.AddOrUpdate("https://www.ikea.com.tr/berjer", "ikea berjer", "https://www.ikea.com.tr/favicon.ico");

            var reloaded = new BookmarkService(store);
            Assert.Single(reloaded.Items);
            Assert.Equal("ikea berjer", reloaded.Items[0].Title);
            Assert.True(reloaded.IsBookmarked("https://ikea.com.tr/berjer"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task SqliteHistoryStore_GetRecentAndDelete_WorkAsExpected()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"lb-history-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new SqliteHistoryStore(dbPath, NullLogger<SqliteHistoryStore>.Instance))
            {
                await store.RecordVisitAsync("https://www.trendyol.com/", "trendyol.com", true);
                await store.RecordVisitAsync("https://www.kigili.com/gomlek", "Kığılı gömlek no :32", true);

                var recent = await store.GetRecentAsync(10);
                Assert.Equal(2, recent.Count);
                Assert.Equal("Kığılı gömlek no :32", recent[0].Title);

                await store.DeleteAsync("https://www.kigili.com/gomlek");
                var afterDelete = await store.GetRecentAsync(10);
                Assert.Single(afterDelete);
                Assert.Equal("trendyol.com", afterDelete[0].Title);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { /* ignore WAL lock */ }
            }
        }
    }

    [Fact]
    public void SettingsViewModel_NavigatesBetweenSubViewsAndReleasesMemoryOnClose()
    {
        var store = new InMemoryBookmarkStore();
        var bookmarks = new BookmarkService(store);
        bookmarks.AddOrUpdate("https://www.trendyol.com/", "trendyol.com");
        bookmarks.AddOrUpdate("https://www.ikea.com.tr/berjer", "ikea berjer");

        var history = new FakeHistoryStore();
        var downloads = new DownloadsViewModel();
        using var settings = new SettingsViewModel(new ThemeService(), bookmarks, history, null, downloads);

        settings.OpenCommand.Execute(null);
        Assert.True(settings.IsOpen);
        Assert.True(settings.IsMainView);

        // Open Yer İmleri
        settings.OpenBookmarksCommand.Execute(null);
        Assert.True(settings.IsBookmarksView);
        Assert.Equal(2, settings.BookmarkItems.Count);
        Assert.Equal("ikea berjer", settings.BookmarkItems[0].Title);

        // Back button returns to Main
        settings.BackToMainCommand.Execute(null);
        Assert.True(settings.IsMainView);
        Assert.Empty(settings.BookmarkItems);

        // Open Renk Temaları
        settings.OpenThemesCommand.Execute(null);
        Assert.True(settings.IsThemesView);

        // Safe Mode & Performance Mode toggles
        settings.ToggleSafeModeCommand.Execute(null);
        Assert.True(settings.IsSafeModeEnabled);
        settings.TogglePerformanceModeCommand.Execute(null);
        Assert.True(settings.IsPerformanceModeEnabled);

        // Clicking İndirmeler closes Settings and opens Downloads popup
        settings.OpenDownloadsCommand.Execute(null);
        Assert.False(settings.IsOpen);
        Assert.True(downloads.IsOpen);
    }

    [Fact]
    public void SettingsViewModel_ClickingBookmarkRowRaisesSavedPageRequestedAndClosesPopup()
    {
        var store = new InMemoryBookmarkStore();
        var bookmarks = new BookmarkService(store);
        bookmarks.AddOrUpdate("https://www.trendyol.com/", "trendyol.com");

        using var settings = new SettingsViewModel(new ThemeService(), bookmarks);
        string? requestedUrl = null;
        settings.SavedPageRequested += (_, url) => requestedUrl = url;

        settings.OpenCommand.Execute(null);
        settings.OpenBookmarksCommand.Execute(null);
        var row = Assert.Single(settings.BookmarkItems);

        settings.OpenSavedPageCommand.Execute(row);
        Assert.Equal("https://www.trendyol.com/", requestedUrl);
        Assert.False(settings.IsOpen);
    }

    [Fact]
    public async Task SettingsViewModel_HistorySearchSelectionAndClearView_WorkAsExpected()
    {
        var history = new FakeHistoryStore();
        await history.RecordVisitAsync("https://www.trendyol.com/", "trendyol.com", true);
        await history.RecordVisitAsync("https://www.kigili.com/gomlek", "Kığılı gömlek no :32", true);

        using var settings = new SettingsViewModel(new ThemeService(), null, history);
        settings.OpenCommand.Execute(null);
        settings.OpenHistoryCommand.Execute(null);

        // Wait briefly for async history load
        for (var i = 0; i < 20 && settings.HistoryItems.Count < 2; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, settings.HistoryItems.Count);
        Assert.True(settings.HistoryItems[0].ShowDateHeader);
        Assert.StartsWith("Bugün - ", settings.HistoryItems[0].DateHeader);
        Assert.Equal("Geçmişi Temizle", settings.ClearHistoryButtonText);

        // Search filter
        settings.HistorySearchText = "trendyol";
        Assert.Single(settings.HistoryItems);
        Assert.Equal("trendyol.com", settings.HistoryItems[0].Title);

        settings.HistorySearchText = string.Empty;
        Assert.Equal(2, settings.HistoryItems.Count);

        // Selecting a checkbox switches button text to "Seçilen Verileri Temizle"
        settings.HistoryItems[0].IsSelected = true;
        Assert.True(settings.HasSelectedHistoryItems);
        Assert.Equal("Seçilen Verileri Temizle", settings.ClearHistoryButtonText);

        // Clicking red button when item is selected deletes only the selected item
        settings.HistoryClearActionCommand.Execute(null);
        Assert.Single(settings.HistoryItems);
        Assert.False(settings.HasSelectedHistoryItems);
        Assert.Equal("Geçmişi Temizle", settings.ClearHistoryButtonText);

        // Clicking red button when nothing is selected opens ClearHistory view with 5 options
        settings.HistoryClearActionCommand.Execute(null);
        Assert.True(settings.IsClearHistoryView);
        Assert.Equal(5, settings.ClearHistoryOptions.Count);
        Assert.Equal("Son yarım saat", settings.ClearHistoryOptions[0].Label);
        Assert.Equal("Son bir saat", settings.ClearHistoryOptions[1].Label);
        Assert.Equal("Son bir gün", settings.ClearHistoryOptions[2].Label);
        Assert.Equal("Son bir hafta", settings.ClearHistoryOptions[3].Label);
        Assert.Equal("Hepsi", settings.ClearHistoryOptions[4].Label);

        // Clearing by range deletes remaining items and returns to History view
        settings.ClearHistoryByRangeCommand.Execute(settings.ClearHistoryOptions[4]);
        Assert.True(settings.IsHistoryView);
    }

    [Fact]
    public void JsonSettingsStore_RoundTripsUserSettings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"lb-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(tempFile, NullLogger<JsonSettingsStore>.Instance);
            var initial = store.Load();
            Assert.Equal("Light", initial.ThemeFile);
            Assert.Equal("mountain", initial.BackgroundFile);
            Assert.False(initial.OnlineSuggestionsEnabled);

            store.Save(new UserSettings("dark", "gradient_silver", true));

            var reloaded = store.Load();
            Assert.Equal("dark", reloaded.ThemeFile);
            Assert.Equal("gradient_silver", reloaded.BackgroundFile);
            Assert.True(reloaded.OnlineSuggestionsEnabled);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void SettingsViewModel_LoadsAndPersistsSettings()
    {
        var fakeStore = new FakeSettingsStore
        {
            Current = new UserSettings("dark", "gradient_silver", true)
        };

        using var settings = new SettingsViewModel(
            new ThemeService(),
            bookmarks: null,
            historyStore: null,
            faviconCache: null,
            downloads: null,
            settingsStore: fakeStore);

        Assert.Equal("dark", settings.SelectedTheme.Key);
        Assert.Equal("gradient_silver", settings.SelectedBackground.Key);
        Assert.True(settings.OnlineSuggestionsEnabled);

        // Selecting a different theme auto-saves
        var redTheme = settings.Themes.First(t => t.Key == "red");
        settings.SelectThemeCommand.Execute(redTheme);
        Assert.Equal("red", fakeStore.Current.ThemeFile);

        // Selecting a different background auto-saves
        var sunsetBg = settings.Backgrounds.First(b => b.Key == "gradient_sunset");
        settings.SelectBackgroundCommand.Execute(sunsetBg);
        Assert.Equal("gradient_sunset", fakeStore.Current.BackgroundFile);
    }

    [Fact]
    public void MainViewModel_Shortcuts_HiddenWhenNoBookmarks_AndVisibleWithMaxSix()
    {
        var bookmarkStore = new InMemoryBookmarkStore();
        var bookmarkService = new BookmarkService(bookmarkStore);

        var factory = new FakeWebViewHostFactory();
        var tabManager = new TabManager(factory, NullLogger<TabManager>.Instance);
        var recentSites = new RecentSites(new InMemoryRecentSitesStore());
        var historyStore = new FakeHistoryStore();
        var history = new HistoryRecorder(historyStore);
        var settings = new SettingsViewModel(new ThemeService());
        var omnibox = new OmniboxViewModel(
            new SuggestionService(historyStore, new FakeSuggestionProvider()),
            new AddressParser(),
            settings,
            NullLogger<OmniboxViewModel>.Instance);
        var downloads = new DownloadsViewModel();
        var status = new StatusViewModel(new FakeMetricsProvider());

        using var mainVm = new MainViewModel(
            tabManager,
            new AddressParser(),
            recentSites,
            history,
            omnibox,
            downloads,
            status,
            settings,
            NullLogger<MainViewModel>.Instance,
            bookmarkService);

        // No bookmarks: Shortcuts empty, HasShortcuts false
        Assert.Empty(mainVm.Shortcuts);
        Assert.False(mainVm.HasShortcuts);

        // Add 3 bookmarks
        bookmarkService.AddOrUpdate("https://google.com", "Google");
        bookmarkService.AddOrUpdate("https://github.com", "GitHub");
        bookmarkService.AddOrUpdate("https://microsoft.com", "Microsoft");

        Assert.Equal(3, mainVm.Shortcuts.Count);
        Assert.True(mainVm.HasShortcuts);
        Assert.Equal("Microsoft", mainVm.Shortcuts[0].Title);

        // Add 5 more bookmarks (total 8) -> Shortcuts should cap at exactly 6
        for (int i = 1; i <= 5; i++)
        {
            bookmarkService.AddOrUpdate($"https://site{i}.com", $"Site {i}");
        }

        Assert.Equal(6, mainVm.Shortcuts.Count);
        Assert.True(mainVm.HasShortcuts);
        Assert.Equal("Site 5", mainVm.Shortcuts[0].Title);

        // Removing bookmarks until empty
        foreach (var b in bookmarkService.Items.ToList())
        {
            bookmarkService.Remove(b.Url);
        }

        Assert.Empty(mainVm.Shortcuts);
        Assert.False(mainVm.HasShortcuts);
    }
}
