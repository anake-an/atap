using System.Globalization;
using Atap.Composition;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Clock;

namespace Atap.Tests.Scheduling;

/// <summary>The clock runs on the shared minute tick, on fake time, with no timer of its own.</summary>
public sealed class ClockWidgetTests : IDisposable
{
    private readonly TestServices _services = new();

    public ClockWidgetTests() => _services.Time.SetLocalTimeZone(TimeZoneInfo.Utc);

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Clock_FollowsTheMinuteTick_WithOneWakeAMinute()
    {
        await using var host = await StartClockAsync();
        var clock = ClockOf(host);
        Assert.Equal(Text(9, 0), clock.Time);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        for (var second = 0; second < 600; second++)
            _services.Time.Advance(TimeSpan.FromSeconds(1));

        // 09:10:00 exactly: ten ticks so far, and the next is due 20 ms later.
        Assert.Equal(Text(9, 9), clock.Time);
        Assert.Equal(10, _services.Scheduler.Stats.WakesLastHour);
        _services.Time.Advance(Scheduler.TickOffset);
        Assert.Equal(Text(9, 10), clock.Time);
    }

    [Fact]
    public async Task Clock_CatchesUpAtOnce_WhenTheTimeZoneOrClockChanges()
    {
        await using var host = await StartClockAsync();
        var clock = ClockOf(host);

        _services.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Atap test", TimeSpan.FromHours(8), "Atap test", "Atap test"));
        _services.Scheduler.NotifyClockChanged();
        Assert.Equal(Text(17, 0), clock.Time);

        // Set 25 minutes forward. Timers do not notice, as on Windows.
        _services.Time.AdjustTime(_services.Time.GetUtcNow().AddMinutes(25));
        _services.Scheduler.NotifyClockChanged();
        Assert.Equal(Text(17, 25), clock.Time);
    }

    [Fact]
    public async Task Clock_CatchesUpAtOnce_AfterSleep()
    {
        await using var host = await StartClockAsync();
        var clock = ClockOf(host);

        _services.Time.AdjustTime(_services.Time.GetUtcNow().AddHours(8).AddMinutes(3));
        _services.Scheduler.NotifyResumed();

        Assert.Equal(Text(17, 3), clock.Time);
    }

    [Fact]
    public async Task Clock_Removed_LeavesNoSchedulerItemsAndNoWakes()
    {
        await using var host = await StartClockAsync();

        await host.ApplyAsync([]);
        _services.Time.Advance(TimeSpan.FromMinutes(1));
        _services.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(new SchedulerStats(0, 0), _services.Scheduler.Stats);
    }

    [Fact]
    public async Task Clock_SplitsHoursAndMinutes_ForTheStackedSideClock()
    {
        await using var host = await StartClockAsync();
        var clock = ClockOf(host);
        var separator = CultureInfo.CurrentCulture.DateTimeFormat.TimeSeparator;

        Assert.Equal(Text(9, 0), clock.Hours + separator + clock.Minutes);
        Assert.Equal("00", clock.Minutes);
    }

    private static string Text(int hour, int minute) =>
        new DateTime(2026, 10, 25, hour, minute, 0).ToString("HH:mm", CultureInfo.CurrentCulture);

    private static ClockViewModel ClockOf(WidgetHost host) => (ClockViewModel)Assert.Single(host.Widgets).ViewModel;

    private async Task<WidgetHost> StartClockAsync()
    {
        var host = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([new ClockDescriptor()]));
        await host.ApplyAsync([ClockDescriptor.TypeIdValue]);
        await host.StartAsync();
        Assert.Equal(WidgetStatus.Running, host.StatusOf(ClockDescriptor.TypeIdValue));
        return host;
    }
}
