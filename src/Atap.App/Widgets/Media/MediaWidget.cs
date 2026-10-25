using Atap.Core;
using Atap.Core.Media;
using Atap.Core.Shared;
using Atap.Core.Widgets;
using Atap.Platform.Media;

namespace Atap.Widgets.Media;

/// <summary>
/// Now-playing info and controls from Windows' global media session (SMTC), through the shared
/// <see cref="MediaSessionSource"/>. Event-driven. Live while the current session is playing. The progress bar
/// follows from the last reported position, with a tick once a second only while the open notch shows it and
/// the song plays.
/// </summary>
public sealed class MediaWidget : WidgetBase
{
    private readonly MediaViewModel _viewModel;
    private Lease<MediaSessionSource>? _source;
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
        _source = Context.Shared.Acquire<MediaSessionSource>();
        _source.Value.Changed += OnSourceChanged;
        _source.Value.PositionChanged += OnPositionChanged;
        Context.Visibility.Changed += OnPositionChanged;
        Show();
        await _source.Value.WhenReady.WaitAsync(cancellationToken);
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnPositionChanged;
        StopTick();
        if (_source is not null)
        {
            _source.Value.Changed -= OnSourceChanged;
            _source.Value.PositionChanged -= OnPositionChanged;
            _source.Dispose();
            _source = null;
        }
        _viewModel.Clear();
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
        var open = Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel;
        if (open && PlaybackProgress.Moves(source.Position, source.Duration))
            _tick ??= Context.Scheduler.EverySecond(UpdateProgress);
        else
            StopTick();
    }

    private void UpdateProgress()
    {
        if (_source?.Value is { } source)
            _viewModel.SetProgress(PlaybackProgress.Fraction(source.Position, source.Duration, Context.Time.GetUtcNow()));
    }

    private void StopTick()
    {
        _tick?.Dispose();
        _tick = null;
    }
}
