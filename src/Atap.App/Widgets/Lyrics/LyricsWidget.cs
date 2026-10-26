using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Lyrics;
using Atap.Core.Media;
using Atap.Core.Net;
using Atap.Core.Shared;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Media;
using Atap.Widgets.Lyrics;

[assembly: AtapWidget(typeof(LyricsDescriptor))]

namespace Atap.Widgets.Lyrics;

/// <summary>Lyrics for the song that is playing, from LRCLIB. Constants only.</summary>
public sealed class LyricsDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "lyrics";
    public const string RomanizeKey = "romanize";
    public const string TranslateUrlKey = "translateUrl";
    public const string TranslateToKey = "translateTo";
    public const string TranslatePathKey = "translatePath";
    public const string MissesFile = "misses.json";

    /// <summary>Language codes offered for translation.</summary>
    public static readonly IReadOnlyList<string> Languages = ["en", "ms", "zh", "ta", "ja", "ko", "ar"];

    /// <summary>Found lyrics come from the HTTP cache for this long.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromDays(30);

    public string TypeId => TypeIdValue;

    public string NameKey => "lyrics.name";

    public string DescriptionKey => "lyrics.description";

    public WidgetGroup Group => WidgetGroup.Media;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Medium, WidgetSize.Large];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(RomanizeKey, "lyrics.romanize", SettingType.Bool, false) { DescriptionKey = "lyrics.romanizeHint" },
        new(TranslateUrlKey, "lyrics.translateUrl", SettingType.Url, null)
        {
            DescriptionKey = "lyrics.translateUrlHint", MaxLength = 2048, Advanced = true,
            // The link may hold a key in its query, so it stays on this PC and is never synced.
            Scope = SettingScope.Machine,
        },
        new(TranslateToKey, "lyrics.translateTo", SettingType.Choice, "en", Languages) { Advanced = true },
        new(TranslatePathKey, "lyrics.translatePath", SettingType.String, "translatedText")
        {
            DescriptionKey = "lyrics.translatePathHint", MaxLength = 512, Advanced = true,
        },
    ];

    // UserUrls is only for the translation link the user enters. LRCLIB stays the one declared host, and its
    // requests are built here from fixed https addresses.
    public WidgetCapabilities Capabilities { get; } =
        new(WidgetPermissions.Network | WidgetPermissions.UserUrls, [LrclibApi.Host], "lyrics.privacy");

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Lyrics/LyricsViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) =>
        new LyricsWidget(context, () => new MediaLyricsPlayer(context.Shared.Acquire<MediaSessionSource>()));
}

/// <summary>The player as lyrics need it. Faked in tests.</summary>
internal interface ILyricsPlayer : IDisposable
{
    NowPlaying Current { get; }

    string? Album { get; }

    TimeSpan? Duration { get; }

    PlaybackSnapshot? Position { get; }

    /// <summary>The song or the playback state changed.</summary>
    event EventHandler? Changed;

    /// <summary>The position jumped, such as after a seek.</summary>
    event EventHandler? PositionChanged;
}

internal sealed class MediaLyricsPlayer(Lease<MediaSessionSource> lease) : ILyricsPlayer
{
    public NowPlaying Current => lease.Value.Current;

    public string? Album => lease.Value.Album;

    public TimeSpan? Duration => lease.Value.Duration;

    public PlaybackSnapshot? Position => lease.Value.Position;

    public event EventHandler? Changed
    {
        add => lease.Value.Changed += value;
        remove => lease.Value.Changed -= value;
    }

    public event EventHandler? PositionChanged
    {
        add => lease.Value.PositionChanged += value;
        remove => lease.Value.PositionChanged -= value;
    }

    public void Dispose() => lease.Dispose();
}

/// <summary>
/// Looks a song up once when it starts: /api/get, then /api/search. Found lyrics come from the HTTP cache for 30 days.
/// A song with none is not asked again for 7 days. Synced lines move with one scheduler At per line, only while the
/// widget is open and the song plays. A seek re-syncs. Song names and lyrics are never logged.
/// With a translation link set, the current synced line is translated through that link while the widget is open,
/// one request at a time, and kept in memory for the song.
/// The line playing now is also published to the shared <see cref="LyricLine"/>, which the music widget can show in
/// the small notch. While it does, the lines keep moving with the widget closed.
/// </summary>
public sealed class LyricsWidget : WidgetBase
{
    private readonly Func<ILyricsPlayer> _createPlayer;
    private readonly LyricsViewModel _viewModel;
    // Translations of this song's lines. Empty text means the line could not be translated.
    private readonly Dictionary<string, string> _translations = new(StringComparer.Ordinal);
    private ILyricsPlayer? _player;
    private CancellationTokenSource? _stopping;
    private CancellationTokenSource? _loading;
    private CancellationTokenSource? _translating;
    private LyricsTranslation? _translation;
    private (string Title, string Artist)? _song;
    private LyricsTimeline? _timeline;
    private IDisposable? _nextLine;
    private Lease<LyricLine>? _line;

    internal LyricsWidget(WidgetContext context, Func<ILyricsPlayer> createPlayer)
        : base(context)
    {
        _createPlayer = createPlayer;
        _viewModel = new LyricsViewModel(context.Text);
        _viewModel.PropertyChanged += OnViewChanged;
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _viewModel.Romanize = settings.GetBool(LyricsDescriptor.RomanizeKey);
        _translation = LyricsTranslation.Read(settings);
        _stopping = new CancellationTokenSource();
        _line = Context.Shared.Acquire<LyricLine>();
        _line.Value.WantedChanged += OnPositionChanged;
        _player = _createPlayer();
        _player.Changed += OnPlayerChanged;
        _player.PositionChanged += OnPositionChanged;
        Context.Visibility.Changed += OnPositionChanged;
        OnPlayerChanged(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnPositionChanged;
        if (_player is not null)
        {
            _player.Changed -= OnPlayerChanged;
            _player.PositionChanged -= OnPositionChanged;
            _player.Dispose();
            _player = null;
        }
        _nextLine?.Dispose();
        _nextLine = null;
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;
        ForgetTranslations();
        _translation = null;
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
        _song = null;
        _timeline = null;
        if (_line is not null)
        {
            _line.Value.WantedChanged -= OnPositionChanged;
            _line.Value.Publish(null);
            _line.Dispose();
            _line = null;
        }
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.PropertyChanged -= OnViewChanged;
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    /// <summary>Shares the line playing now. A new state clears every line, and raises an empty name.</summary>
    private void OnViewChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(LyricsViewModel.CurrentLine))
            _line?.Value.Publish(_viewModel.CurrentLine);
    }

    /// <summary>The load in progress, for tests.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The translation in progress, for tests.</summary>
    internal Task Translating { get; private set; } = Task.CompletedTask;

    private void OnPlayerChanged(object? sender, EventArgs e)
    {
        if (_player is not { } player)
            return;
        var now = player.Current;
        (string, string)? song = now.Title is { } title && now.Artist is { } artist ? (title, artist) : null;
        if (song != _song)
        {
            _song = song;
            _timeline = null;
            ForgetTranslations();
            _loading?.Cancel();
            _loading?.Dispose();
            _loading = null;
            if (song is { } s)
            {
                _viewModel.ShowState(LyricsState.Loading);
                _loading = CancellationTokenSource.CreateLinkedTokenSource(_stopping!.Token);
                Loading = LoadAsync(SongTags.Query(s.Item1, s.Item2, player.Album, player.Duration), _loading.Token);
            }
            else
            {
                _viewModel.ShowState(LyricsState.NoSong);
            }
        }
        Cue();
    }

    private void OnPositionChanged(object? sender, EventArgs e) => Cue();

    private async Task LoadAsync(LyricsQuery query, CancellationToken token)
    {
        var key = LyricsMisses.Key(query);
        var misses = Context.Storage.Load(LyricsDescriptor.MissesFile, LyricsMissesJsonContext.Default.LyricsMisses, new LyricsMisses(), DataScope.Machine);
        if (misses.IsMissing(key, Context.Time.GetUtcNow()))
        {
            _viewModel.ShowState(LyricsState.None);
            return;
        }
        try
        {
            var answered = true;
            LrclibTrack? track = null;
            if (LrclibApi.GetUri(query) is { } getUri)
            {
                var result = await Context.Http.GetAsync(new FetchRequest(getUri, LyricsDescriptor.CacheFor), token);
                answered &= Answered(result);
                if (!result.Body.IsEmpty)
                    track = LrclibApi.ParseTrack(result.Body.Span);
            }
            if ((track is null || track.Kind == LyricsKind.None) && LrclibApi.SearchUri(query) is { } searchUri)
            {
                var result = await Context.Http.GetAsync(new FetchRequest(searchUri, LyricsDescriptor.CacheFor), token);
                answered = Answered(result);
                if (!result.Body.IsEmpty)
                    track = LrclibApi.PickBest(LrclibApi.ParseSearch(result.Body.Span), query);
            }
            // Last, the title without extras such as "(feat. Someone)" or " - Remastered", which LRCLIB rarely has.
            var plainTitle = SongTags.PlainTitle(query.Title);
            if ((track is null || track.Kind == LyricsKind.None) && plainTitle != query.Title
                && LrclibApi.SearchUri(query with { Title = plainTitle }) is { } plainUri)
            {
                var result = await Context.Http.GetAsync(new FetchRequest(plainUri, LyricsDescriptor.CacheFor), token);
                answered = Answered(result);
                if (!result.Body.IsEmpty)
                    track = LrclibApi.PickBest(LrclibApi.ParseSearch(result.Body.Span), query with { Title = plainTitle });
            }
            if (token.IsCancellationRequested)
                return;
            switch (track?.Kind)
            {
                case LyricsKind.Synced:
                    _timeline = new LyricsTimeline(LrcParser.Parse(track.SyncedLyrics));
                    _viewModel.ShowState(LyricsState.Synced);
                    Cue();
                    break;
                case LyricsKind.Plain:
                    _viewModel.ShowPlain(track.PlainLyrics!);
                    break;
                case LyricsKind.Instrumental:
                    _viewModel.ShowState(LyricsState.Instrumental);
                    break;
                default:
                    if (answered)
                    {
                        misses.Add(key, Context.Time.GetUtcNow());
                        Context.Storage.Save(LyricsDescriptor.MissesFile, misses, LyricsMissesJsonContext.Default.LyricsMisses, DataScope.Machine);
                        _viewModel.ShowState(LyricsState.None);
                    }
                    else
                    {
                        _viewModel.ShowState(LyricsState.Failed);
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The song changed or the widget stopped.
        }
    }

    // LRCLIB answered, even if only to say it has nothing. Offline, blocked or failed requests are not an answer.
    private static bool Answered(FetchResult result) =>
        result.Status is FetchStatus.Fresh or FetchStatus.Cached or FetchStatus.Stale || result.Error == FetchError.HttpStatus;

    /// <summary>
    /// Shows the line for now and arms one timer for the next, only while playing and open, or while the small
    /// notch shows the line.
    /// </summary>
    private void Cue()
    {
        _nextLine?.Dispose();
        _nextLine = null;
        if (_timeline is not { } timeline || _player?.Position is not { } position)
            return;
        var cue = timeline.Cue(position, Context.Time.GetUtcNow());
        _viewModel.ShowLines(
            cue.Index >= 0 ? timeline.Lines[cue.Index].Text : string.Empty,
            cue.NextIndex >= 0 ? timeline.Lines[cue.NextIndex].Text : string.Empty);
        var open = Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel;
        var follow = open || _line?.Value.IsWanted == true;
        if (follow && position.IsPlaying && cue.NextAt is { } next)
            _nextLine = Context.Scheduler.At(next, Cue);
        Translate(open);
    }

    /// <summary>
    /// Shows the current line's translation when it is known. Otherwise asks the link for it while the widget is
    /// open, unless a request is already out. That one picks up the line playing when it ends.
    /// </summary>
    private void Translate(bool open)
    {
        var line = _viewModel.CurrentLine;
        if (_translation is not { } translation || string.IsNullOrWhiteSpace(line))
        {
            _viewModel.ShowTranslation(string.Empty);
            return;
        }
        if (_translations.TryGetValue(line, out var known))
        {
            _viewModel.ShowTranslation(known);
            return;
        }
        _viewModel.ShowTranslation(string.Empty);
        if (!open || _translating is not null || _stopping is null)
            return;
        _translating = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        Translating = TranslateAsync(translation, line, _translating);
    }

    private async Task TranslateAsync(LyricsTranslation translation, string line, CancellationTokenSource request)
    {
        string? text = null;
        try
        {
            if (translation.Request(line) is { } fetch)
                text = translation.ReadReply(await Context.Http.GetAsync(fetch, request.Token));
        }
        catch (OperationCanceledException)
        {
            // The song changed or the widget stopped.
        }
        if (request.IsCancellationRequested)
            return;
        _translating = null;
        request.Dispose();
        // A failure is kept too, so the line is not asked again for this song.
        _translations[line] = text ?? string.Empty;
        Translate(Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel);
    }

    /// <summary>Drops this song's translations and the request in flight.</summary>
    private void ForgetTranslations()
    {
        _translations.Clear();
        _translating?.Cancel();
        _translating?.Dispose();
        _translating = null;
        _viewModel.ShowTranslation(string.Empty);
    }
}

public enum LyricsState
{
    NoSong,
    Loading,
    Synced,
    Plain,
    Instrumental,
    None,
    Failed,
}

public sealed class LyricsViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private LyricsState _state = LyricsState.NoSong;

    public LyricsViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public bool Romanize { get; set; }

    public LyricsState State => _state;

    public string CurrentLine { get; private set; } = string.Empty;

    public string NextLine { get; private set; } = string.Empty;

    /// <summary>The current line in Latin letters when it is Japanese kana or Korean and the setting is on.</summary>
    public string Romanized => Romanize && LyricsRomanizer.CanRomanize(CurrentLine) ? LyricsRomanizer.Romanize(CurrentLine) : string.Empty;

    public bool HasRomanized => Romanized.Length > 0;

    /// <summary>The current line through the user's translation link, or empty.</summary>
    public string Translation { get; private set; } = string.Empty;

    public bool HasTranslation => Translation.Length > 0;

    public string PlainText { get; private set; } = string.Empty;

    public bool IsSynced => _state == LyricsState.Synced;

    public bool IsPlain => _state == LyricsState.Plain;

    public bool HasMessage => _state is not (LyricsState.Synced or LyricsState.Plain);

    public string Message => _state switch
    {
        LyricsState.NoSong => _text["lyrics.noSong"],
        LyricsState.Loading => _text["lyrics.loading"],
        LyricsState.Instrumental => _text["lyrics.instrumental"],
        LyricsState.Failed => _text["lyrics.failed"],
        LyricsState.None => _text["lyrics.none"],
        _ => string.Empty,
    };

    public string AccessibleSummary => _state switch
    {
        LyricsState.Synced => _text.Format("lyrics.accessible", ("line", CurrentLine)),
        LyricsState.Plain => _text["lyrics.name"],
        _ => Message,
    };

    internal void ShowState(LyricsState state)
    {
        _state = state;
        CurrentLine = NextLine = PlainText = Translation = string.Empty;
        OnPropertyChanged(string.Empty);
    }

    internal void ShowTranslation(string text)
    {
        if (text == Translation)
            return;
        Translation = text;
        OnPropertyChanged(nameof(Translation));
        OnPropertyChanged(nameof(HasTranslation));
    }

    internal void ShowPlain(string text)
    {
        _state = LyricsState.Plain;
        var builder = new StringBuilder();
        foreach (var line in text.Split('\n'))
            builder.AppendLine(line.TrimEnd('\r'));
        PlainText = builder.ToString().TrimEnd();
        OnPropertyChanged(string.Empty);
    }

    internal void ShowLines(string current, string next)
    {
        if (current == CurrentLine && next == NextLine)
            return;
        CurrentLine = current;
        NextLine = next;
        OnPropertyChanged(nameof(CurrentLine));
        OnPropertyChanged(nameof(NextLine));
        OnPropertyChanged(nameof(Romanized));
        OnPropertyChanged(nameof(HasRomanized));
        OnPropertyChanged(nameof(AccessibleSummary));
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
