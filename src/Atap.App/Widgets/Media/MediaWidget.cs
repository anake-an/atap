using Atap.Core;
using Atap.Core.Lyrics;
using Atap.Core.Media;
using Atap.Core.Shared;
using Atap.Core.Widgets;
using Atap.Platform.Media;

namespace Atap.Widgets.Media;

/// <summary>
/// Now-playing info and controls from Windows' global media session (SMTC), through the shared
/// <see cref="MediaSessionSource"/>. Event-driven. Live while the current session is playing. The progress bar
/// follows from the last reported position, with a tick once a second only while the notch shows it and
/// the song plays. With the lyric line chosen for the small notch, it reads the shared <see cref="LyricLine"/>
/// that the lyrics widget publishes, and asks for it to keep moving only while the small notch shows it.
/// </summary>
public sealed class MediaWidget : WidgetBase
{
    private readonly MediaViewModel _viewModel;
    private Lease<MediaSessionSource>? _source;
    private Lease<LyricLine>? _line;
    private IDisposable? _wantLine;
    private IDisposable? _tick;

    public MediaWidget(WidgetContext context)
        : base(context)
    {
        _viewModel = new MediaViewModel(
            context.Text,
            () => _source?.Value.PreviousAsync() ?? Task.CompletedTask,
            () => _source?.Value.PlayPauseAsync() ?? Task.CompletedTask,
            () => _source?.Value.NextAsync() ?? Task.CompletedTask);
    }

    public override object ViewModel => _viewModel;

    public override async Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _viewModel.PrefersLyricLine = settings.GetString(MediaDescriptor.CompactShowsKey) == MediaDescriptor.ShowLyric;
        if (_viewModel.PrefersLyricLine)
        {
            _line = Context.Shared.Acquire<LyricLine>();
            _line.Value.Changed += OnLineChanged;
            _viewModel.LyricLine = _line.Value.Text;
        }
        _source = Context.Shared.Acquire<MediaSessionSource>();
        _source.Value.Changed += OnSourceChanged;
        _source.Value.PositionChanged += OnPositionChanged;
        Context.Visibility.Changed += OnVisibilityChanged;
        _viewModel.PropertyChanged += OnViewChanged;
        Show();
        WantLine();
        await _source.Value.WhenReady.WaitAsync(cancellationToken);
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _viewModel.PropertyChanged -= OnViewChanged;
        StopTick();
        _wantLine?.Dispose();
        _wantLine = null;
        if (_line is not null)
        {
            _line.Value.Changed -= OnLineChanged;
            _line.Dispose();
            _line = null;
        }
        if (_source is not null)
        {
            _source.Value.Changed -= OnSourceChanged;
            _source.Value.PositionChanged -= OnPositionChanged;
            _source.Dispose();
            _source = null;
        }
        _viewModel.Clear();
        _viewModel.LyricLine = string.Empty;
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        StopTick();
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Show();

    private void OnPositionChanged(object? sender, EventArgs e) => ShowProgress();

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        ShowProgress();
        WantLine();
    }

    // The middle of the small notch came or went, such as on a move to a side edge, where it is left out.
    private void OnViewChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaViewModel.IsCompactMiddleShown))
            OnVisibilityChanged(sender, e);
    }

    private void OnLineChanged(object? sender, EventArgs e)
    {
        if (_line?.Value is { } line)
            _viewModel.LyricLine = line.Text;
    }

    /// <summary>The lyric line moves on its own only while the small notch shows it, so a hidden one costs nothing.</summary>
    private void WantLine()
    {
        if (_line?.Value is { } line && CompactMiddleShown)
        {
            _wantLine ??= line.Want();
            return;
        }
        _wantLine?.Dispose();
        _wantLine = null;
    }

    private void Show()
    {
        if (_source?.Value is not { } source)
            return;
        var now = source.Current;
        if (!now.HasSession)
        {
            _viewModel.Clear();
            StopTick();
            SetIsLive(false);
            return;
        }
        _viewModel.HasSession = true;
        _viewModel.SetTrack(now.Title, now.Artist);
        _viewModel.AlbumArt = source.Art;
        _viewModel.IsPlaying = now.IsPlaying;
        _viewModel.CanSkipPrevious = now.CanPrevious;
        _viewModel.CanSkipNext = now.CanNext;
        ShowProgress();
        SetIsLive(now.IsPlaying);
    }

    /// <summary>Sets the bar for now, and keeps the tick only while it is on screen and moving.</summary>
    private void ShowProgress()
    {
        var source = _source?.Value;
        if (source is null || !source.Current.HasSession)
        {
            _viewModel.SetProgress(null);
            StopTick();
            return;
        }
        UpdateProgress();
        // The small notch has its own thin progress line, so it ticks there too, but not on a side edge without it.
        var shown = Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel || CompactMiddleShown;
        if (shown && PlaybackProgress.Moves(source.Position, source.Duration))
            _tick ??= Context.Scheduler.EverySecond(UpdateProgress);
        else
            StopTick();
    }

    private void UpdateProgress()
    {
        if (_source?.Value is { } source)
            _viewModel.SetProgress(PlaybackProgress.Fraction(source.Position, source.Duration, Context.Time.GetUtcNow()));
    }

    /// <summary>The small notch shows the title and the progress line now. Never on a side edge.</summary>
    private bool CompactMiddleShown => Context.Visibility.Value == WidgetVisibility.Compact && _viewModel.IsCompactMiddleShown;

    private void StopTick()
    {
        _tick?.Dispose();
        _tick = null;
    }
}
