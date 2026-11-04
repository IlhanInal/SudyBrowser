using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;

namespace SudyBrowser.Core.Services;

/// <summary>Produces address bar suggestions: local ones first (history, known sites), online ones separately.</summary>
public sealed class SuggestionService
{
    public const int MaxSuggestions = 8;

    /// <summary>Online results are guaranteed this many slots when the local list would crowd them out.</summary>
    private const int ReservedOnlineSlots = 3;

    private const int CandidateLimit = 40;

    private readonly IHistoryStore _history;
    private readonly ISearchSuggestionProvider _online;
    private readonly BookmarkService? _bookmarks;
    private readonly IReadOnlyList<KnownSite> _knownSites;
    private readonly Func<DateTime> _utcNow;

    public SuggestionService(
        IHistoryStore history,
        ISearchSuggestionProvider online,
        IReadOnlyList<KnownSite>? knownSites = null,
        Func<DateTime>? utcNow = null,
        BookmarkService? bookmarks = null)
    {
        _history = history;
        _online = online;
        _bookmarks = bookmarks;
        _knownSites = knownSites ?? KnownSites.Default;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Ranked, de-duplicated local matches for the typed text (at most <see cref="MaxSuggestions"/>).</summary>
    public async Task<IReadOnlyList<Suggestion>> GetLocalAsync(string typed, CancellationToken cancellationToken)
    {
        var query = UrlText.NormalizeQuery(typed);
        if (query.Length == 0)
        {
            return Array.Empty<Suggestion>();
        }

        var now = _utcNow();
        var scored = new List<(int Score, Suggestion Suggestion)>();

        if (_bookmarks is not null)
        {
            foreach (var bookmark in _bookmarks.Items)
            {
                var score = SuggestionRanker.Score(query, bookmark.Url, bookmark.Title, 5, bookmark.CreatedAtUtc, now);
                if (score != SuggestionRanker.NoMatch)
                {
                    scored.Add((score + 50, new Suggestion(bookmark.Url, bookmark.Title, bookmark.Url, SuggestionSource.Bookmark, bookmark.FaviconUrl)));
                }
            }
        }

        foreach (var entry in await _history.SearchAsync(query, CandidateLimit, cancellationToken).ConfigureAwait(true))
        {
            var score = SuggestionRanker.Score(query, entry.Url, entry.Title, entry.VisitCount, entry.LastVisitUtc, now);
            if (score != SuggestionRanker.NoMatch)
            {
                scored.Add((score, new Suggestion(entry.Url, entry.Title, entry.Url, SuggestionSource.History, entry.FaviconUrl)));
            }
        }

        foreach (var site in _knownSites)
        {
            var score = SuggestionRanker.Score(query, site.Url, site.Title, 0, DateTime.MinValue, now);
            if (score != SuggestionRanker.NoMatch)
            {
                scored.Add((score, new Suggestion(site.Url, site.Title, site.Url, SuggestionSource.Site)));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return scored
            .OrderByDescending(s => s.Score)
            .Where(s => seen.Add(UrlText.DedupeKey(s.Suggestion.Url)))
            .Take(MaxSuggestions)
            .Select(s => s.Suggestion)
            .ToList();
    }

    /// <summary>Online search suggestions. Only call this when the user has opted in.</summary>
    public async Task<IReadOnlyList<Suggestion>> GetOnlineAsync(string typed, CancellationToken cancellationToken)
    {
        var queries = await _online.GetSuggestionsAsync(typed.Trim(), cancellationToken).ConfigureAwait(true);
        return queries
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => new Suggestion(q, q, string.Empty, SuggestionSource.Search))
            .ToList();
    }

    /// <summary>Local rows first, then online rows, never more than <see cref="MaxSuggestions"/> in total.</summary>
    public static IReadOnlyList<Suggestion> Merge(IReadOnlyList<Suggestion> local, IReadOnlyList<Suggestion> online)
    {
        if (online.Count == 0)
        {
            return local;
        }

        var localCount = Math.Min(local.Count, MaxSuggestions - Math.Min(online.Count, ReservedOnlineSlots));
        var result = new List<Suggestion>(MaxSuggestions);
        result.AddRange(local.Take(localCount));

        var seen = new HashSet<string>(result.Select(s => s.Text), StringComparer.OrdinalIgnoreCase);
        foreach (var suggestion in online)
        {
            if (result.Count == MaxSuggestions)
            {
                break;
            }

            if (seen.Add(suggestion.Text))
            {
                result.Add(suggestion);
            }
        }

        return result;
    }
}
