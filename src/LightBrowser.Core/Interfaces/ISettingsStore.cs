using SudyBrowser.Core.Models;

namespace SudyBrowser.Core.Interfaces;

public interface ISettingsStore
{
    UserSettings Load();

    void Save(UserSettings settings);
}
