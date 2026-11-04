using SudyBrowser.Core.Models;
using SudyBrowser.Core.Policies;
using SudyBrowser.Core.Services;

namespace SudyBrowser.Tests;

public class SuggestionServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static HistoryEntry Entry(string url, string title, int visits = 1, int daysAgo = 0) =>
        new(url, title, visits, Now.AddDays(-daysAgo));

    private static (SuggestionService Service, FakeHistoryStore History, FakeSuggestionProvider Online) Create()
    {
        var history = new FakeHistoryStore();
        var online = new FakeSuggestionProvider();
        return (new SuggestionService(history, online, utcNow: () => Now), history, online);
    }

    [Fact]
    public async Task EmptyOrWhitespace_ReturnsNothingWithoutQueryingTheStore()
    {
        var (service, history, _) = Create();

        Assert.Empty(await service.GetLocalAsync("", CancellationToken.None));
        Assert.Empty(await service.GetLocalAsync("   ", CancellationToken.None));
        Assert.Equal(0, history.SearchCalls);
    }

    [Fact]
    public async Task SingleCharacter_FindsHostPrefixMatches()
    {
        var (service, history, _) = Create();
        history.Entries.Add(Entry("https://www.yandex.com/", "Yandex"));

        var result = await service.GetLocalAsync("y", CancellationToken.None);

        Assert.Contains(result, s => s.Url == "https://www.yandex.com/" && s.Source == SuggestionSource.History);
        Assert.Contains(result, s => s.Source == SuggestionSource.Site && s.Title == "YouTube");
    }

    [Fact]
    public async Task HostPrefix_RanksAboveTitleOnlyMatch()
    {
        var (service, history, _) = Create();
        history.Entries.Add(Entry("https://blog.example.org/post", "Why youtube is big", visits: 40));
        history.Entries.Add(Entry("https://youtube.com/feed", "Feed", visits: 1));

        var result = await service.GetLocalAsync("youtube", CancellationToken.None);

        // Front page first, then the deep link on the same host, then the title-only match.
        Assert.Equal(
            new[] { "https://www.youtube.com/", "https://youtube.com/feed", "https://blog.example.org/post" },
            result.Select(s => s.Url).ToArray());
    }

    [Fact]
    public async Task FrontPage_RanksAboveDeepLinkOnSameHost()
    {
        var (service, history, _) = Create();
        history.Entries.Add(Entry("https://www.youtube.com/watch?v=1", "A video", visits: 30));

        var result = await service.GetLocalAsync("yout", CancellationToken.None);

        Assert.Equal("https://www.youtube.com/", result[0].Url);
    }

    [Fact]
    public async Task SameAddress_AppearsOnce_HistoryWinsOverBuiltInSite()
    {
        var (service, history, _) = Create();
        history.Entries.Add(Entry("https://youtube.com", "My YouTube", visits: 5));

        var result = await service.GetLocalAsync("youtube", CancellationToken.None);

        Assert.Single(result, s => UrlText.DedupeKey(s.Url) == "youtube.com");
        Assert.Equal(SuggestionSource.History, result.Single(s => UrlText.DedupeKey(s.Url) == "youtube.com").Source);
    }

    [Fact]
    public async Task Results_AreCappedAtEight()
    {
        var (service, history, _) = Create();
        for (var i = 0; i < 30; i++)
        {
            history.Entries.Add(Entry($"https://site{i}.com/", $"Site {i}"));
        }

        var result = await service.GetLocalAsync("site", CancellationToken.None);

        Assert.Equal(SuggestionService.MaxSuggestions, result.Count);
    }

    [Fact]
    public async Task TypedScheme_AndWww_AreIgnoredWhenMatching()
    {
        var (service, history, _) = Create();
        history.Entries.Add(Entry("https://www.example.com/", "Example"));

        var result = await service.GetLocalAsync("https://www.exam", CancellationToken.None);

        Assert.Equal("https://www.example.com/", result[0].Url);
    }

    [Fact]
    public async Task NoMatches_ReturnsEmpty()
    {
        var (service, _, _) = Create();

        Assert.Empty(await service.GetLocalAsync("qqqqzzzz", CancellationToken.None));
    }

    [Fact]
    public async Task Online_WrapsQueriesAsSearchSuggestions()
    {
        var (service, _, online) = Create();
        online.Reply = new[] { "weather", "", "weather today" };

        var result = await service.GetOnlineAsync("wea", CancellationToken.None);

        Assert.Equal(new[] { "weather", "weather today" }, result.Select(s => s.Text));
        Assert.All(result, s => Assert.Equal(SuggestionSource.Search, s.Source));
    }

    [Fact]
    public void Merge_PutsLocalFirst_AndReservesRoomForOnline()
    {
        var local = Enumerable.Range(0, 8).Select(i => new Suggestion($"https://l{i}.com/", $"L{i}", $"https://l{i}.com/", SuggestionSource.History)).ToList();
        var online = Enumerable.Range(0, 8).Select(i => new Suggestion($"q{i}", $"q{i}", string.Empty, SuggestionSource.Search)).ToList();

        var merged = SuggestionService.Merge(local, online);

        Assert.Equal(SuggestionService.MaxSuggestions, merged.Count);
        Assert.Equal(5, merged.Count(s => s.Source == SuggestionSource.History));
        Assert.Equal(3, merged.Count(s => s.IsSearch));
        Assert.All(merged.Take(5), s => Assert.Equal(SuggestionSource.History, s.Source));
    }

    [Fact]
    public void Merge_WithFewLocalResults_LetsOnlineFillTheList()
    {
        var local = new[] { new Suggestion("https://a.com/", "A", "https://a.com/", SuggestionSource.History) };
        var online = Enumerable.Range(0, 10).Select(i => new Suggestion($"q{i}", $"q{i}", string.Empty, SuggestionSource.Search)).ToList();

        var merged = SuggestionService.Merge(local, online);

        Assert.Equal(SuggestionService.MaxSuggestions, merged.Count);
        Assert.Equal(7, merged.Count(s => s.IsSearch));
    }

    [Fact]
    public void Merge_WithoutOnline_ReturnsLocalUnchanged()
    {
        var local = new[] { new Suggestion("https://a.com/", "A", "https://a.com/", SuggestionSource.History) };

        Assert.Same(local, SuggestionService.Merge(local, Array.Empty<Suggestion>()));
    }
}

public class InlineCompletionTests
{
    private static Suggestion Site(string url) => new(url, "t", url, SuggestionSource.History);

    [Theory]
    [InlineData("y", "https://www.youtube.com/", "outube.com")]
    [InlineData("youtube.c", "https://www.youtube.com/", "om")]
    [InlineData("YOU", "https://www.youtube.com/", "tube.com")]
    [InlineData("www.you", "https://www.youtube.com/", "tube.com")]
    [InlineData("https://you", "https://www.youtube.com/", null)]
    [InlineData("https://www.you", "https://www.youtube.com/", "tube.com")]
    [InlineData("youtube.com", "https://www.youtube.com/", null)]
    [InlineData("youtube.com/w", "https://youtube.com/watch?v=1", "atch?v=1")]
    [InlineData("tube", "https://www.youtube.com/", null)]
    [InlineData("you tube", "https://www.youtube.com/", null)]
    [InlineData("", "https://www.youtube.com/", null)]
    public void GetTail_CompletesOnlyFromTheStartOfTheAddress(string typed, string url, string? expected) =>
        Assert.Equal(expected, InlineCompletion.GetTail(typed, Site(url)));

    [Fact]
    public void GetTail_NeverCompletesSearchSuggestionsOrMissingRows()
    {
        Assert.Null(InlineCompletion.GetTail("wea", new Suggestion("weather", "weather", string.Empty, SuggestionSource.Search)));
        Assert.Null(InlineCompletion.GetTail("wea", null));
    }
}

public class HistoryRecorderTests
{
    private static NavigationState Loaded(string url, string title) => new(url, title, false, false, false);

    [Fact]
    public void NewAddress_CountsAsVisit_ChangedTitleOnlyUpdates_RepeatIsIgnored()
    {
        var store = new FakeHistoryStore();
        var recorder = new HistoryRecorder(store);
        var tab = Guid.NewGuid();

        recorder.Record(tab, Loaded("https://a.com/x#frag", "A"));
        recorder.Record(tab, Loaded("https://a.com/x", "A"));
        recorder.Record(tab, Loaded("https://a.com/x", "A new title"));
        recorder.Record(tab, Loaded("https://b.com/", "B"));

        Assert.Equal(
            new[]
            {
                ("https://a.com/x", "A", true, ""),
                ("https://a.com/x", "A new title", false, ""),
                ("https://b.com/", "B", true, ""),
            },
            store.Recorded.ToArray());
    }

    [Fact]
    public void LoadingPagesAndNonWebAddresses_AreNotRecorded()
    {
        var store = new FakeHistoryStore();
        var recorder = new HistoryRecorder(store);
        var tab = Guid.NewGuid();

        recorder.Record(tab, new NavigationState("https://a.com/", "A", false, false, true));
        recorder.Record(tab, Loaded("about:blank", ""));
        recorder.Record(tab, Loaded("file:///c:/secret.txt", "x"));

        Assert.Empty(store.Recorded);
    }

    [Fact]
    public void Forget_AllowsTheSameAddressToCountAgain()
    {
        var store = new FakeHistoryStore();
        var recorder = new HistoryRecorder(store);
        var tab = Guid.NewGuid();

        recorder.Record(tab, Loaded("https://a.com/", "A"));
        recorder.Forget(tab);
        recorder.Record(tab, Loaded("https://a.com/", "A"));

        Assert.Equal(2, store.Recorded.Count(r => r.CountVisit));
    }
}
