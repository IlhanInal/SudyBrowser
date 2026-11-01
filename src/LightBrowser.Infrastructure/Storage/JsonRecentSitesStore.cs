using System.Text.Json;
using LightBrowser.Core.Interfaces;
using LightBrowser.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightBrowser.Infrastructure.Storage;

/// <summary>Stores recent sites as a small JSON file in the app data folder. Failures are logged, never thrown.</summary>
public sealed class JsonRecentSitesStore : IRecentSitesStore
{
    private readonly string _path;
    private readonly ILogger<JsonRecentSitesStore> _logger;

    public JsonRecentSitesStore(ILogger<JsonRecentSitesStore> logger)
        : this(Path.Combine(AppDataPaths.Root, "recent-sites.json"), logger)
    {
    }

    public JsonRecentSitesStore(string path, ILogger<JsonRecentSitesStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public IReadOnlyList<RecentSite> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<RecentSite>();
            }

            var sites = JsonSerializer.Deserialize<List<RecentSite>>(File.ReadAllText(_path));
            return sites?.Where(s => s is not null && !string.IsNullOrEmpty(s.Url)).ToList()
                ?? (IReadOnlyList<RecentSite>)Array.Empty<RecentSite>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read recent sites from {Path}", _path);
            return Array.Empty<RecentSite>();
        }
    }

    public void Save(IReadOnlyList<RecentSite> sites)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(sites));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save recent sites to {Path}", _path);
        }
    }
}
