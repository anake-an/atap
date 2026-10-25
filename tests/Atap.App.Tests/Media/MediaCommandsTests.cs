using Atap.Core.Alerts;
using Atap.Core.Media;
using Atap.Features.Gestures;
using Atap.Features.MediaCommands;
using Atap.Platform.Audio;
using Atap.Platform.Media;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Media;

/// <summary>The media and audio commands, and the level they show on the notch.</summary>
public sealed class MediaCommandsTests : IAsyncLifetime
{
    private readonly TestServices _services = new();
    private readonly FakeMedia _media = new();
    private readonly FakeVolume _volume = new();
    private readonly FakeAlerts _alerts = new();
    private MediaCommandsFeature _feature = null!;

    public async Task InitializeAsync()
    {
        _feature = new MediaCommandsFeature(_services.Commands, _alerts, _services.Text, new MediaAccess(_media), new VolumeAccess(_volume));
        await _feature.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _feature.DisposeAsync();
        _services.Dispose();
    }

    [Theory]
    [InlineData(GestureCommands.PlayPause, "playPause")]
    [InlineData(MediaCommandsFeature.NextCommand, "next")]
    [InlineData(MediaCommandsFeature.PreviousCommand, "previous")]
    public async Task MusicCommands_ReachWhateverPlays(string command, string action)
    {
        Assert.True(await _services.Commands.TryRunAsync(command));

        Assert.Equal([action], _media.Actions);
    }

    [Fact]
    public async Task VolumeUp_StepsOnTheGrid_AndShowsTheLevel()
    {
        _volume.Percent = 37;

        Assert.True(await _services.Commands.TryRunAsync(GestureCommands.VolumeUp));

        Assert.Equal(38, _volume.Percent);
        var alert = Assert.Single(_alerts.Shown);
        Assert.Equal(AlertPriority.Overlay, alert.Priority);
        Assert.Equal(MediaCommandsFeature.VolumeAlertKey, alert.Key);
        Assert.Equal(0.38, alert.Level!.Value, 3);
        Assert.Equal("volume", alert.IconKey);
    }

    [Fact]
    public async Task VolumeDown_StopsAtZero_WithTheMuteIcon()
    {
        _volume.Percent = 1;

        await _services.Commands.TryRunAsync(GestureCommands.VolumeDown);

        Assert.Equal(0, _volume.Percent);
        Assert.Equal("mute", _alerts.Shown[^1].IconKey);
    }

    [Fact]
    public async Task VolumeUp_Unmutes()
    {
        _volume.Percent = 20;
        _volume.Muted = true;

        await _services.Commands.TryRunAsync(GestureCommands.VolumeUp);

        Assert.False(_volume.Muted);
        Assert.Equal(22, _volume.Percent);
    }

    [Fact]
    public async Task VolumeDown_KeepsMute_AndSaysMuted()
    {
        _volume.Percent = 20;
        _volume.Muted = true;

        await _services.Commands.TryRunAsync(GestureCommands.VolumeDown);

        Assert.True(_volume.Muted);
        Assert.Equal(_services.Text["mediacommands.muted"], _alerts.Shown[^1].Title);
        Assert.Equal(0, _alerts.Shown[^1].Level);
    }

    [Fact]
    public async Task Mute_Toggles()
    {
        _volume.Percent = 50;

        await _services.Commands.TryRunAsync(MediaCommandsFeature.MuteCommand);
        Assert.True(_volume.Muted);

        await _services.Commands.TryRunAsync(MediaCommandsFeature.MuteCommand);
        Assert.False(_volume.Muted);
        Assert.Equal(0.5, _alerts.Shown[^1].Level);
    }

    [Fact]
    public async Task WithoutSpeakers_NothingChangesOrShows()
    {
        _volume.HasDevice = false;

        await _services.Commands.TryRunAsync(GestureCommands.VolumeUp);
        await _services.Commands.TryRunAsync(MediaCommandsFeature.MuteCommand);

        Assert.Empty(_alerts.Shown);
    }

    [Fact]
    public async Task Stopping_RemovesTheCommands()
    {
        await _feature.DisposeAsync();

        Assert.False(_services.Commands.Contains(GestureCommands.PlayPause));
        Assert.False(_services.Commands.Contains(GestureCommands.VolumeUp));
    }

    private sealed class MediaAccess(IMediaControls media) : IMediaAccess
    {
        public Task RunAsync(Func<IMediaControls, Task> action) => action(media);
    }

    private sealed class VolumeAccess(IVolumeControl volume) : IVolumeAccess
    {
        public void Run(Action<IVolumeControl> action) => action(volume);
    }

    private sealed class FakeMedia : IMediaControls
    {
        public List<string> Actions { get; } = [];

        public NowPlaying Current => NowPlaying.Nothing;

        public Task PlayPauseAsync() => Add("playPause");

        public Task NextAsync() => Add("next");

        public Task PreviousAsync() => Add("previous");

        private Task Add(string action)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVolume : IVolumeControl
    {
        public bool HasDevice { get; set; } = true;

        public int Percent { get; set; }

        public bool Muted { get; set; }

        public int VolumePercent => Percent;

        public bool IsMuted => Muted;

        public void SetVolumePercent(int percent) => Percent = percent;

        public void SetMuted(bool muted) => Muted = muted;
    }

    private sealed class FakeAlerts : IAlertSink
    {
        public List<Alert> Shown { get; } = [];

        public void Show(Alert alert) => Shown.Add(alert);

        public void Dismiss(string key)
        {
        }
    }
}
