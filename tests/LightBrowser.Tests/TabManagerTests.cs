using LightBrowser.Core.Models;
using LightBrowser.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightBrowser.Tests;

public class TabManagerTests
{
    private readonly FakeWebViewHostFactory _factory = new();
    private readonly TabManager _manager;

    public TabManagerTests()
    {
        _manager = new TabManager(_factory, NullLogger<TabManager>.Instance);
    }

    [Fact]
    public void OpenTab_AddsAndActivatesTab()
    {
        var tab = _manager.OpenTab();

        Assert.Single(_manager.Tabs);
        Assert.Same(tab, _manager.ActiveTab);
        Assert.Single(_factory.Created);
    }

    [Fact]
    public void OpenTab_EachTabGetsItsOwnHost_AndLatestBecomesActive()
    {
        var first = _manager.OpenTab();
        var second = _manager.OpenTab();

        Assert.Equal(2, _factory.Created.Count);
        Assert.NotSame(first.View, second.View);
        Assert.Same(second, _manager.ActiveTab);
    }

    [Fact]
    public void SelectTab_ChangesActiveTab_AndRaisesEvent()
    {
        var first = _manager.OpenTab();
        _manager.OpenTab();
        BrowserTab? raised = null;
        _manager.ActiveTabChanged += (_, t) => raised = t;

        _manager.SelectTab(first);

        Assert.Same(first, _manager.ActiveTab);
        Assert.Same(first, raised);
    }

    [Fact]
    public void CloseTab_DisposesHost_AndRemovesTab()
    {
        var tab = _manager.OpenTab();
        var host = _factory.Created[0];

        _manager.CloseTab(tab);

        Assert.Empty(_manager.Tabs);
        Assert.True(host.IsDisposed);
    }

    [Fact]
    public void CloseTab_Active_SelectsRightNeighbour_ElseLeft()
    {
        var a = _manager.OpenTab();
        var b = _manager.OpenTab();
        var c = _manager.OpenTab();

        _manager.SelectTab(b);
        _manager.CloseTab(b);
        Assert.Same(c, _manager.ActiveTab);

        _manager.CloseTab(c);
        Assert.Same(a, _manager.ActiveTab);
    }

    [Fact]
    public void CloseTab_Inactive_KeepsActiveTab()
    {
        var a = _manager.OpenTab();
        var b = _manager.OpenTab();

        _manager.CloseTab(a);

        Assert.Same(b, _manager.ActiveTab);
    }

    [Fact]
    public void CloseTab_Last_LeavesNoActiveTab_WithoutThrowing()
    {
        var tab = _manager.OpenTab();

        _manager.CloseTab(tab);

        Assert.Null(_manager.ActiveTab);
        Assert.Empty(_manager.Tabs);
    }

    [Fact]
    public void CloseTab_Unknown_IsIgnored()
    {
        var other = new TabManager(_factory, NullLogger<TabManager>.Instance).OpenTab();
        var tab = _manager.OpenTab();

        _manager.CloseTab(other);

        Assert.Single(_manager.Tabs);
        Assert.Same(tab, _manager.ActiveTab);
    }

    [Fact]
    public void CloseTab_RaisesTabClosed_BeforeDisposingHost()
    {
        var tab = _manager.OpenTab();
        var host = _factory.Created[0];
        var disposedAtEvent = true;
        _manager.TabClosed += (_, _) => disposedAtEvent = host.IsDisposed;

        _manager.CloseTab(tab);

        Assert.False(disposedAtEvent);
        Assert.True(host.IsDisposed);
    }

    [Fact]
    public void Dispose_DisposesAllHosts()
    {
        _manager.OpenTab();
        _manager.OpenTab();

        _manager.Dispose();

        Assert.All(_factory.Created, h => Assert.True(h.IsDisposed));
        Assert.Empty(_manager.Tabs);
    }

    [Fact]
    public void Tab_ForwardsHostStateChanges_UntilDisposed()
    {
        var tab = _manager.OpenTab();
        var host = _factory.Created[0];
        var received = new List<NavigationState>();
        tab.StateChanged += (_, s) => received.Add(s);

        host.Raise(new NavigationState("https://a.test/", "A", false, false, true));
        _manager.CloseTab(tab);
        host.Raise(new NavigationState("https://b.test/", "B", false, false, false));

        Assert.Single(received);
        Assert.Equal("https://a.test/", received[0].Url);
    }
}
