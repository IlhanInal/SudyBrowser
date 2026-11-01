using LightBrowser.Core.Interfaces;
using LightBrowser.Core.Models;
using LightBrowser.Core.Services;

namespace LightBrowser.Tests;

public class RecentSitesTests
{
    private sealed class MemoryStore : IRecentSitesStore
    {
        public List<RecentSite> Saved { get; private set; } = new();

        public IReadOnlyList<RecentSite> Load() => Saved;

        public void Save(IReadOnlyList<RecentSite> sites) => Saved = sites.ToList();
    }

    [Fact]
    public void Record_KeepsLastThree_MostRecentFirst()
    {
        var sites = new RecentSites(new MemoryStore());

        foreach (var host in new[] { "a", "b", "c", "d" })
        {
            sites.Record($"https://{host}.com/page", host, string.Empty);
        }

        Assert.Equal(new[] { "https://d.com", "https://c.com", "https://b.com" }, sites.Items.Select(s => s.Url));
    }

    [Fact]
    public void Record_SameHost_MovesToFrontWithoutDuplicate_AndKeepsFavicon()
    {
        var sites = new RecentSites(new MemoryStore());
        sites.Record("https://a.com/x", "A", "https://a.com/f.ico");
        sites.Record("https://b.com/", "B", string.Empty);

        sites.Record("https://a.com/y", "A2", string.Empty);

        Assert.Equal(2, sites.Items.Count);
        Assert.Equal("https://a.com", sites.Items[0].Url);
        Assert.Equal("https://a.com/f.ico", sites.Items[0].FaviconUrl);
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("file:///C:/x.txt")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Record_IgnoresNonWebPages(string url)
    {
        var sites = new RecentSites(new MemoryStore());

        sites.Record(url, "t", string.Empty);

        Assert.Empty(sites.Items);
    }

    [Fact]
    public void Record_PersistsAndRaisesChanged()
    {
        var store = new MemoryStore();
        var sites = new RecentSites(store);
        var raised = 0;
        sites.Changed += (_, _) => raised++;

        sites.Record("https://a.com/", "A", string.Empty);
        sites.Record("https://a.com/", "A", string.Empty);

        Assert.Single(store.Saved);
        Assert.Equal(1, raised);
        Assert.Single(new RecentSites(store).Items);
    }
}
