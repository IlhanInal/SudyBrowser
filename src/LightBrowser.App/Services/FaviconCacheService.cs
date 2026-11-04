using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Policies;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.App.Services;

/// <summary>
/// Resolves, decodes (at a small thumbnail size), freezes, and caches site favicons in a bounded LRU cache.
/// - Deduplicates concurrent in-flight requests per host.
/// - Remembers failed hosts for a cooldown window so typing does not repeatedly hit unreachable URLs.
/// - Performs all network and image decoding off the UI thread.
/// </summary>
public sealed class FaviconCacheService
{
    public const int DefaultMaxEntries = 128;
    public const int DecodePixelSize = 20;
    public static readonly TimeSpan DefaultFailureCooldown = TimeSpan.FromMinutes(10);

    private readonly IFaviconFetcher _fetcher;
    private readonly ILogger<FaviconCacheService> _logger;
    private readonly int _maxEntries;
    private readonly TimeSpan _failureCooldown;
    private readonly Func<DateTime> _utcNow;

    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _lruMap = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lruList = new();
    private readonly Dictionary<string, DateTime> _failedUntilUtc = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<ImageSource?>> _inFlight = new(StringComparer.Ordinal);

    private sealed record CacheEntry(string Key, ImageSource Icon);

    public FaviconCacheService(
        IFaviconFetcher fetcher,
        ILogger<FaviconCacheService> logger,
        int maxEntries = DefaultMaxEntries,
        TimeSpan? failureCooldown = null,
        Func<DateTime>? utcNow = null)
    {
        _fetcher = fetcher;
        _logger = logger;
        _maxEntries = Math.Max(8, maxEntries);
        _failureCooldown = failureCooldown ?? DefaultFailureCooldown;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Current number of decoded icons held in the LRU cache.</summary>
    public int CachedCount
    {
        get
        {
            lock (_sync)
            {
                return _lruMap.Count;
            }
        }
    }

    /// <summary>
    /// Synchronously checks the in-memory LRU cache without allocating tasks or hitting the network.
    /// Returns true when the site has already been resolved (either to an icon or to a recent failure).
    /// </summary>
    public bool TryGetCached(string? pageUrl, string? knownFaviconUrl, out ImageSource? icon)
    {
        icon = null;
        var key = ResolveKey(pageUrl, knownFaviconUrl);
        if (key.Length == 0)
        {
            return true;
        }

        lock (_sync)
        {
            if (_lruMap.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                icon = node.Value.Icon;
                return true;
            }

            if (_failedUntilUtc.TryGetValue(key, out var untilUtc))
            {
                if (_utcNow() < untilUtc)
                {
                    return true;
                }

                _failedUntilUtc.Remove(key);
            }
        }

        return false;
    }

    /// <summary>
    /// Asynchronously fetches, decodes, and caches a site's favicon off the UI thread.
    /// Concurrent requests for the same host share a single network operation.
    /// </summary>
    public async Task<ImageSource?> GetOrFetchAsync(
        string? pageUrl,
        string? knownFaviconUrl,
        CancellationToken cancellationToken)
    {
        if (TryGetCached(pageUrl, knownFaviconUrl, out var cached))
        {
            return cached;
        }

        var key = ResolveKey(pageUrl, knownFaviconUrl);
        var candidates = FaviconUrlPolicy.GetCandidateUrls(pageUrl, knownFaviconUrl);
        if (key.Length == 0 || candidates.Count == 0)
        {
            return null;
        }

        Task<ImageSource?> sharedTask;
        lock (_sync)
        {
            if (_lruMap.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return node.Value.Icon;
            }

            if (!_inFlight.TryGetValue(key, out sharedTask!))
            {
                sharedTask = FetchAndCacheCoreAsync(key, candidates);
                _inFlight[key] = sharedTask;
            }
        }

        return await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImageSource?> FetchAndCacheCoreAsync(string key, IReadOnlyList<string> candidates)
    {
        try
        {
            var icon = await Task.Run(async () =>
            {
                foreach (var url in candidates)
                {
                    var bytes = await _fetcher.FetchBytesAsync(url, CancellationToken.None).ConfigureAwait(false);
                    if (bytes is { Length: > 0 } && TryDecodeFrozenBitmap(bytes, out var decoded) && decoded is not null)
                    {
                        return decoded;
                    }
                }

                return null;
            }).ConfigureAwait(false);

            lock (_sync)
            {
                _inFlight.Remove(key);
                if (icon is not null)
                {
                    _failedUntilUtc.Remove(key);
                    StoreInLruLocked(key, icon);
                }
                else
                {
                    PruneFailedMapLocked();
                    _failedUntilUtc[key] = _utcNow() + _failureCooldown;
                }
            }

            return icon;
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _inFlight.Remove(key);
                _failedUntilUtc[key] = _utcNow() + _failureCooldown;
            }

            _logger.LogDebug(ex, "Favicon resolution failed for {Key}", key);
            return null;
        }
    }

    private void StoreInLruLocked(string key, ImageSource icon)
    {
        if (_lruMap.TryGetValue(key, out var existing))
        {
            _lruList.Remove(existing);
            existing.Value = new CacheEntry(key, icon);
            _lruList.AddFirst(existing);
            return;
        }

        while (_lruMap.Count >= _maxEntries && _lruList.Last is { } oldest)
        {
            _lruList.RemoveLast();
            _lruMap.Remove(oldest.Value.Key);
        }

        var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, icon));
        _lruList.AddFirst(node);
        _lruMap[key] = node;
    }

    private void PruneFailedMapLocked()
    {
        if (_failedUntilUtc.Count < _maxEntries * 2)
        {
            return;
        }

        var now = _utcNow();
        var expired = _failedUntilUtc.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
        foreach (var k in expired)
        {
            _failedUntilUtc.Remove(k);
        }

        if (_failedUntilUtc.Count >= _maxEntries * 2)
        {
            _failedUntilUtc.Clear();
        }
    }

    private static string ResolveKey(string? pageUrl, string? knownFaviconUrl)
    {
        var key = FaviconUrlPolicy.GetSiteCacheKey(pageUrl);
        return key.Length > 0 ? key : FaviconUrlPolicy.GetSiteCacheKey(knownFaviconUrl);
    }

    /// <summary>
    /// Decodes raw image bytes into a small, frozen <see cref="BitmapSource"/> on a background thread.
    /// Because it is frozen and backed by <see cref="BitmapCacheOption.OnLoad"/>, the stream can be disposed
    /// immediately and the bitmap can be bound from the UI thread with minimal memory footprint.
    /// </summary>
    private static bool TryDecodeFrozenBitmap(byte[] bytes, out ImageSource? image)
    {
        image = null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = DecodePixelSize;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                return false;
            }

            image = bitmap;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
