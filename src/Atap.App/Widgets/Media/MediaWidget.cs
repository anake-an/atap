using Atap.Core.Shared;
using Atap.Core.Widgets;
using Atap.Platform.Media;

namespace Atap.Widgets.Media;

/// <summary>
/// Now-playing info and controls from Windows' global media session (SMTC), through the shared
/// <see cref="MediaSessionSource"/>. Fully event-driven. Live while the current session is playing.
/// </summary>
public sealed class MediaWidget : WidgetBase
{
    private readonly MediaViewModel _viewModel;
    private Lease<MediaSessionSource>? _source;

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
        Show();
        await _source.Value.WhenReady.WaitAsync(cancellationToken);
    }

    public override Task StopAsync()
    {
        if (_source is not null)
        {
            _source.Value.Changed -= OnSourceChanged;
            _source.Dispose();
            _source = null;
        }
        _viewModel.Clear();
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        if (_source?.Value is not { } source)
            return;
        var now = source.Current;
        if (!now.HasSession)
        {
            _viewModel.Clear();
            SetIsLive(false);
            return;
        }
        _viewModel.HasSession = true;
        _viewModel.SetTrack(now.Title, now.Artist);
        _viewModel.AlbumArt = source.Art;
        _viewModel.IsPlaying = now.IsPlaying;
        _viewModel.CanSkipPrevious = now.CanPrevious;
        _viewModel.CanSkipNext = now.CanNext;
        SetIsLive(now.IsPlaying);
    }
}
