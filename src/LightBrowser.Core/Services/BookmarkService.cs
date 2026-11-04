using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;

namespace SudyBrowser.Core.Services;

/// <summary>
/// Manages user bookmarks in memory and persists changes through <see cref="IBookmarkStore"/>.
/// Capped to prevent unbounded memory growth.
/// </summary>
public sealed class BookmarkService
{
    public const int MaxCount = 200;

    private readonly IBookmarkStore _store;
    private readonly List<BookmarkEntry> _items;

    public BookmarkService(IBookmarkStore store)
    {
        _store = store;
        _items = store.Load().Take(MaxCount).ToList();
    }

    public IReadOnlyList<BookmarkEntry> Items => _items;

    public event EventHandler? Changed;

    public bool IsBookmarked(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var key = UrlText.DedupeKey(url);
        return key.Length > 0 && _items.Any(b => string.Equals(UrlText.DedupeKey(b.Url), key, StringComparison.Ordinal));
    }

    /// <summary>Adds the page if not bookmarked, or removes it if already bookmarked. Returns true if now bookmarked.</summary>
    public bool Toggle(string url, string title, string faviconUrl = "")
    {
        if (IsBookmarked(url))
        {
            Remove(url);
            return false;
        }

        AddOrUpdate(url, title, faviconUrl);
        return true;
    }

    public void AddOrUpdate(string url, string title, string faviconUrl = "")
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var cleanTitle = string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim();
        var key = UrlText.DedupeKey(uri.AbsoluteUri);
        var existingIndex = _items.FindIndex(b => string.Equals(UrlText.DedupeKey(b.Url), key, StringComparison.Ordinal));

        if (existingIndex >= 0)
        {
            var existing = _items[existingIndex];
            var resolvedFavicon = !string.IsNullOrWhiteSpace(faviconUrl) ? faviconUrl : existing.FaviconUrl;
            _items.RemoveAt(existingIndex);
            _items.Insert(0, new BookmarkEntry(uri.AbsoluteUri, cleanTitle, resolvedFavicon, DateTime.UtcNow));
        }
        else
        {
            _items.Insert(0, new BookmarkEntry(uri.AbsoluteUri, cleanTitle, faviconUrl ?? string.Empty, DateTime.UtcNow));
            if (_items.Count > MaxCount)
            {
                _items.RemoveRange(MaxCount, _items.Count - MaxCount);
            }
        }

        _store.Save(_items);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var key = UrlText.DedupeKey(url);
        var removed = _items.RemoveAll(b =>
            string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase)
            || (key.Length > 0 && string.Equals(UrlText.DedupeKey(b.Url), key, StringComparison.Ordinal)));

        if (removed > 0)
        {
            _store.Save(_items);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }
}
