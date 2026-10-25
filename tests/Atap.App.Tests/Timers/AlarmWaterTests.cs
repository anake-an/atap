using System.Globalization;
using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Widgets;
using Atap.Platform.Session;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Timers.Alarm;
using Atap.Widgets.Timers.Water;

namespace Atap.Tests.Timers;

/// <summary>Alarms and water reminders on fake time, starting 25 October 2026 at 09:00, with UTC as the local zone.</summary>
public sealed class AlarmWaterTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly List<OwnerLifetime> _lifetimes = [];
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly FakeSession _session = new();

    public AlarmWaterTests() => _services.Time.SetLocalTimeZone(TimeZoneInfo.Utc);

    public void Dispose()
    {
        foreach (var lifetime in _lifetimes)
            lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void Alarm_RingsWithoutAPlacement()
    {
        Assert.True(new AlarmDescriptor().WorksWithoutPlacement);
        Assert.True(new WaterDescriptor().WorksWithoutPlacement);
    }

    [Fact]
    public async Task Alarm_SetInTheEditor_RingsOnce_ThenTurnsOff()
    {
        var (widget, alarms) = await StartAlarmAsync();
        AddAlarm(alarms, hourSteps: 2, minuteSteps: 2);

        var row = Assert.Single(alarms.Rows);
        Assert.Equal(Time(9, 10), row.Time);
        Assert.Equal("Once", row.Detail);
        Assert.Equal("in 10 min", alarms.NextText);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        Advance(TimeSpan.FromMinutes(10));

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Alarm", alert.Alert.Title);
        Assert.Equal(AlertPriority.Critical, alert.Alert.Priority);
        Assert.Equal(AlertSounds.Default, alert.Alert.SoundKey);
        Assert.False(Assert.Single(alarms.Rows).IsOn);
        Assert.True(alarms.CanSnooze);
        await widget.StopAsync();
    }

    [Fact]
    public async Task Alarm_TimeLeft_CountsDownEachMinute_OnlyWhileShown()
    {
        var (widget, alarms) = await StartAlarmAsync();
        AddAlarm(alarms, hourSteps: 2, minuteSteps: 2);
        Assert.Equal("in 10 min", alarms.NextText);

        _visibility.Value = WidgetVisibility.Expanded;
        Advance(TimeSpan.FromMinutes(3));
        Assert.Equal("in 7 min", alarms.NextText);

        _visibility.Value = WidgetVisibility.Hidden;
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
        await widget.StopAsync();
    }

    [Fact]
    public async Task Alarm_Snooze_RingsAgainFiveMinutesLater()
    {
        var (_, alarms) = await StartAlarmAsync();
        AddAlarm(alarms, hourSteps: 2, minuteSteps: 2);
        Advance(TimeSpan.FromMinutes(10));

        alarms.SnoozeCommand.Execute(null);

        Assert.Null(_services.Alerts.Current);
        Assert.False(alarms.CanSnooze);
        Advance(TimeSpan.FromMinutes(5));
        Assert.Equal("Alarm", Assert.IsType<ShownAlert>(_services.Alerts.Current).Alert.Title);
    }

    [Fact]
    public async Task Alarm_MissedWhileClosed_AlertsOnce()
    {
        var (first, alarms) = await StartAlarmAsync();
        AddAlarm(alarms, hourSteps: 2, minuteSteps: 2, everyDay: true);
        await first.StopAsync();
        await first.DisposeAsync();

        Advance(TimeSpan.FromHours(1));
        var (second, _) = await StartAlarmAsync();

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal($"Missed alarm {Time(9, 10)}", alert.Alert.Title);
        Assert.Equal(AlertPriority.Normal, alert.Alert.Priority);
        _services.Alerts.DismissCurrent();
        await second.StopAsync();
        await second.DisposeAsync();

        var (_, again) = await StartAlarmAsync();
        Assert.Null(_services.Alerts.Current);
        Assert.True(Assert.Single(again.Rows).IsOn);
    }

    [Fact]
    public async Task Alarm_SwitchedOff_HasNoWake()
    {
        var (_, alarms) = await StartAlarmAsync();
        AddAlarm(alarms, hourSteps: 2, minuteSteps: 2, everyDay: true);

        alarms.Rows[0].IsOn = false;

        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        Assert.Equal("No alarms on", alarms.NextText);
    }

    [Fact]
    public async Task Alarm_EditorMinutes_StepInFivesWithinTheHour()
    {
        var (_, alarms) = await StartAlarmAsync();
        alarms.AddCommand.Execute(null);
        Assert.Equal(Time(7, 0), alarms.EditTimeText);

        alarms.MinuteDownCommand.Execute(null);
        Assert.Equal(Time(7, 55), alarms.EditTimeText);
        alarms.HourDownCommand.Execute(null);
        Assert.Equal(Time(6, 55), alarms.EditTimeText);
        alarms.MinuteUpCommand.Execute(null);
        Assert.Equal(Time(6, 0), alarms.EditTimeText);

        alarms.CancelEditCommand.Execute(null);
        Assert.Empty(alarms.Rows);
    }

    [Fact]
    public async Task Water_RemindsAfterTheInterval_AndADrinkStartsItAgain()
    {
        var (_, water) = await StartWaterAsync();
        Assert.Equal("0 of 8", water.CountText);
        Assert.Equal($"Next reminder at {Time(10, 0)}", water.NextText);

        Advance(TimeSpan.FromMinutes(30));
        water.DrinkCommand.Execute(null);
        Assert.Equal("1 of 8", water.CountText);
        Assert.Equal($"Next reminder at {Time(10, 30)}", water.NextText);

        Advance(TimeSpan.FromMinutes(61));
        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Time for some water", alert.Alert.Title);
        Assert.Equal("1 of 8 glasses today", alert.Alert.Subtitle);
    }

    [Fact]
    public async Task Water_WaitsWhileLocked_AndStartsAgainOnUnlock()
    {
        var (_, water) = await StartWaterAsync();

        _session.Set(locked: true);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        Assert.Equal("Paused while the PC is locked", water.NextText);

        Advance(TimeSpan.FromHours(3));
        Assert.Null(_services.Alerts.Current);

        _session.Set(locked: false);
        Assert.Equal($"Next reminder at {Time(13, 0)}", water.NextText);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
    }

    private static string Time(int hour, int minute) => new TimeOnly(hour, minute).ToString("t", CultureInfo.CurrentCulture);

    /// <summary>Adds an alarm from 7:00 with the editor's steppers, as a person would click.</summary>
    private static void AddAlarm(AlarmsViewModel alarms, int hourSteps, int minuteSteps, bool everyDay = false)
    {
        alarms.AddCommand.Execute(null);
        for (var i = 0; i < hourSteps; i++)
            alarms.HourUpCommand.Execute(null);
        for (var i = 0; i < minuteSteps; i++)
            alarms.MinuteUpCommand.Execute(null);
        if (everyDay)
        {
            foreach (var day in alarms.Days)
                day.IsOn = true;
        }
        alarms.SaveEditCommand.Execute(null);
    }

    private async Task<(AlarmWidget Widget, AlarmsViewModel ViewModel)> StartAlarmAsync()
    {
        var descriptor = new AlarmDescriptor();
        var widget = Assert.IsType<AlarmWidget>(descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, Lifetime(descriptor), _visibility)));
        await widget.StartAsync(new SettingValues(descriptor.Settings, null));
        return (widget, Assert.IsType<AlarmsViewModel>(widget.ViewModel));
    }

    private async Task<(WaterWidget Widget, WaterViewModel ViewModel)> StartWaterAsync()
    {
        var descriptor = new WaterDescriptor();
        var widget = new WaterWidget(_services.CreateContext(descriptor.TypeId, descriptor, Lifetime(descriptor), _visibility), _session);
        await widget.StartAsync(new SettingValues(descriptor.Settings, null));
        return (widget, Assert.IsType<WaterViewModel>(widget.ViewModel));
    }

    private OwnerLifetime Lifetime(IWidgetDescriptor descriptor)
    {
        var lifetime = new OwnerLifetime(descriptor.TypeId);
        _lifetimes.Add(lifetime);
        return lifetime;
    }

    /// <summary>A minute at a time, so every wake on the way runs.</summary>
    private void Advance(TimeSpan span)
    {
        for (var left = span; left > TimeSpan.Zero; left -= TimeSpan.FromMinutes(1))
            _services.Time.Advance(left < TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1));
    }

    private sealed class FakeSession : ISessionLock
    {
        public bool IsLocked { get; private set; }

        public event EventHandler? Changed;

        public void Set(bool locked)
        {
            IsLocked = locked;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }
}
