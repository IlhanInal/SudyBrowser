using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;

namespace SudyBrowser.Core.Services;

/// <summary>
/// Turns tab navigation state into history writes. A tab reports state many times per page, so this
/// remembers the last recorded page per tab: a new address counts as a visit, a changed title only updates the row.
/// </summary>
public sealed class HistoryRecorder
{
    private const int MaxUrlLength = 2048;

    private readonly IHistoryStore _store;
    private readonly Dictionary<Guid, (string Url, string Title, string FaviconUrl)> _lastByTab = new();

    public HistoryRecorder(IHistoryStore store) => _store = store;

    public void Record(Guid tabId, NavigationState state)
    {
        if (state.IsLoading
            || state.Url.Length > MaxUrlLength
            || !Uri.TryCreate(state.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var url = uri.GetLeftPart(UriPartial.Query);
        var title = string.IsNullOrWhiteSpace(state.Title) ? uri.Host : state.Title.Trim();
        var faviconUrl = FaviconUrlPolicy.IsSafeHttpUrl(state.FaviconUrl, out var faviconUri) && faviconUri is not null
            ? faviconUri.AbsoluteUri
            : string.Empty;

        bool countVisit;
        if (_lastByTab.TryGetValue(tabId, out var last) && last.Url == url)
        {
            if (string.IsNullOrEmpty(faviconUrl))
            {
                faviconUrl = last.FaviconUrl;
            }

            if (last.Title == title && last.FaviconUrl == faviconUrl)
            {
                return;
            }

            countVisit = false;
        }
        else
        {
            countVisit = true;
        }

        _lastByTab[tabId] = (url, title, faviconUrl);
        _ = _store.RecordVisitAsync(url, title, countVisit, faviconUrl);
    }

    /// <summary>Drops per-tab bookkeeping when a tab is closed.</summary>
    public void Forget(Guid tabId) => _lastByTab.Remove(tabId);
}
