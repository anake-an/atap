using System.Text;
using System.Text.Json;
using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Malaysia;
using Atap.Core.Net;
using Atap.Core.Tests.Malaysia;
using Atap.Core.Widgets;
using Atap.Features.Malaysia;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Hijri;
using Atap.Widgets.WaktuSolat;

namespace Atap.Tests.Malaysia;

/// <summary>The widget on fake time (it starts at 17:00 in Malaysia on 25 October 2026) and a fake e-Solat.</summary>
public sealed class WaktuSolatWidgetTests : IDisposable
{
    private static readonly TimeSpan Myt = TimeSpan.FromHours(8);

    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new(MalaysiaKeys.WaktuSolatTypeId);
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly FakeEsolat _esolat = new();

    public WaktuSolatWidgetTests()
    {
        _services.Settings.Register(new MalaysiaDescriptor().Settings);
        _esolat.Months[10] = EsolatFixtures.Sgr01October2026;
        _esolat.Months[11] = EsolatFixtures.Sgr01November2026;
    }

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task NoArea_AsksForOne_AndFetchesNothing()
    {
        var (widget, viewModel) = await StartAsync(zone: null);

        Assert.False(viewModel.HasTimes);
        Assert.Equal("Pick your area on the Prayer times page in Settings.", viewModel.Message);
        Assert.False(widget.IsLive);
        Assert.Equal(0, _esolat.Requests);
    }

    [Fact]
    public async Task WithArea_ShowsTheNextPrayer_FromTwoMonthsOfData()
    {
        var (widget, viewModel) = await StartAsync();

        Assert.True(viewModel.HasTimes);
        Assert.Equal("Maghrib", viewModel.ShortName);
        Assert.Equal("in 1 h 59 min", viewModel.Countdown);
        Assert.Equal(7, viewModel.Rows.Count);
        Assert.True(viewModel.Rows.Single(r => r.Name == "Maghrib").IsNext);
        Assert.False(widget.IsLive);
        // 25 October: this month and next, once each.
        Assert.Equal(2, _esolat.Requests);
    }

    [Fact]
    public async Task GoesLive_TenMinutesBefore_AndAlertsOnceAtThePrayer()
    {
        var (widget, viewModel) = await StartAsync();

        AdvanceTo(18, 48);
        Assert.False(widget.IsLive);
        AdvanceTo(18, 49);
        Assert.True(widget.IsLive);
        Assert.Null(_services.Alerts.Current);

        AdvanceTo(18, 59);
        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Time for Maghrib", alert.Alert.Title);
        Assert.Equal(AlertPriority.High, alert.Alert.Priority);
        Assert.Equal("Maghrib now", viewModel.Headline);
        Assert.Equal("now", viewModel.Short);

        _services.Alerts.DismissCurrent();
        AdvanceTo(19, 0);
        Assert.Null(_services.Alerts.Current);
        Assert.True(widget.IsLive);
        AdvanceTo(19, 4);
        Assert.False(widget.IsLive);
    }

    [Fact]
    public async Task OutOfDateData_SaysSo_AndNeverAlerts()
    {
        _esolat.Months.Clear();
        var (widget, viewModel) = await StartAsync();

        Assert.Equal("Prayer times are out of date. Atap will try again soon.", viewModel.Message);
        AdvanceTo(19, 5);
        Assert.Null(_services.Alerts.Current);
        Assert.False(widget.IsLive);
    }

    [Fact]
    public async Task Hidden_HasOneWake_Shown_AddsTheMinuteTick()
    {
        await StartAsync();
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        _visibility.Value = WidgetVisibility.Compact;
        Assert.Equal(2, _services.Scheduler.Stats.ArmedItems);

        _visibility.Value = WidgetVisibility.Hidden;
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Stop_LeavesNothingArmed()
    {
        var (widget, _) = await StartAsync();
        _visibility.Value = WidgetVisibility.Expanded;

        await widget.StopAsync();

        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        Assert.False(widget.IsLive);
    }

    [Fact]
    public async Task Hijri_UsesTheOfficialDate_FromTheSameData()
    {
        _services.Settings.Set(MalaysiaKeys.Zone, "SGR01");
        var descriptor = new HijriDescriptor();
        var widget = descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility, _esolat));
        await widget.StartAsync(SettingValues.Empty);
        var viewModel = Assert.IsType<HijriViewModel>(widget.ViewModel);

        Assert.Equal("14 Jamadilawal 1448", viewModel.DateText);
        Assert.False(viewModel.IsEstimate);
    }

    [Fact]
    public async Task Hijri_WithoutAnArea_IsCalculated_WithTheOffset()
    {
        var descriptor = new HijriDescriptor();
        var widget = descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility, _esolat));
        await widget.StartAsync(SettingValues.Empty);
        var viewModel = Assert.IsType<HijriViewModel>(widget.ViewModel);
        var plain = viewModel.DateText;

        _services.Settings.Set(MalaysiaKeys.HijriOffset, 1);

        Assert.True(viewModel.IsEstimate);
        Assert.Equal(HijriDate.FromGregorian(new DateOnly(2026, 10, 25), 1).Format("en"), viewModel.DateText);
        Assert.NotEqual(plain, viewModel.DateText);
        Assert.Equal(0, _esolat.Requests);
    }

    [Fact]
    public async Task AreaChangedWhileLoading_LoadsTheNewArea()
    {
        var answer = new TaskCompletionSource();
        _esolat.Gate = answer.Task;
        var (_, viewModel) = await StartAsync("WLY01");

        _services.Settings.Set(MalaysiaKeys.Zone, "SGR01");
        answer.SetResult();

        await WaitUntil(() => viewModel.HasTimes);
        Assert.Equal("SGR01", _esolat.Zones[^1]);
    }

    [Fact]
    public async Task Hijri_AreaChangedWhileLoading_LoadsTheNewArea()
    {
        var answer = new TaskCompletionSource();
        _esolat.Gate = answer.Task;
        _services.Settings.Set(MalaysiaKeys.Zone, "WLY01");
        var descriptor = new HijriDescriptor();
        var widget = descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility, _esolat));
        await widget.StartAsync(SettingValues.Empty);
        var viewModel = Assert.IsType<HijriViewModel>(widget.ViewModel);

        _services.Settings.Set(MalaysiaKeys.Zone, "SGR01");
        answer.SetResult();

        await WaitUntil(() => !viewModel.IsEstimate);
        Assert.Equal("SGR01", _esolat.Zones[^1]);
    }

    [Fact]
    public async Task NotchOnly_AlertsWithoutASound()
    {
        await StartAsync();

        AdvanceTo(18, 59);

        Assert.Null(Assert.IsType<ShownAlert>(_services.Alerts.Current).Alert.SoundKey);
    }

    [Fact]
    public async Task SoftChime_PlaysTheChimeThatComesWithWindows()
    {
        await StartAsync(settings: new() { [WaktuSolatDescriptor.AtPrayerKey] = WaktuSolatDescriptor.AtPrayerChime });

        AdvanceTo(18, 59);

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current).Alert;
        Assert.Equal(WaktuSolatWidget.ChimeSound(), alert.SoundKey);
        Assert.Equal(AlertPriority.High, alert.Priority);
    }

    [Fact]
    public async Task Azan_PlaysThePickedFile()
    {
        await StartAsync(settings: new()
        {
            [WaktuSolatDescriptor.AtPrayerKey] = WaktuSolatDescriptor.AtPrayerAzan,
            [WaktuSolatDescriptor.AzanFileKey] = @"C:\Sounds\azan.mp3",
        });

        AdvanceTo(18, 59);

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current).Alert;
        Assert.Equal(AlertSounds.ForFile(@"C:\Sounds\azan.mp3"), alert.SoundKey);
        Assert.Equal(AlertSoundKind.File, AlertSounds.Resolve(alert.SoundKey, null)?.Kind);
        // High, not Critical, so Do not disturb and full screen still keep it quiet.
        Assert.Equal(AlertPriority.High, alert.Priority);
    }

    [Fact]
    public async Task NoAlert_ShowsNothingAtThePrayer()
    {
        await StartAsync(settings: new() { [WaktuSolatDescriptor.AtPrayerKey] = WaktuSolatDescriptor.AtPrayerOff });

        AdvanceTo(19, 0);

        Assert.Null(_services.Alerts.Current);
    }

    [Fact]
    public async Task OldAlertTurnedOff_StaysOff()
    {
        await StartAsync(settings: new() { [WaktuSolatDescriptor.OldReminderKey] = false });

        AdvanceTo(19, 0);

        Assert.Null(_services.Alerts.Current);
    }

    [Theory]
    [InlineData(null, null, WaktuSolatDescriptor.AtPrayerNotch)]
    [InlineData(true, null, WaktuSolatDescriptor.AtPrayerNotch)]
    [InlineData(false, null, WaktuSolatDescriptor.AtPrayerOff)]
    [InlineData(false, WaktuSolatDescriptor.AtPrayerAzan, WaktuSolatDescriptor.AtPrayerAzan)]
    [InlineData(null, WaktuSolatDescriptor.AtPrayerChime, WaktuSolatDescriptor.AtPrayerChime)]
    [InlineData(null, "loud", WaktuSolatDescriptor.AtPrayerNotch)]
    public void AtPrayer_ReadsTheChoice_OrTheOldOnOrOff(bool? reminder, string? atPrayer, string expected)
    {
        var values = new Dictionary<string, object>();
        if (reminder is { } on)
            values[WaktuSolatDescriptor.OldReminderKey] = on;
        if (atPrayer is not null)
            values[WaktuSolatDescriptor.AtPrayerKey] = atPrayer;

        Assert.Equal(expected, WaktuSolatWidget.AtPrayerOf(Settings(values)));
    }

    [Theory]
    [InlineData(WaktuSolatDescriptor.AtPrayerNotch, @"C:\Sounds\azan.mp3", null)]
    [InlineData(WaktuSolatDescriptor.AtPrayerOff, @"C:\Sounds\azan.mp3", null)]
    [InlineData(WaktuSolatDescriptor.AtPrayerChime, @"C:\Sounds\azan.mp3", "chime")]
    [InlineData(WaktuSolatDescriptor.AtPrayerAzan, @"C:\Sounds\azan.mp3", @"file:C:\Sounds\azan.mp3")]
    [InlineData(WaktuSolatDescriptor.AtPrayerAzan, "", "chime")]
    [InlineData(WaktuSolatDescriptor.AtPrayerAzan, @"\\server\share\azan.mp3", "chime")]
    [InlineData(WaktuSolatDescriptor.AtPrayerAzan, @"C:\Sounds\azan.exe", "chime")]
    public void Sound_AzanFallsBackToTheChime_UntilAFileIsPicked(string atPrayer, string azanFile, string? expected)
    {
        Assert.Equal(expected, WaktuSolatWidget.SoundFor(atPrayer, azanFile, () => "chime"));
    }

    [Fact]
    public void TheChime_IsAWindowsSound_NeverOneAtapShips()
    {
        var chime = WaktuSolatWidget.ChimeSound();

        Assert.True(chime == AlertSounds.Windows || chime.EndsWith(@"\Media\chimes.wav", StringComparison.OrdinalIgnoreCase), chime);
    }

    [Fact]
    public void AzanFile_ShowsOnlyForTheAzan_AndStaysOnThisPc()
    {
        var file = new WaktuSolatDescriptor().Settings.Single(s => s.Key == WaktuSolatDescriptor.AzanFileKey);

        Assert.Equal(SettingType.File, file.Type);
        Assert.Equal((WaktuSolatDescriptor.AtPrayerKey, (object)WaktuSolatDescriptor.AtPrayerAzan), file.VisibleWhen);
        Assert.Equal(SettingScope.Machine, file.Scope);
    }

    private static SettingValues Settings(Dictionary<string, object> values) =>
        new(new WaktuSolatDescriptor().Settings, values.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)));

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private async Task<(WaktuSolatWidget Widget, WaktuSolatViewModel ViewModel)> StartAsync(
        string? zone = "SGR01",
        Dictionary<string, object>? settings = null)
    {
        if (zone is not null)
            _services.Settings.Set(MalaysiaKeys.Zone, zone);
        var descriptor = new WaktuSolatDescriptor();
        var widget = Assert.IsType<WaktuSolatWidget>(
            descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility, _esolat)));
        await widget.StartAsync(settings is null ? new SettingValues(descriptor.Settings, null) : Settings(settings));
        return (widget, Assert.IsType<WaktuSolatViewModel>(widget.ViewModel));
    }

    /// <summary>Moves the clock a minute at a time, so every wake on the way runs, as on a real PC.</summary>
    private void AdvanceTo(int hour, int minute)
    {
        var target = new DateTimeOffset(2026, 10, 25, hour, minute, 0, Myt);
        while (_services.Time.GetUtcNow() < target)
        {
            var step = target - _services.Time.GetUtcNow();
            _services.Time.Advance(step < TimeSpan.FromMinutes(1) ? step : TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>e-Solat answering from the recorded months. The mirror and anything else fail.</summary>
    private sealed class FakeEsolat : IHttpDataService
    {
        public Dictionary<int, string> Months { get; } = [];

        public int Requests { get; private set; }

        /// <summary>The zones asked for, in order.</summary>
        public List<string> Zones { get; } = [];

        /// <summary>Answers wait for this, so a test can change things while a load is out.</summary>
        public Task Gate { get; set; } = Task.CompletedTask;

        public async Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Requests++;
            var query = request.Url.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToList();
            if (query.FirstOrDefault(p => p[0] == "zone")?[1] is { } zone)
                Zones.Add(zone);
            await Gate;
            var month = query.FirstOrDefault(p => p[0] == "month")?[1];
            if (request.Url.Host != MalaysiaKeys.EsolatHost || month is null || !Months.TryGetValue(int.Parse(month, System.Globalization.CultureInfo.InvariantCulture), out var body))
                return FetchResult.Fail(FetchStatus.Failed, FetchError.Network);
            return new FetchResult(FetchStatus.Fresh, Encoding.UTF8.GetBytes(body), DateTimeOffset.UtcNow, FetchError.None);
        }
    }
}
