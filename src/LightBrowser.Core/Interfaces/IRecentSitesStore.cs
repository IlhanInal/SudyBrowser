using SudyBrowser.Core.Models;

namespace SudyBrowser.Core.Interfaces;

public interface IRecentSitesStore
{
    /// <summary>Loads saved sites, most recent first. Returns an empty list if nothing valid is stored.</summary>
    IReadOnlyList<RecentSite> Load();

    void Save(IReadOnlyList<RecentSite> sites);
}
