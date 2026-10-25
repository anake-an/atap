using System.Text;
using System.Text.Json;
using Atap.Composition;
using Atap.Core.Lyrics;
using Atap.Core.Media;
using Atap.Core.Net;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Lyrics;

namespace Atap.Tests.Media;

public sealed class LyricsWidgetTests : IDisposable
{
    private const string Synced = """{"id":1,"trackName":"Song","artistName":"Band","duration":200,"syncedLyrics":"[00:01.00]one\n[00:05.00]two\n[00:09.00]three"}""";

    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("lyrics");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Expanded);
    private readonly FakePlayer _player = new();

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    private LyricsWidget Create(IHttpDataService http)
    {
        var descriptor = new LyricsDescriptor();
        return new LyricsWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility, http), () => _player);
    }

    [Fact]
    public async Task SyncedLines_FollowThePlayer_WithOneTimerWhileOpenAndPlaying()
    {
        var now = _services.Time.GetUtcNow();
        _player.Play("Song", "Band", new PlaybackSnapshot(TimeSpan.Zero, now, true));
        var widget = Create(new FakeLrclib(get: Synced));
        await widget.StartAsync(SettingValues.Empty);
        await widget.Loading;
        var view = Assert.IsType<LyricsViewModel>(widget.ViewModel);

        Assert.Equal(LyricsState.Synced, view.State);
        Assert.Equal("one", view.NextLine);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        _services.Time.Advance(TimeSpan.FromSeconds(1.1));
        Assert.Equal("one", view.CurrentLine);
        Assert.Equal("two", view.NextLine);

        _player.Seek(new PlaybackSnapshot(TimeSpan.FromSeconds(6), _services.Time.GetUtcNow(), true));
        Assert.Equal("two", view.CurrentLine);

        _visibility.Value = WidgetVisibility.Hidden;
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);

        await widget.StopAsync();
        Assert.True(_player.Disposed);
    }

    [Fact]
    public async Task NoLyrics_IsRemembered_SoTheSongIsNotAskedAgain()
    {
        _player.Play("Rare", "Nobody", null);
        var http = new FakeLrclib(get: null, search: "[]");
        var widget = Create(http);
        await widget.StartAsync(SettingValues.Empty);
        await widget.Loading;

        Assert.Equal(LyricsState.None, Assert.IsType<LyricsViewModel>(widget.ViewModel).State);
        Assert.Equal(2, http.Requests);
        await widget.StopAsync();

        var again = Create(http);
        await again.StartAsync(SettingValues.Empty);
        await again.Loading;

        Assert.Equal(2, http.Requests);
        Assert.Equal(LyricsState.None, Assert.IsType<LyricsViewModel>(again.ViewModel).State);
        await again.StopAsync();
    }

    [Fact]
    public async Task Offline_IsNotRememberedAsNoLyrics()
    {
        _player.Play("Song", "Band", null);
        var widget = Create(new FakeLrclib(get: null, search: null, offline: true));
        await widget.StartAsync(SettingValues.Empty);
        await widget.Loading;

        Assert.Equal(LyricsState.Failed, Assert.IsType<LyricsViewModel>(widget.ViewModel).State);
        await widget.StopAsync();
    }

    [Fact]
    public async Task Translation_IsOffByDefault_AndSendsNothing()
    {
        var descriptor = new LyricsDescriptor();
        Assert.All(
            descriptor.Settings.Where(s => s.Key.StartsWith("translate", StringComparison.Ordinal)),
            s => Assert.True(s.Advanced));
        Assert.Equal([LrclibApi.Host], descriptor.Capabilities.NetworkHosts);

        var now = _services.Time.GetUtcNow();
        _player.Play("Song", "Band", new PlaybackSnapshot(TimeSpan.Zero, now, true));
        var http = new FakeHttp();
        var widget = Create(http);
        await widget.StartAsync(SettingValues.Empty);
        await widget.Loading;
        var view = Assert.IsType<LyricsViewModel>(widget.ViewModel);

        _services.Time.Advance(TimeSpan.FromSeconds(1.1));
        _services.Time.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal("two", view.CurrentLine);
        Assert.Empty(http.Translations);
        Assert.False(view.HasTranslation);
        await widget.StopAsync();
    }

    [Fact]
    public async Task Translation_SendsTheCurrentLineToTheLink_AndShowsTheReply()
    {
        var now = _services.Time.GetUtcNow();
        _player.Play("Song", "Band", new PlaybackSnapshot(TimeSpan.Zero, now, true));
        var http = new FakeHttp { Reply = url => Task.FromResult(Ok($$"""{"translatedText":"[{{Query(url, "q")}}]"}""")) };
        var widget = Create(http);
        await widget.StartAsync(Translate());
        await widget.Loading;
        var view = Assert.IsType<LyricsViewModel>(widget.ViewModel);

        // Nothing to translate before the first line.
        Assert.Empty(http.Translations);

        _services.Time.Advance(TimeSpan.FromSeconds(1.1));
        await widget.Translating;
        Assert.Equal("https://translate.example.com/translate?q=one&target=ms", Assert.Single(http.Translations).AbsoluteUri);
        Assert.Equal("[one]", view.Translation);

        _services.Time.Advance(TimeSpan.FromSeconds(4));
        await widget.Translating;
        Assert.Equal("[two]", view.Translation);

        // A line seen before comes from memory.
        _player.Seek(new PlaybackSnapshot(TimeSpan.FromSeconds(2), _services.Time.GetUtcNow(), true));
        Assert.Equal("[one]", view.Translation);
        Assert.Equal(2, http.Translations.Count);

        // Nothing is sent while the widget is closed.
        _visibility.Value = WidgetVisibility.Hidden;
        _player.Seek(new PlaybackSnapshot(TimeSpan.FromSeconds(10), _services.Time.GetUtcNow(), true));
        Assert.Equal("three", view.CurrentLine);
        Assert.Equal(2, http.Translations.Count);
        await widget.StopAsync();
    }

    [Fact]
    public async Task Translation_SendsOneRequestAtATime()
    {
        var now = _services.Time.GetUtcNow();
        _player.Play("Song", "Band", new PlaybackSnapshot(TimeSpan.Zero, now, true));
        var pending = new TaskCompletionSource<FetchResult>();
        var http = new FakeHttp { Reply = url => Query(url, "q") == "one" ? pending.Task : Task.FromResult(Ok("""{"translatedText":"dua"}""")) };
        var widget = Create(http);
        await widget.StartAsync(Translate());
        await widget.Loading;
        var view = Assert.IsType<LyricsViewModel>(widget.ViewModel);

        _services.Time.Advance(TimeSpan.FromSeconds(1.1));
        _services.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal("two", view.CurrentLine);
        Assert.Single(http.Translations);

        // When the first one ends, the line playing now is asked for.
        pending.SetResult(Ok("""{"translatedText":"satu"}"""));
        await widget.Translating;
        await widget.Translating;
        Assert.Equal(2, http.Translations.Count);
        Assert.Equal("dua", view.Translation);
        await widget.StopAsync();
    }

    [Fact]
    public async Task Translation_Failure_ShowsNoTranslation_AndLyricsKeepWorking()
    {
        var now = _services.Time.GetUtcNow();
        _player.Play("Song", "Band", new PlaybackSnapshot(TimeSpan.Zero, now, true));
        var http = new FakeHttp { Reply = _ => Task.FromResult(FetchResult.Fail(FetchStatus.Failed, FetchError.HttpStatus)) };
        var widget = Create(http);
        await widget.StartAsync(Translate());
        await widget.Loading;
        var view = Assert.IsType<LyricsViewModel>(widget.ViewModel);

        _services.Time.Advance(TimeSpan.FromSeconds(1.1));
        await widget.Translating;
        Assert.Equal("one", view.CurrentLine);
        Assert.False(view.HasTranslation);

        http.Reply = _ => Task.FromResult(Ok("""{"other":"x"}"""));
        _services.Time.Advance(TimeSpan.FromSeconds(4));
        await widget.Translating;
        Assert.Equal(LyricsState.Synced, view.State);
        Assert.Equal("two", view.CurrentLine);
        Assert.Equal("three", view.NextLine);
        Assert.False(view.HasTranslation);

        // A failed line is not asked again for this song.
        _player.Seek(new PlaybackSnapshot(TimeSpan.FromSeconds(2), _services.Time.GetUtcNow(), true));
        Assert.Equal(2, http.Translations.Count);
        await widget.StopAsync();
    }

    [Theory]
    [InlineData("https://{text}.example.com/?lang={lang}")]
    [InlineData("https://translate.example.com/?lang={lang}")]
    [InlineData("ftp://translate.example.com/?q={text}")]
    public void Translation_LinkThatCannotCarryTheLineSafely_IsOff(string link) =>
        Assert.Null(LyricsTranslation.Read(Translate(link)));

    private SettingValues Translate(string link = "https://translate.example.com/translate?q={text}&target={lang}") => new(
        new LyricsDescriptor().Settings,
        new Dictionary<string, JsonElement>
        {
            [LyricsDescriptor.TranslateUrlKey] = JsonSerializer.SerializeToElement(link),
            [LyricsDescriptor.TranslateToKey] = JsonSerializer.SerializeToElement("ms"),
        });

    private static string Query(Uri url, string name) =>
        url.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).First(p => p[0] == name)[1];

    private static FetchResult Ok(string body) => new(FetchStatus.Fresh, Encoding.UTF8.GetBytes(body), DateTimeOffset.UtcNow, FetchError.None);

    /// <summary>LRCLIB with synced lyrics, and the translation link answered by <see cref="Reply"/>.</summary>
    private sealed class FakeHttp : IHttpDataService
    {
        public List<Uri> Translations { get; } = [];

        public Func<Uri, Task<FetchResult>> Reply { get; set; } = _ => Task.FromResult(Ok("""{"translatedText":"x"}"""));

        public Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Url.Host == LrclibApi.Host)
                return Task.FromResult(Ok(Synced));
            Assert.False(request.DiskCache);
            Translations.Add(request.Url);
            return Reply(request.Url);
        }
    }

    private sealed class FakeLrclib(string? get, string? search = null, bool offline = false) : IHttpDataService
    {
        public int Requests { get; private set; }

        public Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Requests++;
            if (offline)
                return Task.FromResult(FetchResult.Fail(FetchStatus.Failed, FetchError.Offline));
            var body = request.Url.AbsolutePath.EndsWith("/get", StringComparison.Ordinal) ? get : search;
            return Task.FromResult(body is null
                ? FetchResult.Fail(FetchStatus.Failed, FetchError.HttpStatus)
                : new FetchResult(FetchStatus.Fresh, Encoding.UTF8.GetBytes(body), DateTimeOffset.UtcNow, FetchError.None));
        }
    }

    private sealed class FakePlayer : ILyricsPlayer
    {
        public NowPlaying Current { get; private set; } = NowPlaying.Nothing;

        public string? Album => null;

        public TimeSpan? Duration => null;

        public PlaybackSnapshot? Position { get; private set; }

        public bool Disposed { get; private set; }

        public event EventHandler? Changed;

        public event EventHandler? PositionChanged;

        public void Play(string title, string artist, PlaybackSnapshot? position)
        {
            Current = new NowPlaying(true, title, artist, PlaybackState.Playing, true, true);
            Position = position;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(PlaybackSnapshot position)
        {
            Position = position;
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Disposed = true;
    }
}
