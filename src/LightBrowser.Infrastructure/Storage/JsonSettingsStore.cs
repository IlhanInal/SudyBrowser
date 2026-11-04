using System.Text.Json;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.Infrastructure.Storage;

/// <summary>Stores user configuration (theme, background, settings) in a small JSON file. Failures are logged, never thrown.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly ILogger<JsonSettingsStore> _logger;

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger)
        : this(Path.Combine(AppDataPaths.Root, "settings.json"), logger)
    {
    }

    public JsonSettingsStore(string path, ILogger<JsonSettingsStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UserSettings();
            }

            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path));
            return settings ?? new UserSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read user settings from {Path}", _path);
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save user settings to {Path}", _path);
        }
    }
}
