using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.Core.Services;

/// <summary>
/// Owns the tab list and the active tab, and is the single place where tabs are created and destroyed.
/// Not thread-safe: use from the UI thread.
/// </summary>
public sealed class TabManager : IDisposable
{
    private readonly IWebViewHostFactory _hostFactory;
    private readonly ILogger<TabManager> _logger;
    private readonly List<BrowserTab> _tabs = new();

    public TabManager(IWebViewHostFactory hostFactory, ILogger<TabManager> logger)
    {
        _hostFactory = hostFactory;
        _logger = logger;
    }

    public IReadOnlyList<BrowserTab> Tabs => _tabs;

    /// <summary>The selected tab, or <c>null</c> when no tab is open.</summary>
    public BrowserTab? ActiveTab { get; private set; }

    public event EventHandler<BrowserTab>? TabOpened;

    /// <summary>Raised after the tab is removed from <see cref="Tabs"/> but before it is disposed.</summary>
    public event EventHandler<BrowserTab>? TabClosed;

    public event EventHandler<BrowserTab?>? ActiveTabChanged;

    /// <summary>Creates a new blank tab and makes it active.</summary>
    public BrowserTab OpenTab()
    {
        var tab = new BrowserTab(_hostFactory.Create());
        _tabs.Add(tab);
        _logger.LogInformation("Tab {TabId} opened ({Count} open)", tab.Id, _tabs.Count);
        TabOpened?.Invoke(this, tab);
        SetActive(tab);
        return tab;
    }

    public void SelectTab(BrowserTab tab)
    {
        if (_tabs.Contains(tab))
        {
            SetActive(tab);
        }
    }

    /// <summary>Closes a tab. If it was active, its right neighbour (or else left) becomes active.</summary>
    public void CloseTab(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasActive = ReferenceEquals(tab, ActiveTab);
        _tabs.RemoveAt(index);

        if (wasActive)
        {
            BrowserTab? next = _tabs.Count == 0 ? null : _tabs[Math.Min(index, _tabs.Count - 1)];
            SetActive(next);
        }

        TabClosed?.Invoke(this, tab);
        DisposeTab(tab);
        _logger.LogInformation("Tab {TabId} closed ({Count} open)", tab.Id, _tabs.Count);
    }

    public void Dispose()
    {
        foreach (var tab in _tabs)
        {
            DisposeTab(tab);
        }

        _tabs.Clear();
        ActiveTab = null;
    }

    private void SetActive(BrowserTab? tab)
    {
        if (ReferenceEquals(tab, ActiveTab))
        {
            return;
        }

        ActiveTab = tab;
        ActiveTabChanged?.Invoke(this, tab);
    }

    private void DisposeTab(BrowserTab tab)
    {
        try
        {
            tab.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose tab {TabId}", tab.Id);
        }
    }
}
