using Atap.Core.Alerts;
using Atap.Core.Notch;
using Atap.Features.Alerts;
using Atap.Notch;
using Atap.Notch.Surfaces.Alerts;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Alerts;

/// <summary>
/// The alerts feature on a fake clock, with fake sounds and fake Windows quiet hours. Settings live in a
/// temp folder, never in the user's own.
/// </summary>
public sealed class AlertsFeatureTests : IAsyncLifetime
{
    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly FakeForeground _foreground = new();
    private readonly FakeSoundOutput _output = new();
    private readonly FakeQuietHours _quietHours = new();
    private readonly List<string> _log = [];
    private readonly AlertQueue _queue;
    private readonly AlertsFeature _feature;

    public AlertsFeatureTests()
    {
        _services.Settings.Load();
        _services.Settings.Register(new AlertsFeatureDescriptor().Settings);
        _queue = new AlertQueue(_services.Time, ui: null);
        _feature = new AlertsFeature(
            _queue, _notch, _foreground, _services.Settings, _services.Text, _services.Commands,
            new InlineContext(), _output, _quietHours, (message, _) => _log.Add(message));
    }

    public Task InitializeAsync() => _feature.StartAsync();

    public async Task DisposeAsync()
    {
        await _feature.DisposeAsync();
        _services.Dispose();
    }

    private AlertViewModel? Shown => _notch.GetSurfaceContent(NotchSurface.Alert) as AlertViewModel;

    private void Advance(double seconds) => _services.Time.Advance(TimeSpan.FromSeconds(seconds));

    private void Show(string source, Alert alert) => _queue.For(source).Show(alert);

    [Fact]
    public void AnAlert_ShowsOnTheNotchThenTheNotchGoesBack()
    {
        Show("battery", new Alert("full", AlertPriority.Normal, "Battery full") { Subtitle = "You can unplug now" });

        Assert.Equal("Battery full", Shown!.Title);
        Assert.Equal("You can unplug now", Shown.Subtitle);

        Advance(4);
        Assert.Null(Shown);
    }

    [Fact]
    public void TheNextAlert_GetsItsOwnViewModel()
    {
        Show("a", new Alert("1", AlertPriority.Normal, "First"));
        var first = Shown;
        Show("a", new Alert("2", AlertPriority.Normal, "Second"));
        Assert.Same(first, Shown);

        Advance(4);
        Assert.NotSame(first, Shown);
        Assert.Equal("Second", Shown!.Title);
    }

    [Fact]
    public void FullScreen_SendsNoticesToMissed_AndAlarmsWaitWithTheirSoundOnTime()
    {
        _foreground.FullScreen = FullScreenKind.Game;
        Assert.True(_queue.IsHidden);

        Show("weather", new Alert("rain", AlertPriority.High, "Heavy rain soon") { SoundKey = AlertSounds.Default });
        Show("alarm", new Alert("ring", AlertPriority.Critical, "Alarm 07:00") { SoundKey = AlertSounds.ForFile(@"C:\Sounds\alarm.mp3") });

        Assert.Null(Shown);
        Assert.Single(_queue.Missed);
        Assert.Equal(0, _output.Notifications);
        var alarm = Assert.Single(_output.Started);
        Assert.Equal(@"C:\Sounds\alarm.mp3", alarm.Sound.Value);

        _foreground.FullScreen = FullScreenKind.None;
        Assert.Equal("Alarm 07:00", Shown!.Title);
        Assert.True(Shown.CanStop);
        Assert.Single(_output.Started);
    }

    [Fact]
    public void TheHideFlag_HidesAlertsToo()
    {
        _notch.SetFlag(NotchFlag.Hide, "rules", true);

        Show("a", new Alert("n", AlertPriority.Normal, "Notice"));

        Assert.Null(Shown);
        Assert.Single(_queue.Missed);
        _notch.SetFlag(NotchFlag.Hide, "rules", false);
        Assert.False(_queue.IsHidden);
    }

    [Theory]
    [InlineData(AlertsFeatureDescriptor.SoundWindows, 1, 0)]
    [InlineData(AlertsFeatureDescriptor.SoundOff, 0, 0)]
    [InlineData(AlertsFeatureDescriptor.SoundFile, 0, 1)]
    public void TheDefaultSound_FollowsTheSetting(string choice, int notifications, int files)
    {
        _services.Settings.Set(AlertsFeatureDescriptor.SoundKey, choice);
        _services.Settings.Set(AlertsFeatureDescriptor.SoundFileKey, @"C:\Sounds\chime.wav");

        Show("todo", new Alert("due", AlertPriority.High, "Hand in the report") { SoundKey = AlertSounds.Default });

        Assert.Equal(notifications, _output.Notifications);
        Assert.Equal(files, _output.Started.Count);
    }

    [Fact]
    public void OwnFileWithoutAUsableFile_PlaysTheWindowsSound()
    {
        _services.Settings.Set(AlertsFeatureDescriptor.SoundKey, AlertsFeatureDescriptor.SoundFile);
        _services.Settings.Set(AlertsFeatureDescriptor.SoundFileKey, @"\\nas\share\chime.wav");

        Show("todo", new Alert("due", AlertPriority.High, "Hand in the report") { SoundKey = AlertSounds.Default });

        Assert.Equal(1, _output.Notifications);
        Assert.Empty(_output.Started);
    }

    [Fact]
    public void WhileItsSoundPlays_TheAlertStaysWithAStopButton()
    {
        Show("waktu-solat", new Alert("zohor", AlertPriority.High, "Zohor") { SoundKey = AlertSounds.ForBundled("azan.ogg") });
        Assert.True(Shown!.CanStop);

        Advance(180);
        Assert.Equal("Zohor", Shown!.Title);

        _output.Started[0].Finish();
        Assert.Null(Shown);
    }

    [Fact]
    public void Stop_EndsTheSoundAndClosesOnlyThatAlert()
    {
        Show("waktu-solat", new Alert("zohor", AlertPriority.High, "Zohor") { SoundKey = AlertSounds.ForBundled("azan.ogg") });
        Show("todo", new Alert("next", AlertPriority.Normal, "Next up"));
        Advance(30);

        Shown!.StopCommand.Execute(null);

        Assert.True(_output.Started[0].Stopped);
        Assert.Equal("Next up", Shown!.Title);
        Assert.False(Shown.CanStop);
    }

    [Fact]
    public void ACriticalAlert_HasCloseUntilItIsClosed()
    {
        Show("timer", new Alert("done", AlertPriority.Critical, "Timer done"));
        Assert.True(Shown!.ShowClose);

        Shown.CloseCommand.Execute(null);

        Assert.Null(Shown);
    }

    [Fact]
    public void Dismissing_FromTheSource_EndsItsSound()
    {
        Show("alarm", new Alert("ring", AlertPriority.Critical, "Alarm") { SoundKey = AlertSounds.ForFile(@"C:\Sounds\alarm.mp3") });

        _queue.For("alarm").Dismiss("ring");

        Assert.True(_output.Started[0].Stopped);
        Assert.Null(Shown);
    }

    [Fact]
    public void AClick_ClosesTheAlert_ButNeverCutsASound()
    {
        Show("a", new Alert("n", AlertPriority.Normal, "Notice"));
        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Click, NotchSurface.Alert)));
        Assert.Null(Shown);

        Show("waktu-solat", new Alert("zohor", AlertPriority.High, "Zohor") { SoundKey = AlertSounds.ForBundled("azan.ogg") });
        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Click, NotchSurface.Alert)));
        Assert.NotNull(Shown);
        Assert.False(_output.Started[0].Stopped);

        // Clicks on other surfaces are not the alert's.
        Assert.False(_notch.Offer(new NotchInput(NotchInputKind.Click, NotchSurface.Compact)));
    }

    [Fact]
    public void WindowsQuietHours_AreReadOnlyWhenAnAlertIsAboutToShow()
    {
        Advance(600);
        Assert.Equal(0, _quietHours.Asked);

        _quietHours.On = true;
        Show("a", new Alert("n", AlertPriority.Normal, "Notice") { SoundKey = AlertSounds.Windows });

        Assert.Equal(1, _quietHours.Asked);
        Assert.Null(Shown);
        Assert.Single(_queue.Missed);
        Assert.Equal(0, _output.Notifications);
    }

    [Fact]
    public void WindowsQuietHours_CanBeIgnored()
    {
        _services.Settings.Set(AlertsFeatureDescriptor.FollowWindowsKey, false);
        _quietHours.On = true;

        Show("a", new Alert("n", AlertPriority.Normal, "Notice"));

        Assert.Equal(0, _quietHours.Asked);
        Assert.NotNull(Shown);
    }

    [Fact]
    public async Task Commands_StopTheSoundAndClearMissed()
    {
        _queue.SetQuiet("rule", true);
        Show("a", new Alert("n", AlertPriority.Normal, "Notice"));
        Show("alarm", new Alert("ring", AlertPriority.Critical, "Alarm") { SoundKey = AlertSounds.ForFile(@"C:\Sounds\alarm.mp3") });

        Assert.True(await _services.Commands.TryRunAsync(AlertsFeature.StopSoundCommand));
        Assert.True(_output.Started[0].Stopped);
        Assert.False(await _services.Commands.TryRunAsync(AlertsFeature.StopSoundCommand));

        Assert.True(await _services.Commands.TryRunAsync(AlertsFeature.ClearMissedCommand));
        Assert.Empty(_queue.Missed);
    }

    [Fact]
    public async Task TurningItOff_ReturnsEverythingToTheBaseline()
    {
        _foreground.FullScreen = FullScreenKind.FullScreen;
        _foreground.FullScreen = FullScreenKind.None;
        Show("waktu-solat", new Alert("zohor", AlertPriority.High, "Zohor") { SoundKey = AlertSounds.ForBundled("azan.ogg") });
        _foreground.FullScreen = FullScreenKind.Game;

        await _feature.DisposeAsync();

        Assert.Null(_notch.GetSurfaceContent(NotchSurface.Alert));
        Assert.Equal(0, _notch.InputHandlerCount);
        Assert.False(_services.Commands.Contains(AlertsFeature.StopSoundCommand));
        Assert.False(_services.Commands.Contains(AlertsFeature.ClearMissedCommand));
        Assert.Null(_queue.SystemQuiet);
        Assert.False(_queue.IsHidden);
        Assert.All(_output.Started, s => Assert.True(s.HasEnded));

        // No longer following the notch.
        _foreground.FullScreen = FullScreenKind.None;
        _foreground.FullScreen = FullScreenKind.Game;
        Assert.False(_queue.IsHidden);
    }

    [Fact]
    public void Descriptor_IsAlwaysOnAndKeepsTheFilePathOnThisPc()
    {
        var descriptor = new AlertsFeatureDescriptor();

        Assert.True(descriptor.IsEnabled(Atap.Core.Widgets.SettingValues.Empty, Atap.Composition.AppMode.Safe));
        var file = Assert.Single(descriptor.Settings, s => s.Key == AlertsFeatureDescriptor.SoundFileKey);
        Assert.Equal(Atap.Core.Widgets.SettingScope.Machine, file.Scope);
        Assert.All(descriptor.Settings, s => Assert.StartsWith("alerts.", s.Key, StringComparison.Ordinal));
    }
}
