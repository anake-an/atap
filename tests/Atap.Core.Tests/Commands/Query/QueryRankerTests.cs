using Atap.Core.Commands.Query;

namespace Atap.Core.Tests.Commands.Query;

public class QueryRankerTests
{
    // Start menu entries on a typical Windows 11 PC in Malaysia.
    private const string StartMenuApps = """
        Calculator
        Camera
        Clock
        Command Prompt
        Control Panel
        Discord
        Excel
        File Explorer
        Google Chrome
        Microsoft Edge
        Microsoft Store
        Microsoft Teams
        Notepad
        OneDrive
        Outlook
        Paint
        Photos
        PowerPoint
        Settings
        Snipping Tool
        Spotify
        Steam
        Sticky Notes
        Task Manager
        Telegram
        Terminal
        Visual Studio Code
        WhatsApp
        Windows PowerShell
        Word
        Zoom Workplace
        """;

    private static readonly QueryCandidate[] Apps =
        [.. StartMenuApps.Split('\n').Select(t => t.Trim()).Select(t => new QueryCandidate($"app:{t}", QueryResultKind.App, t))];

    private static readonly QueryCandidate[] Commands =
    [
        new("settings.open", QueryResultKind.Command, "Open settings"),
        new("layout.edit", QueryResultKind.Command, "Edit layout"),
        new("notch.hide", QueryResultKind.Command, "Hide notch for 1 hour") { Keywords = ["sembunyi"] },
        new("notch.panel.toggle", QueryResultKind.Command, "Show or hide the panel"),
        new("media.playPause", QueryResultKind.Command, "Play or pause") { Keywords = ["music", "muzik"] },
        new("audio.volume.up", QueryResultKind.Command, "Volume up"),
    ];

    private static readonly QueryCandidate WebSearch = new("web", QueryResultKind.WebSearch, "Search the web");

    private static QueryCandidate[] Everything => [.. Apps, .. Commands, WebSearch];

    [Theory]
    [InlineData("spot", "app:Spotify")]
    [InlineData("vsc", "app:Visual Studio Code")]
    [InlineData("code", "app:Visual Studio Code")]
    [InlineData("vis code", "app:Visual Studio Code")]
    [InlineData("calc", "app:Calculator")]
    [InlineData("set", "app:Settings")]
    [InlineData("teams", "app:Microsoft Teams")]
    [InlineData("ps", "app:Windows PowerShell")]
    [InlineData("shell", "app:Windows PowerShell")]
    [InlineData("whats", "app:WhatsApp")]
    [InlineData("muzik", "media.playPause")]
    [InlineData("semb", "notch.hide")]
    [InlineData("edit lay", "layout.edit")]
    public void Rank_PutsTheExpectedRowFirst(string query, string expectedId)
    {
        var ranked = QueryRanker.Rank(query, Everything);

        Assert.Equal(expectedId, ranked[0].Candidate.Id);
    }

    [Fact]
    public void Rank_BlankQueryGivesNothing()
    {
        Assert.Empty(QueryRanker.Rank("", Everything));
        Assert.Empty(QueryRanker.Rank("   ", Everything));
        Assert.Empty(QueryRanker.Rank(null, Everything));
    }

    [Fact]
    public void Rank_LeavesOutRowsThatDoNotMatch()
    {
        var ranked = QueryRanker.Rank("zzqx", Everything);

        Assert.Equal(WebSearch, Assert.Single(ranked).Candidate);
    }

    [Fact]
    public void Rank_KeepsWebSearchLastEvenWhenFull()
    {
        var ranked = QueryRanker.Rank("e", Everything, maxResults: 5);

        Assert.Equal(5, ranked.Count);
        Assert.Equal(WebSearch, ranked[^1].Candidate);
        Assert.DoesNotContain(ranked.SkipLast(1), r => r.Candidate.Kind == QueryResultKind.WebSearch);
    }

    [Fact]
    public void Rank_KeepsAskAiRightAboveWebSearch_EvenWhenFull()
    {
        var askAi = new QueryCandidate("ai", QueryResultKind.AskAi, "Ask AI: \"e\"");

        var ranked = QueryRanker.Rank("e", [askAi, .. Everything], maxResults: 5);

        Assert.Equal(5, ranked.Count);
        Assert.Equal(WebSearch, ranked[^1].Candidate);
        Assert.Equal(askAi, ranked[^2].Candidate);
        Assert.Single(ranked, r => r.Candidate.Kind == QueryResultKind.AskAi);
    }

    [Fact]
    public void Rank_PinsCalculationsAndConversionsToTheTop()
    {
        var conversion = new QueryCandidate("conversion", QueryResultKind.Conversion, "5 km = 3.10685596118667 mi");
        var calculation = new QueryCandidate("calculation", QueryResultKind.Calculation, "= 96");

        var ranked = QueryRanker.Rank("calc", [.. Everything, conversion, calculation]);

        Assert.Equal(calculation, ranked[0].Candidate);
        Assert.Equal(conversion, ranked[1].Candidate);
        Assert.Equal(QueryRanker.PinnedScore, ranked[0].Score);
        Assert.Equal("app:Calculator", ranked[2].Candidate.Id);
        Assert.Equal(WebSearch, ranked[^1].Candidate);
    }

    [Fact]
    public void Rank_IsTheSameForAnyInputOrder()
    {
        var expected = Ids(QueryRanker.Rank("o", Everything, maxResults: 30));
        var random = new Random(20261025);

        for (var n = 0; n < 25; n++)
        {
            var shuffled = Everything;
            random.Shuffle(shuffled);

            Assert.Equal(expected, Ids(QueryRanker.Rank("o", shuffled, maxResults: 30)));
        }
    }

    [Fact]
    public void Rank_BreaksTiesByKindThenTitleThenId()
    {
        QueryCandidate[] candidates =
        [
            new("b", QueryResultKind.App, "Clock"),
            new("a", QueryResultKind.App, "Clock"),
            new("clock.open", QueryResultKind.Command, "Clock"),
        ];

        var ranked = QueryRanker.Rank("clock", candidates);

        Assert.Equal(["clock.open", "a", "b"], Ids(ranked));
    }

    [Fact]
    public void Rank_ListsADuplicateIdOnce()
    {
        QueryCandidate[] candidates =
        [
            new("app:Spotify", QueryResultKind.App, "Spotify"),
            new("app:Spotify", QueryResultKind.App, "Spotify"),
        ];

        Assert.Single(QueryRanker.Rank("spot", candidates));
    }

    [Fact]
    public void Rank_SkipsNullCandidates()
    {
        QueryCandidate?[] candidates = [null, new("app:Steam", QueryResultKind.App, "Steam")];

        Assert.Single(QueryRanker.Rank("steam", candidates));
    }

    [Fact]
    public void Rank_KeywordRanksBetweenTitleTiers()
    {
        var music = new QueryCandidate("app:Music", QueryResultKind.App, "Music");
        var musicPlayer = new QueryCandidate("app:Music Player", QueryResultKind.App, "Music Player");
        var playPause = Commands.Single(c => c.Id == "media.playPause");

        var ranked = QueryRanker.Rank("music", [musicPlayer, playPause, music]);

        // A whole-title match beats a whole-keyword match, which beats the start of a title.
        Assert.Equal(["app:Music", "media.playPause", "app:Music Player"], Ids(ranked));
    }

    [Fact]
    public void Score_FollowsTheTierOrder()
    {
        int[] scores =
        [
            QueryMatcher.Score("notepad", "Notepad"),           // whole title
            QueryMatcher.Score("note", "Notepad"),              // start of the title
            QueryMatcher.Score("note", "Sticky Notes"),         // start of a word
            QueryMatcher.Score("sti no", "Sticky Notes"),       // every word, in order
            QueryMatcher.Score("sn", "Sticky Notes"),           // initials
            QueryMatcher.Score("ote", "Notepad"),               // anywhere
            QueryMatcher.Score("ntp", "Notepad"),               // letters in order
        ];

        Assert.All(scores, s => Assert.True(s > 0));
        Assert.Equal(scores.OrderDescending(), scores);
    }

    [Fact]
    public void Score_PrefersTheShorterTitleInATier()
    {
        Assert.True(QueryMatcher.Score("ste", "Steam") > QueryMatcher.Score("ste", "Steam Link"));
    }

    [Theory]
    [InlineData("cafe", "Café")]
    [InlineData("SPOTIFY", "spotify")]
    [InlineData("power shell", "PowerShell")]
    [InlineData("win11", "Windows 11")]
    [InlineData("dont", "Don't Starve")]
    [InlineData("ms store", "Microsoft Store")]
    public void Score_MatchesWithoutCaseAccentsOrPunctuation(string query, string title)
    {
        Assert.True(QueryMatcher.Score(query, title) > 0);
    }

    [Theory]
    [InlineData("xyz", "Notepad")]
    [InlineData("pn", "Spotify")]
    [InlineData("", "Notepad")]
    [InlineData("note", "")]
    public void Score_IsZeroWithoutAMatch(string query, string title)
    {
        Assert.Equal(0, QueryMatcher.Score(query, title));
    }

    private static string[] Ids(IReadOnlyList<RankedCandidate> ranked) => [.. ranked.Select(r => r.Candidate.Id)];
}
