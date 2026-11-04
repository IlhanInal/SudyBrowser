using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;

namespace SudyBrowser.Tests;

internal sealed class FakeHistoryStore : IHistoryStore
{
    public List<HistoryEntry> Entries { get; } = new();

    public List<(string Url, string Title, bool CountVisit, string FaviconUrl)> Recorded { get; } = new();

    public int SearchCalls { get; private set; }

    public Task RecordVisitAsync(
        string url,
        string title,
        bool countVisit,
        string faviconUrl = "",
        CancellationToken cancellationToken = default)
    {
        Recorded.Add((url, title, countVisit, faviconUrl));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoryEntry>> SearchAsync(string normalizedQuery, int limit, CancellationToken cancellationToken)
    {
        SearchCalls++;
        IReadOnlyList<HistoryEntry> result = Entries
            .Where(e => e.Url.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                        || e.Title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }
}

internal sealed class FakeSuggestionProvider : ISearchSuggestionProvider
{
    /// <summary>Per-query reply gate; when set, the call waits for it, so tests control reply order.</summary>
    public Dictionary<string, TaskCompletionSource<IReadOnlyList<string>>> Gates { get; } = new();

    public List<string> Queries { get; } = new();

    public IReadOnlyList<string> Reply { get; set; } = Array.Empty<string>();

    /// <summary>Simulates a service that cannot be cancelled, so only the caller's own checks can drop its late reply.</summary>
    public bool IgnoreCancellation { get; set; }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken)
    {
        lock (Queries)
        {
            Queries.Add(query);
        }

        if (Gates.TryGetValue(query, out var gate))
        {
            return IgnoreCancellation ? await gate.Task : await gate.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Reply;
    }
}
