using SudyBrowser.Core.Models;

namespace SudyBrowser.Core.Interfaces;

public interface IHistoryStore
{
    /// <summary>
    /// Adds or updates a page. <paramref name="countVisit"/> is false when only the title or favicon of an already
    /// counted visit changed. Implementations must not throw; failures are logged.
    /// </summary>
    Task RecordVisitAsync(
        string url,
        string title,
        bool countVisit,
        string faviconUrl = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="limit"/> candidates whose host, address or title contain
    /// <paramref name="normalizedQuery"/> (lower case, scheme and "www." already removed), best-known first.
    /// </summary>
    Task<IReadOnlyList<HistoryEntry>> SearchAsync(string normalizedQuery, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the most recently visited history entries up to <paramref name="limit"/>, newest first.
    /// </summary>
    Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HistoryEntry>>(Array.Empty<HistoryEntry>());

    /// <summary>
    /// Removes a specific URL from browsing history.
    /// </summary>
    Task DeleteAsync(string url, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Removes all history entries whose last visit is on or after <paramref name="cutoffUtc"/>.
    /// Pass <see cref="DateTime.MinValue"/> to clear all history.
    /// </summary>
    Task DeleteSinceAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
