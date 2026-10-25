using System.Text;
using Atap.Core.Lyrics;

namespace Atap.Core.Tests.Lyrics;

public class LrclibApiTests
{
    // The shape of a live /api/get response, with invented lyrics. name, hasWordSync and lyricsfile are extra fields Atap ignores.
    private const string GetResponse = """
        {
          "id": 3396226,
          "name": "Lampu Kota",
          "trackName": "Lampu Kota",
          "artistName": "Hujan Petang",
          "albumName": "Senja di Pantai Timur",
          "duration": 210,
          "instrumental": false,
          "hasWordSync": false,
          "plainLyrics": "Lampu kota menyala satu satu\nAku menunggu bas terakhir pulang\n",
          "syncedLyrics": "[00:12.40] Lampu kota menyala satu satu\n[00:17.85] Aku menunggu bas terakhir pulang\n[03:25.72] ",
          "lyricsfile": "version: '1.0'\nmetadata:\n  title: Lampu Kota\n  artist: Hujan Petang\nlines: []\n"
        }
        """;

    // The shape of a live instrumental record: both lyric fields are null.
    private const string InstrumentalResponse = """
        {"id":37018755,"name":"Ombak Malam","trackName":"Ombak Malam","artistName":"Hujan Petang","albumName":"Senja di Pantai Timur","duration":344.0,"instrumental":true,"hasWordSync":false,"plainLyrics":null,"syncedLyrics":null,"lyricsfile":"version: '1.0'\nlines: []\n"}
        """;

    private const string NotFoundResponse = """
        {"message":"Failed to find specified track","name":"TrackNotFound","statusCode":404}
        """;

    private const string SearchResponse = """
        [
          {"id":11,"trackName":"Lampu Kota (Live)","artistName":"Hujan Petang","albumName":"Konsert","duration":251.0,"instrumental":false,"plainLyrics":"Lampu kota","syncedLyrics":"[00:20.00] Lampu kota"},
          null,
          {"id":12,"trackName":"Lampu Kota","artistName":"Hujan Petang","albumName":"Single","duration":211.0,"instrumental":false,"plainLyrics":"Lampu kota","syncedLyrics":null},
          {"id":13,"trackName":"Lampu Kota","artistName":"Hujan Petang","albumName":"Senja di Pantai Timur","duration":209.0,"instrumental":false,"plainLyrics":"Lampu kota","syncedLyrics":"[00:12.40] Lampu kota"},
          {"trackName":"No id","artistName":"Hujan Petang","duration":210.0},
          {"id":14,"trackName":"Lampu Kota","artistName":"Hujan Petang","duration":210.0,"instrumental":false,"plainLyrics":null,"syncedLyrics":null}
        ]
        """;

    private static readonly LyricsQuery Song = new("Lampu Kota", "Hujan Petang", "Senja di Pantai Timur", TimeSpan.FromSeconds(209.6));

    [Fact]
    public void GetUri_SendsEveryFieldEscapedInAFixedOrder()
    {
        var uri = LrclibApi.GetUri(Song);

        Assert.Equal(
            "https://lrclib.net/api/get?track_name=Lampu%20Kota&artist_name=Hujan%20Petang&album_name=Senja%20di%20Pantai%20Timur&duration=210",
            uri?.AbsoluteUri);
        Assert.Equal(LrclibApi.Host, uri?.Host);
    }

    [Fact]
    public void GetUri_EscapesReservedAndNonLatinCharacters()
    {
        var uri = LrclibApi.GetUri(new LyricsQuery(" Kau & Aku? ", "사랑해 #1", "Don't (Stop)"));

        Assert.Equal(
            "https://lrclib.net/api/get?track_name=Kau%20%26%20Aku%3F&artist_name=%EC%82%AC%EB%9E%91%ED%95%B4%20%231&album_name=Don%27t%20%28Stop%29",
            uri?.AbsoluteUri);
    }

    [Fact]
    public void GetUri_LeavesOutABlankAlbumAndAnUnknownOrOutOfRangeDuration()
    {
        TimeSpan?[] durations = [null, TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(3600.6), TimeSpan.FromSeconds(-5)];

        foreach (var duration in durations)
        {
            var uri = LrclibApi.GetUri(new LyricsQuery("Lampu Kota", "Hujan Petang", "  ", duration));

            Assert.Equal("https://lrclib.net/api/get?track_name=Lampu%20Kota&artist_name=Hujan%20Petang", uri?.AbsoluteUri);
        }
    }

    [Theory]
    [InlineData("", "Hujan Petang")]
    [InlineData("Lampu Kota", " ")]
    public void GetUri_NeedsATitleAndAnArtist(string title, string artist)
    {
        Assert.Null(LrclibApi.GetUri(new LyricsQuery(title, artist)));
    }

    [Fact]
    public void SearchUri_SendsTheTitleAndArtistOnly()
    {
        Assert.Equal(
            "https://lrclib.net/api/search?track_name=Lampu%20Kota&artist_name=Hujan%20Petang",
            LrclibApi.SearchUri(Song)?.AbsoluteUri);
        Assert.Equal(
            "https://lrclib.net/api/search?track_name=Lampu%20Kota",
            LrclibApi.SearchUri(new LyricsQuery("Lampu Kota", ""))?.AbsoluteUri);
        Assert.Null(LrclibApi.SearchUri(new LyricsQuery("", "Hujan Petang")));
    }

    [Fact]
    public void Uris_CutVeryLongFieldsWithoutSplittingACharacter()
    {
        var title = new string('a', LrclibApi.MaxFieldLength - 1) + "\U0001F319" + "tail";

        var uri = LrclibApi.SearchUri(new LyricsQuery(title, ""));

        Assert.Equal("https://lrclib.net/api/search?track_name=" + new string('a', LrclibApi.MaxFieldLength - 1), uri?.AbsoluteUri);
    }

    [Fact]
    public void ParseTrack_ReadsARecord()
    {
        var track = LrclibApi.ParseTrack(GetResponse);

        Assert.NotNull(track);
        Assert.Equal(3396226, track.Id);
        Assert.Equal("Lampu Kota", track.TrackName);
        Assert.Equal("Hujan Petang", track.ArtistName);
        Assert.Equal("Senja di Pantai Timur", track.AlbumName);
        Assert.Equal(210, track.Duration);
        Assert.False(track.Instrumental);
        Assert.Equal(LyricsKind.Synced, track.Kind);

        var lyrics = LrcParser.Parse(track.SyncedLyrics);
        Assert.Equal(3, lyrics.Lines.Count);
        Assert.Equal("Aku menunggu bas terakhir pulang", lyrics.Lines[1].Text);
    }

    [Fact]
    public void ParseTrack_ReadsUtf8Bytes()
    {
        var track = LrclibApi.ParseTrack(Encoding.UTF8.GetBytes(GetResponse));

        Assert.Equal(3396226, track?.Id);
    }

    [Fact]
    public void ParseTrack_InstrumentalRecord()
    {
        var track = LrclibApi.ParseTrack(InstrumentalResponse);

        Assert.Equal(344.0, track?.Duration);
        Assert.Equal(LyricsKind.Instrumental, track?.Kind);
    }

    [Theory]
    [InlineData(NotFoundResponse)]
    [InlineData("Failed to deserialize query string: missing field `artist_name`")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"abc\"}")]
    [InlineData("{\"id\":5,\"instrumental\":\"maybe\"}")]
    [InlineData("{\"id\":5,")]
    [InlineData("<html>Bad gateway</html>")]
    public void ParseTrack_AnythingButARecord_GivesNull(string body)
    {
        Assert.Null(LrclibApi.ParseTrack(body));
        Assert.Null(LrclibApi.ParseTrack(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void Parse_TextWithALoneSurrogate_GivesNothing()
    {
        // A lone surrogate cannot become UTF-8, so the reader would throw. Kept out of InlineData, which cannot carry it.
        Assert.Null(LrclibApi.ParseTrack("{\"id\":1,\"trackName\":\"\uD800\"}"));
        Assert.Empty(LrclibApi.ParseSearch("[{\"id\":1,\"trackName\":\"\uD800\"}]"));
    }

    [Fact]
    public void ParseTrack_DurationAsText_IsStillRead()
    {
        var track = LrclibApi.ParseTrack("""{"id":7,"duration":"210.5","plainLyrics":"Satu"}""");

        Assert.Equal(210.5, track?.Duration);
        Assert.Equal(LyricsKind.Plain, track?.Kind);
    }

    [Fact]
    public void ParseSearch_SkipsNullsAndRecordsWithoutAnId()
    {
        var tracks = LrclibApi.ParseSearch(SearchResponse);

        Assert.Equal([11L, 12L, 13L, 14L], tracks.Select(t => t.Id));
        Assert.Equal(tracks.Select(t => t.Id), LrclibApi.ParseSearch(Encoding.UTF8.GetBytes(SearchResponse)).Select(t => t.Id));
    }

    [Theory]
    [InlineData(GetResponse)]
    [InlineData(NotFoundResponse)]
    [InlineData("[")]
    [InlineData("")]
    [InlineData("null")]
    public void ParseSearch_AnythingButAnArray_GivesNoRecords(string body)
    {
        Assert.Empty(LrclibApi.ParseSearch(body));
    }

    [Fact]
    public void Kind_PrefersInstrumentalThenSyncedThenPlain()
    {
        Assert.Equal(LyricsKind.Instrumental, new LrclibTrack { Instrumental = true, SyncedLyrics = "[00:01.00]x" }.Kind);
        Assert.Equal(LyricsKind.Synced, new LrclibTrack { SyncedLyrics = "[00:01.00]x", PlainLyrics = "x" }.Kind);
        Assert.Equal(LyricsKind.Plain, new LrclibTrack { SyncedLyrics = "  ", PlainLyrics = "x" }.Kind);
        Assert.Equal(LyricsKind.None, new LrclibTrack { PlainLyrics = "\n" }.Kind);
    }

    [Fact]
    public void PickBest_PrefersSyncedLyricsWithinTheDurationWindow()
    {
        // 11 is synced but 41 s too long, 12 is plain only, 13 is synced and 0.6 s off.
        var best = LrclibApi.PickBest(LrclibApi.ParseSearch(SearchResponse), Song);

        Assert.Equal(13, best?.Id);
    }

    [Fact]
    public void PickBest_WithoutAKnownLength_AcceptsEveryLength()
    {
        var tracks = LrclibApi.ParseSearch(SearchResponse);
        var song = Song with { Duration = null };

        // 13 matches the title exactly. Without it, the plain 12 with the exact title beats the synced live record 11,
        // whose timing would not fit the studio track.
        Assert.Equal(13, LrclibApi.PickBest(tracks, song)?.Id);
        Assert.Equal(12, LrclibApi.PickBest(tracks.Where(t => t.Id != 13), song)?.Id);
    }

    [Fact]
    public void PickBest_ZeroNegativeOrTinyLength_CountsAsUnknown()
    {
        // Browsers often report a length of 0. GetUri leaves it out, so PickBest must not measure records against it.
        LrclibTrack[] candidates = [Synced(1, "Lampu Kota", "Hujan Petang", 210)];
        TimeSpan[] durations = [TimeSpan.Zero, TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(3600.6)];

        foreach (var duration in durations)
            Assert.Equal(1, LrclibApi.PickBest(candidates, new LyricsQuery("Lampu Kota", "Hujan Petang", null, duration))?.Id);
    }

    [Fact]
    public void PickBest_TheSameArtist_BeatsASyncedCover()
    {
        LrclibTrack[] candidates =
        [
            Synced(1, "Lampu Kota", "Kumpulan Lain", 210),
            new LrclibTrack { Id = 2, TrackName = "Lampu Kota", ArtistName = "Hujan Petang", Duration = 210, PlainLyrics = "Lampu kota" },
        ];

        Assert.Equal(2, LrclibApi.PickBest(candidates, Song with { Duration = null })?.Id);
        Assert.Equal(2, LrclibApi.PickBest(candidates, Song)?.Id);
    }

    [Fact]
    public void PickBest_PrefersTheSameArtistThenTheSameTitleThenTheClosestLength()
    {
        LrclibTrack[] candidates =
        [
            Synced(1, "Lampu Kota", "Hujan Petang & Kawan", 210),
            Synced(2, "Lampu Kota (Akustik)", "hujan petang", 210),
            Synced(3, "lampu kota", "Hujan Petang", 211.5),
            Synced(4, "Lampu Kota", "HUJAN PETANG", 210.2),
        ];

        Assert.Equal(4, LrclibApi.PickBest(candidates, Song)?.Id);
        Assert.Equal(3, LrclibApi.PickBest(candidates[..3], Song)?.Id);
        Assert.Equal(2, LrclibApi.PickBest(candidates[..2], Song)?.Id);
    }

    [Fact]
    public void PickBest_TieKeepsTheServerOrder()
    {
        LrclibTrack[] candidates = [Synced(8, "Lampu Kota", "Hujan Petang", 210), Synced(5, "Lampu Kota", "Hujan Petang", 210)];

        Assert.Equal(8, LrclibApi.PickBest(candidates, Song with { Duration = TimeSpan.FromSeconds(210) })?.Id);
    }

    [Fact]
    public void PickBest_UnknownRecordLength_IsAcceptedButRankedLast()
    {
        LrclibTrack[] candidates = [Synced(1, "Lampu Kota", "Hujan Petang", null), Synced(2, "Lampu Kota", "Hujan Petang", 211)];
        var plainThatFits = new LrclibTrack { Id = 3, TrackName = "Lampu Kota", ArtistName = "Hujan Petang", Duration = 210, PlainLyrics = "Lampu kota" };

        Assert.Equal(2, LrclibApi.PickBest(candidates, Song)?.Id);
        Assert.Equal(1, LrclibApi.PickBest(candidates[..1], Song)?.Id);
        Assert.Equal(3, LrclibApi.PickBest([candidates[0], plainThatFits], Song)?.Id);
    }

    [Fact]
    public void PickBest_NothingFits_GivesNull()
    {
        LrclibTrack[] candidates =
        [
            Synced(1, "Lampu Kota", "Hujan Petang", 300),
            new LrclibTrack { Id = 2, TrackName = "Lampu Kota", ArtistName = "Hujan Petang", Duration = 210 },
        ];

        Assert.Null(LrclibApi.PickBest(candidates, Song));
        Assert.Null(LrclibApi.PickBest([], Song));
    }

    [Fact]
    public void ToString_ShowsNoLyricsOrSongDetails()
    {
        var track = new LrclibTrack
        {
            Id = 9,
            TrackName = "Lampu Kota",
            ArtistName = "Hujan Petang",
            AlbumName = "Senja di Pantai Timur",
            PlainLyrics = "Lampu kota",
            SyncedLyrics = "[00:01.00] Lampu kota",
        };

        Assert.Equal("LrclibTrack { Id = 9, Kind = Synced }", track.ToString());
        Assert.Equal("LyricsQuery { Duration = 00:03:29.6000000 }", Song.ToString());
        Assert.Equal("LrcLine { Time = 00:00:01 }", new LrcLine(TimeSpan.FromSeconds(1), "Lampu kota").ToString());
    }

    private static LrclibTrack Synced(long id, string title, string artist, double? seconds) => new()
    {
        Id = id,
        TrackName = title,
        ArtistName = artist,
        Duration = seconds,
        SyncedLyrics = "[00:01.00] Lampu kota",
    };
}
