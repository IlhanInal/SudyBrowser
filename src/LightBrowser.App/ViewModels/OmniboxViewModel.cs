using SudyBrowser.App.Services;
using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;
using SudyBrowser.Core.Services;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.App.ViewModels;

public sealed class InlineCompletionEventArgs : EventArgs
{
    public InlineCompletionEventArgs(string typed, string tail)
    {
        Typed = typed;
        Tail = tail;
    }

    /// <summary>The exact text the completion was computed for; the view applies it only if the box still holds this.</summary>
    public string Typed { get; }

    /// <summary>The part to append after <see cref="Typed"/>, shown selected.</summary>
    public string Tail { get; }
}

/// <summary>
/// State of the address bar suggestion list. Local results are published as soon as the database answers
/// (no debounce); online results follow after a short debounce and are dropped if the user kept typing.
/// Website rows load their favicons asynchronously without delaying the list or keyboard navigation.
/// </summary>
public sealed class OmniboxViewModel : ObservableObject, IDisposable
{
    private const int OnlineDebounceMilliseconds = 150;
    private const int MinOnlineQueryLength = 2;

    private readonly SuggestionService _service;
    private readonly IAddressParser _addressParser;
    private readonly SettingsViewModel _settings;
    private readonly FaviconCacheService? _favicons;
    private readonly ILogger<OmniboxViewModel> _logger;
    private IReadOnlyList<SuggestionItemViewModel> _suggestions = Array.Empty<SuggestionItemViewModel>();
    private CancellationTokenSource? _cts;
    private string _typedText = string.Empty;
    private int _selectedIndex = -1;
    private bool _isOpen;
    private bool _userNavigated;
    private int _version;

    public OmniboxViewModel(
        SuggestionService service,
        IAddressParser addressParser,
        SettingsViewModel settings,
        ILogger<OmniboxViewModel> logger,
        FaviconCacheService? favicons = null)
    {
        _service = service;
        _addressParser = addressParser;
        _settings = settings;
        _logger = logger;
        _favicons = favicons;
    }

    /// <summary>Raised after local results arrive when the top row can be completed inline.</summary>
    public event EventHandler<InlineCompletionEventArgs>? InlineCompletionReady;

    public IReadOnlyList<SuggestionItemViewModel> Suggestions
    {
        get => _suggestions;
        private set => SetProperty(ref _suggestions, value);
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>Highlighted row, or -1 for "what the user typed". Always re-notified so the list re-syncs after a rebuild.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        private set
        {
            _selectedIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>What the user actually typed, without any inline completion or arrow-key preview.</summary>
    public string TypedText => _typedText;

    /// <summary>Starts a new query for text the user just typed. Cancels whatever was running before.</summary>
    public void TextTyped(string text, bool allowInline)
    {
        CancelPending();
        _typedText = text;
        _userNavigated = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            Reset();
            return;
        }

        var version = ++_version;
        _cts = new CancellationTokenSource();
        _ = UpdateAsync(text, allowInline, version, _cts.Token);
    }

    /// <summary>
    /// Moves the highlight (wrapping through "typed text") and returns the text the address bar should show.
    /// </summary>
    public string MoveSelection(int delta)
    {
        if (_suggestions.Count == 0)
        {
            return _typedText;
        }

        _userNavigated = true;
        var next = _selectedIndex + delta;
        if (next < -1)
        {
            next = _suggestions.Count - 1;
        }
        else if (next >= _suggestions.Count)
        {
            next = -1;
        }

        SelectedIndex = next;
        return next < 0 ? _typedText : _suggestions[next].Text;
    }

    /// <summary>Hides the list and stops any pending query.</summary>
    public void Close()
    {
        CancelPending();
        Reset();
    }

    public void Dispose() => CancelPending();

    private async Task UpdateAsync(string text, bool allowInline, int version, CancellationToken cancellationToken)
    {
        try
        {
            var local = await _service.GetLocalAsync(text, cancellationToken);
            if (version != _version)
            {
                return;
            }

            var localRows = BuildRowViewModels(local, existingRows: null, version, cancellationToken);
            PublishLocal(text, local, localRows, allowInline);

            if (!ShouldAskOnline(text))
            {
                return;
            }

            await Task.Delay(OnlineDebounceMilliseconds, cancellationToken);
            var online = await _service.GetOnlineAsync(text, cancellationToken);

            // A late reply must never replace results for newer text, or reshuffle rows under the user's arrow keys.
            if (version != _version || _userNavigated || online.Count == 0)
            {
                return;
            }

            var merged = SuggestionService.Merge(local, online);
            Suggestions = BuildRowViewModels(merged, existingRows: localRows, version, cancellationToken);
            IsOpen = Suggestions.Count > 0;
            SelectedIndex = _selectedIndex;
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer input or closed; nothing to report.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Updating address bar suggestions failed");
        }
    }

    private void PublishLocal(
        string text,
        IReadOnlyList<Suggestion> local,
        IReadOnlyList<SuggestionItemViewModel> localRows,
        bool allowInline)
    {
        Suggestions = localRows;
        IsOpen = localRows.Count > 0;

        var tail = allowInline ? InlineCompletion.GetTail(text, local.FirstOrDefault()) : null;
        SelectedIndex = tail is null ? -1 : 0;
        if (tail is not null)
        {
            InlineCompletionReady?.Invoke(this, new InlineCompletionEventArgs(text, tail));
        }
    }

    private IReadOnlyList<SuggestionItemViewModel> BuildRowViewModels(
        IReadOnlyList<Suggestion> suggestions,
        IReadOnlyList<SuggestionItemViewModel>? existingRows,
        int version,
        CancellationToken cancellationToken)
    {
        if (suggestions.Count == 0)
        {
            return Array.Empty<SuggestionItemViewModel>();
        }

        var rows = new List<SuggestionItemViewModel>(suggestions.Count);
        foreach (var suggestion in suggestions)
        {
            if (existingRows is not null)
            {
                var reused = existingRows.FirstOrDefault(r => r.Suggestion == suggestion);
                if (reused is not null)
                {
                    rows.Add(reused);
                    continue;
                }
            }

            if (suggestion.IsSearch || _favicons is null)
            {
                rows.Add(new SuggestionItemViewModel(suggestion));
                continue;
            }

            if (_favicons.TryGetCached(suggestion.Url, suggestion.FaviconUrl, out var cachedIcon))
            {
                rows.Add(new SuggestionItemViewModel(suggestion, cachedIcon));
            }
            else
            {
                var row = new SuggestionItemViewModel(suggestion);
                rows.Add(row);
                _ = LoadFaviconForRowAsync(row, version, cancellationToken);
            }
        }

        return rows;
    }

    private async Task LoadFaviconForRowAsync(
        SuggestionItemViewModel row,
        int version,
        CancellationToken cancellationToken)
    {
        if (_favicons is null)
        {
            return;
        }

        try
        {
            var icon = await _favicons.GetOrFetchAsync(row.Url, row.Suggestion.FaviconUrl, cancellationToken);
            if (icon is not null && version == _version && _isOpen)
            {
                row.Favicon = icon;
            }
        }
        catch (OperationCanceledException)
        {
            // Query superseded or popup closed.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not assign favicon for {Url}", row.Url);
        }
    }

    private bool ShouldAskOnline(string text)
    {
        // Text that is already an address is never sent to a third party.
        var trimmed = text.Trim();
        return _settings.OnlineSuggestionsEnabled
               && trimmed.Length >= MinOnlineQueryLength
               && _addressParser.Parse(trimmed)?.Kind == AddressInputKind.Search;
    }

    private void CancelPending()
    {
        _version++;
        var cts = _cts;
        _cts = null;
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void Reset()
    {
        _typedText = string.Empty;
        _userNavigated = false;
        IsOpen = false;
        SelectedIndex = -1;
        if (_suggestions.Count > 0)
        {
            Suggestions = Array.Empty<SuggestionItemViewModel>();
        }
    }
}
