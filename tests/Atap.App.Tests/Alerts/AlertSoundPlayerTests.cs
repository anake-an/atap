using Atap.Core.Alerts;
using Atap.Features.Alerts;

namespace Atap.Tests.Alerts;

/// <summary>The playback service with a fake output. Nothing here makes a real sound.</summary>
public class AlertSoundPlayerTests
{
    private static readonly AlertSound Azan = new(AlertSoundKind.Bundled, "azan.ogg");
    private static readonly AlertSound Chime = new(AlertSoundKind.File, @"C:\Sounds\chime.wav");
    private static readonly AlertSound Windows = new(AlertSoundKind.Windows, string.Empty);

    private readonly FakeSoundOutput _output = new();
    private readonly List<string> _log = [];
    private bool _quiet;
    private bool _hidden;

    private AlertSoundPlayer CreatePlayer() =>
        new(_output, new InlineContext(), () => _quiet, () => _hidden, (message, _) => _log.Add(message));

    [Fact]
    public void ASound_PlaysOnceAndIsNeverStartedAgain()
    {
        var player = CreatePlayer();
        var ended = new List<PlayingSound>();
        player.Ended += (_, sound) => ended.Add(sound);

        var playing = player.Play(Azan, AlertPriority.High);
        Assert.NotNull(playing);
        Assert.True(player.IsPlaying);

        _output.Started[0].Finish();

        Assert.False(player.IsPlaying);
        Assert.Single(_output.Started);
        Assert.Same(playing, Assert.Single(ended));
        Assert.True(playing!.IsEnded);
    }

    [Fact]
    public void APlayingSound_IsNeverCutByANewOne()
    {
        var player = CreatePlayer();
        player.Play(Azan, AlertPriority.High);

        Assert.Null(player.Play(Chime, AlertPriority.High));
        Assert.Null(player.Play(Windows, AlertPriority.Normal));

        Assert.Single(_output.Started);
        Assert.False(_output.Started[0].Stopped);
        Assert.Equal(0, _output.Notifications);
    }

    [Fact]
    public void ACriticalSound_PlaysAlongside()
    {
        var player = CreatePlayer();
        player.Play(Azan, AlertPriority.High);

        Assert.NotNull(player.Play(Chime, AlertPriority.Critical));

        Assert.Equal(2, _output.Playing.Count());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void QuietOrFullScreen_SkipsAllButCritical(bool quiet, bool hidden)
    {
        _quiet = quiet;
        _hidden = hidden;
        var player = CreatePlayer();

        Assert.Null(player.Play(Azan, AlertPriority.High));
        Assert.Null(player.Play(Windows, AlertPriority.Normal));
        Assert.Empty(_output.Started);
        Assert.Equal(0, _output.Notifications);

        // An alarm rings on time, even in a game.
        Assert.NotNull(player.Play(Chime, AlertPriority.Critical));
    }

    [Fact]
    public void TheWindowsSound_PlaysButIsNotFollowed()
    {
        var player = CreatePlayer();

        Assert.Null(player.Play(Windows, AlertPriority.Normal));

        Assert.Equal(1, _output.Notifications);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void Stop_EndsEverySoundAtOnce()
    {
        var player = CreatePlayer();
        var changes = 0;
        var ended = 0;
        player.PlayingChanged += (_, _) => changes++;
        player.Ended += (_, _) => ended++;
        player.Play(Azan, AlertPriority.High);
        player.Play(Chime, AlertPriority.Critical);

        player.Stop();

        Assert.False(player.IsPlaying);
        Assert.All(_output.Started, s => Assert.True(s.Stopped));
        Assert.Equal(1, _output.NotificationStops);
        Assert.Equal(2, ended);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void StopFor_StopsOnlyThatAlertsSound()
    {
        var player = CreatePlayer();
        var prayer = new ShownAlert("waktu-solat", new Alert("zohor", AlertPriority.High, "Zohor"), DateTimeOffset.UnixEpoch);
        var alarm = new ShownAlert("alarm", new Alert("ring", AlertPriority.Critical, "Alarm"), DateTimeOffset.UnixEpoch);
        player.Play(Azan, AlertPriority.High, prayer);
        player.Play(Chime, AlertPriority.Critical, alarm);

        player.StopFor("alarm", "ring");

        Assert.False(_output.Started[0].Stopped);
        Assert.True(_output.Started[1].Stopped);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public void ASoundThatCannotPlay_FallsBackToTheWindowsSound()
    {
        _output.FailNext = true;
        var player = CreatePlayer();

        Assert.Null(player.Play(Chime, AlertPriority.High));

        Assert.False(player.IsPlaying);
        Assert.Equal(1, _output.Notifications);
        Assert.Single(_log);
    }

    [Fact]
    public void AfterDispose_NothingPlays()
    {
        var player = CreatePlayer();
        player.Play(Azan, AlertPriority.High);

        player.Dispose();

        Assert.True(_output.Started[0].Stopped);
        Assert.Null(player.Play(Chime, AlertPriority.Critical));
        Assert.Single(_output.Started);
    }
}
