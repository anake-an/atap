using Atap.Composition;
using Atap.Core.Layout;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Scheduling;

/// <summary>
/// The idle budget: with the default layout Atap wakes at most once a minute. Measured with the real widgets
/// from the app's catalog, on fake time, so a widget that adds a timer or a faster tick fails here.
/// </summary>
public sealed class DefaultLayoutWakeTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task DefaultLayout_WakesAtMostOnceAMinute_AndLeavesNothingOnceRemoved()
    {
        var enabled = DefaultLayout.Create().EnabledWidgets;
        await using var host = _services.CreateWidgetHost(Discovery.Widgets(typeof(App).Assembly));
        await host.ApplyAsync(enabled);
        // A Windows source may never answer on a build server. After 30 real seconds the host's own start
        // limit, which runs on fake time, is passed, so the start gives up and disposing cannot hang.
        var start = host.StartAsync();
        if (await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(30))) != start)
            _services.Time.Advance(WidgetHost.StartTimeout);
        await start;
        Assert.Equal(enabled, host.InstanceIds);
        var before = _services.Scheduler.Stats.WakesLastHour;

        for (var second = 0; second < 600; second++)
            _services.Time.Advance(TimeSpan.FromSeconds(1));

        // The clock ticks once a minute. Nothing else in the default layout may add a wake.
        Assert.Equal(10, _services.Scheduler.Stats.WakesLastHour - before);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        await host.ApplyAsync([]);
        // Music's shared media source waits out its grace period, then nothing is left.
        _services.Time.Advance(Atap.Core.Shared.SharedServices.GracePeriod + TimeSpan.FromSeconds(1));
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        Assert.Equal(0, _services.Shared.ActiveCount);
    }
}
