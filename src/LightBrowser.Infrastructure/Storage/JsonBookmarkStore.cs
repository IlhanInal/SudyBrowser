using System.Text.Json;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.Infrastructure.Storage;

/// <summary>Stores bookmarks as a compact JSON file in the app data folder. Failures are logged, never thrown.</summary>
public sealed class JsonBookmarkStore : IBookmarkStore
{
    private readonly string _path;
    private readonly ILogger<JsonBookmarkStore> _logger;

    public JsonBookmarkStore(ILogger<JsonBookmarkStore> logger)
        : this(Path.Combine(AppDataPaths.Root, "bookmarks.json"), logger)
    {
    }

    public JsonBookmarkStore(string path, ILogger<JsonBookmarkStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public IReadOnlyList<BookmarkEntry> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<BookmarkEntry>();
            }

            var items = JsonSerializer.Deserialize<List<BookmarkEntry>>(File.ReadAllText(_path));
            return items?.Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Url)).ToList()
                ?? (IReadOnlyList<BookmarkEntry>)Array.Empty<BookmarkEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read bookmarks from {Path}", _path);
            return Array.Empty<BookmarkEntry>();
        }
    }

    public void Save(IReadOnlyList<BookmarkEntry> bookmarks)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(bookmarks));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save bookmarks to {Path}", _path);
        }
    }
}
