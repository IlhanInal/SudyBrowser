namespace SudyBrowser.Core.Interfaces;

/// <summary>Online search query suggestions. The query leaves the machine, so callers must opt in.</summary>
public interface ISearchSuggestionProvider
{
    /// <summary>Returns suggested queries. Returns an empty list on network failure; throws only on cancellation.</summary>
    Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken);
}
