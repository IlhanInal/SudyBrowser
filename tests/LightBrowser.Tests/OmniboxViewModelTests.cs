using SudyBrowser.App.Services;
using SudyBrowser.App.ViewModels;
using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;
using SudyBrowser.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace SudyBrowser.Tests;

public sealed class OmniboxViewModelTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    static OmniboxViewModelTests()
    {
        // SettingsViewModel builds pack:// URIs; touching this registers the scheme without starting a WPF Application.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
    }

    private readonly FakeHistoryStore _history = new();
    private readonly FakeSuggestionProvider _online = new();
    private readonly SettingsViewModel _settings = new(new ThemeService());
    private readonly OmniboxViewModel _omnibox;

    public OmniboxViewModelTests()
    {
        _history.Entries.Add(new HistoryEntry("https://www.youtube.com/watch?v=1", "A video", 3, DateTime.UtcNow));
        var service = new SuggestionService(_history, _online);
        _omnibox = new OmniboxViewModel(service, new AddressParser(), _settings, NullLogger<OmniboxViewModel>.Instance);
    }

    public void Dispose() => _omnibox.Dispose();

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Typing_ShowsLocalResultsWithoutWaitingForOnline()
    {
        _settings.OnlineSuggestionsEnabled = true;
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        _online.Gates["you"] = gate; // never released: online is "hanging"

        _omnibox.TextTyped("you", allowInline: false);
        await WaitUntil(() => _omnibox.IsOpen);

        Assert.Contains(_omnibox.Suggestions, s => s.Source == SuggestionSource.History);
        Assert.DoesNotContain(_omnibox.Suggestions, s => s.IsSearch);
    }

    [Fact]
    public async Task OnlineResults_AreMergedAfterwards_WhenEnabled()
    {
        _settings.OnlineSuggestionsEnabled = true;
        _online.Reply = new[] { "youtube music", "youtube tv" };

        _omnibox.TextTyped("you", allowInline: false);

        await WaitUntil(() => _omnibox.Suggestions.Any(s => s.IsSearch));
        Assert.True(_omnibox.Suggestions.First().Source != SuggestionSource.Search, "Local rows stay on top.");
    }

    [Fact]
    public async Task OnlineDisabled_NeverCallsTheProvider()
    {
        _omnibox.TextTyped("you", allowInline: false);
        await WaitUntil(() => _omnibox.IsOpen);
        await Task.Delay(400);

        Assert.Empty(_online.Queries);
    }

    [Theory]
    [InlineData("youtube.com")]
    [InlineData("https://example.org/page")]
    [InlineData("y")]
    public async Task AddressLikeOrTooShortText_IsNeverSentOnline(string text)
    {
        _settings.OnlineSuggestionsEnabled = true;

        _omnibox.TextTyped(text, allowInline: false);
        await Task.Delay(500);

        Assert.Empty(_online.Queries);
    }

    [Fact]
    public async Task LateOnlineReply_ForOlderText_NeverOverwritesNewerResults()
    {
        _settings.OnlineSuggestionsEnabled = true;
        _online.IgnoreCancellation = true;
        var oldGate = new TaskCompletionSource<IReadOnlyList<string>>();
        _online.Gates["yo"] = oldGate;

        _omnibox.TextTyped("yo", allowInline: false);
        await WaitUntil(() => _online.Queries.Contains("yo"));

        _online.Reply = new[] { "you tube new" };
        _omnibox.TextTyped("you", allowInline: false);
        await WaitUntil(() => _omnibox.Suggestions.Any(s => s.Text == "you tube new"));

        oldGate.SetResult(new[] { "STALE" });
        await Task.Delay(200);

        Assert.DoesNotContain(_omnibox.Suggestions, s => s.Text == "STALE");
        Assert.Contains(_omnibox.Suggestions, s => s.Text == "you tube new");
    }

    [Fact]
    public async Task RapidTyping_SendsOnlyTheFinalQueryOnline()
    {
        _settings.OnlineSuggestionsEnabled = true;

        foreach (var text in new[] { "we", "wea", "weat", "weath" })
        {
            _omnibox.TextTyped(text, allowInline: false);
        }

        await Task.Delay(600);

        Assert.Equal(new[] { "weath" }, _online.Queries.ToArray());
    }

    [Fact]
    public async Task EmptyText_ClosesAndClears()
    {
        _omnibox.TextTyped("you", allowInline: false);
        await WaitUntil(() => _omnibox.IsOpen);

        _omnibox.TextTyped("", allowInline: false);

        Assert.False(_omnibox.IsOpen);
        Assert.Empty(_omnibox.Suggestions);
        Assert.Equal(-1, _omnibox.SelectedIndex);
    }

    [Fact]
    public async Task Close_DropsResultsThatArriveAfterwards()
    {
        _settings.OnlineSuggestionsEnabled = true;
        _online.IgnoreCancellation = true;
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        _online.Gates["you"] = gate;

        _omnibox.TextTyped("you", allowInline: false);
        await WaitUntil(() => _online.Queries.Contains("you"));
        _omnibox.Close();
        gate.SetResult(new[] { "late" });
        await Task.Delay(200);

        Assert.False(_omnibox.IsOpen);
        Assert.Empty(_omnibox.Suggestions);
    }

    [Fact]
    public async Task InlineCompletion_IsRaisedForTheTopRow_OnlyWhenAllowed()
    {
        InlineCompletionEventArgs? raised = null;
        _omnibox.InlineCompletionReady += (_, e) => raised = e;

        _omnibox.TextTyped("you", allowInline: false);
        await WaitUntil(() => _omnibox.IsOpen);
        Assert.Null(raised);
        Assert.Equal(-1, _omnibox.SelectedIndex);

        _omnibox.TextTyped("you", allowInline: true);
        await WaitUntil(() => raised is not null);

        Assert.Equal("you", raised!.Typed);
        Assert.Equal("tube.com", raised.Tail);
        Assert.Equal(0, _omnibox.SelectedIndex);
    }

    [Fact]
    public async Task MoveSelection_WrapsThroughTypedText_AndReturnsTheTextToShow()
    {
        _omnibox.TextTyped("you", allowInline: false);
        await WaitUntil(() => _omnibox.Suggestions.Count >= 2);
        var count = _omnibox.Suggestions.Count;

        Assert.Equal(_omnibox.Suggestions[0].Text, _omnibox.MoveSelection(1));
        Assert.Equal(0, _omnibox.SelectedIndex);
        Assert.Equal("you", _omnibox.MoveSelection(-1));
        Assert.Equal(-1, _omnibox.SelectedIndex);
        Assert.Equal(_omnibox.Suggestions[count - 1].Text, _omnibox.MoveSelection(-1));
        Assert.Equal("you", _omnibox.MoveSelection(1));
    }

    [Fact]
    public void MoveSelection_WithoutResults_KeepsTypedText()
    {
        Assert.Equal(string.Empty, _omnibox.MoveSelection(1));
        Assert.Equal(-1, _omnibox.SelectedIndex);
    }
}
