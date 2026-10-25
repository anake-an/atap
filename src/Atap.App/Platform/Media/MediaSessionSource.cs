using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atap.Core.Lyrics;
using Atap.Core.Media;
using Atap.Core.Shared;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Atap.Platform.Media;

/// <summary>Play, pause and skip on whatever plays now. The media commands use it, and tests pass their own.</summary>
internal interface IMediaControls
{
    NowPlaying Current { get; }

    Task PlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();
}

/// <summary>
/// Windows' current media session (SMTC), shared by the media widget and the media commands. Fully event-driven:
/// it listens to session, track and playback changes only. Made on the first lease and gone 30 seconds after the
/// last, so a command does not start and stop it each time. Everything it raises arrives on the UI thread.
/// </summary>
internal sealed class MediaSessionSource : ISharedService<MediaSessionSource>, IMediaControls
{
    private const int ArtDecodeSize = 128;

    private readonly SynchronizationContext _ui;
    private readonly Task _ready;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _disposed;

    // Bumped on every track refresh so a slow, stale thumbnail load cannot overwrite a newer one.
    private int _trackVersion;

    private MediaSessionSource(SynchronizationContext ui)
    {
        _ui = ui;
        _ready = StartAsync();
    }

    /// <summary>Raised on the UI thread when the session, the track or the playback changes.</summary>
    public event EventHandler? Changed;

    public NowPlaying Current { get; private set; } = NowPlaying.Nothing;

    /// <summary>The album art at a small size, or null when the app gives none.</summary>
    public ImageSource? Art { get; private set; }

    /// <summary>The album name, or null when the app gives none.</summary>
    public string? Album { get; private set; }

    /// <summary>The song length, or null when the app does not report one.</summary>
    public TimeSpan? Duration { get; private set; }

    /// <summary>The last reported song position, or null when the app does not report one.</summary>
    public PlaybackSnapshot? Position { get; private set; }

    /// <summary>Raised on the UI thread when the position jumps or the playback state changes. Not raised as the song plays.</summary>
    public event EventHandler? PositionChanged;

    public static MediaSessionSource Create(SharedServiceContext context) => new(context.Ui);

    /// <summary>Done once Windows has answered with the session manager, or failed to.</summary>
    public Task WhenReady => _ready;

    public void Dispose()
    {
        _disposed = true;
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
        DetachSession();
    }

    public Task PlayPauseAsync() => RunControlAsync(s => s.TryTogglePlayPauseAsync().AsTask());

    public Task NextAsync() => RunControlAsync(s => s.TrySkipNextAsync().AsTask());

    public Task PreviousAsync() => RunControlAsync(s => s.TrySkipPreviousAsync().AsTask());

    private async Task StartAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed)
                return;
            _manager = manager;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            await AttachSessionAsync(manager.GetCurrentSession());
        }
        catch (Exception ex)
        {
            // Without media sessions (some Windows editions), nothing plays as far as Atap knows.
            Debug.WriteLine($"Media: session manager failed: {ex.Message}");
        }
    }

    // SMTC events arrive on thread-pool threads; everything below runs on the UI thread.
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ui.Post(_ =>
        {
            if (_manager is not null)
                _ = AttachSessionAsync(_manager.GetCurrentSession());
        }, null);

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ui.Post(_ => _ = RefreshTrackAsync(), null);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _ui.Post(_ => RefreshPlayback(), null);

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        _ui.Post(_ => RefreshPosition(), null);

    private async Task AttachSessionAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        DetachSession();
        if (_disposed)
            return;
        _session = session;

        if (session is null)
        {
            _trackVersion++;
            Art = null;
            Publish(NowPlaying.Nothing);
            return;
        }

        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelineChanged;
        Publish(Current with { HasSession = true });
        RefreshPlayback();
        await RefreshTrackAsync();
    }

    private void DetachSession()
    {
        if (_session is null)
            return;
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
        _session = null;
        Duration = null;
        Position = null;
    }

    private void RefreshPlayback()
    {
        var session = _session;
        if (session is null)
            return;

        GlobalSystemMediaTransportControlsSessionPlaybackInfo? info = null;
        try
        {
            info = session.GetPlaybackInfo();
        }
        catch (Exception ex)
        {
            // The source app can close at any moment.
            Debug.WriteLine($"Media: playback info failed: {ex.Message}");
        }

        Publish(Current with
        {
            State = info is null ? PlaybackState.None : MediaStateMapper.FromSessionStatus((int)info.PlaybackStatus),
            CanPrevious = info?.Controls.IsPreviousEnabled ?? false,
            CanNext = info?.Controls.IsNextEnabled ?? false,
        });
        _rate = info?.PlaybackRate ?? 1;
        RefreshPosition();
    }

    private double _rate = 1;

    // Apps report the position when it jumps or the state changes. Between reports it follows from the snapshot.
    private void RefreshPosition()
    {
        var session = _session;
        if (session is null)
            return;
        try
        {
            var timeline = session.GetTimelineProperties();
            var length = timeline.EndTime - timeline.StartTime;
            Duration = length > TimeSpan.Zero ? length : null;
            // An app that reports no timeline gives zero for everything. Lyrics then cannot follow it.
            Position = Duration is null && timeline.Position == TimeSpan.Zero
                ? null
                : new PlaybackSnapshot(timeline.Position - timeline.StartTime, timeline.LastUpdatedTime, Current.IsPlaying, _rate);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Media: timeline failed: {ex.Message}");
            Position = null;
        }
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshTrackAsync()
    {
        var session = _session;
        if (session is null)
            return;

        var version = ++_trackVersion;
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            if (version != _trackVersion)
                return;
            Album = MediaStateMapper.CleanText(properties?.AlbumTitle);
            Publish(Current with
            {
                Title = MediaStateMapper.CleanText(properties?.Title),
                Artist = MediaStateMapper.CleanText(properties?.Artist),
            });

            var art = await LoadThumbnailAsync(properties?.Thumbnail);
            if (version != _trackVersion)
                return;
            Art = art;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Media: track refresh failed: {ex.Message}");
        }
    }

    private void Publish(NowPlaying next)
    {
        if (next == Current)
            return;
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Decodes the thumbnail at a small size. Returns null when the app gives none.</summary>
    private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
            return null;

        try
        {
            using var winRtStream = await thumbnail.OpenReadAsync();
            using var source = winRtStream.AsStreamForRead();
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = ArtDecodeSize;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Media: thumbnail failed: {ex.Message}");
            return null;
        }
    }

    private async Task RunControlAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        await _ready;
        var session = _session;
        if (session is null)
            return;
        try
        {
            await action(session);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Media: control failed: {ex.Message}");
        }
    }
}
