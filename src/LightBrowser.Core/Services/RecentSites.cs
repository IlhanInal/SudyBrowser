using LightBrowser.Core.Interfaces;
using LightBrowser.Core.Models;

namespace LightBrowser.Core.Services;

/// <summary>Keeps the last few distinct sites (by host) the user visited, most recent first.</summary>
public sealed class RecentSites
{
    public const int MaxCount = 3;

    private readonly IRecentSitesStore _store;
    private readonly List<RecentSite> _items;

    public RecentSites(IRecentSitesStore store)
    {
        _store = store;
        _items = store.Load().Take(MaxCount).ToList();
    }

    public IReadOnlyList<RecentSite> Items => _items;

    public event EventHandler? Changed;

    /// <summary>Records a visit. Only http(s) pages count; revisiting a host moves it to the front.</summary>
    public void Record(string url, string title, string faviconUrl)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var site = new RecentSite(
            uri.GetLeftPart(UriPartial.Authority),
            string.IsNullOrWhiteSpace(title) ? uri.Host : title,
            faviconUrl);

        var existing = _items.FindIndex(s => SameHost(s.Url, site.Url));
        if (existing == 0 && _items[0] == site)
        {
            return;
        }

        if (existing >= 0)
        {
            // Keep a previously known favicon if this sample does not have one yet.
            if (string.IsNullOrEmpty(site.FaviconUrl))
            {
                site = site with { FaviconUrl = _items[existing].FaviconUrl };
            }

            _items.RemoveAt(existing);
        }

        _items.Insert(0, site);
        if (_items.Count > MaxCount)
        {
            _items.RemoveRange(MaxCount, _items.Count - MaxCount);
        }

        _store.Save(_items);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameHost(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var ua)
        && Uri.TryCreate(b, UriKind.Absolute, out var ub)
        && string.Equals(ua.Host, ub.Host, StringComparison.OrdinalIgnoreCase);
}
