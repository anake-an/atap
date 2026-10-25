using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Timers.Focus;
using Atap.Widgets.Timers.Stopwatch;
using Atap.Widgets.Timers.WorldClock;

namespace Atap.Tests.Timers;

/// <summary>The focus timer, stopwatch and world clock on fake time, starting 25 October 2026 at 09:00 UTC.</summary>
public sealed class TimerWidgetTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly List<OwnerLifetime> _lifetimes = [];
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);

    public TimerWidgetTests() => _services.Time.SetLocalTimeZone(TimeZoneInfo.Utc);

    public void Dispose()
    {
        foreach (var lifetime in _lifetimes)
            lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task Focus_Start_IsLive_AndCountsDown()
    {
        var (widget, viewModel) = await StartFocusAsync();
        Assert.True(viewModel.IsIdle);
        Assert.Equal("25:00", viewModel.TimeText);
        Assert.False(widget.IsLive);

        viewModel.ToggleCommand.Execute(null);
        // In the compact notch the text follows the minute tick.
        _visibility.Value = WidgetVisibility.Compact;
        Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        Assert.True(widget.IsLive);
        Assert.True(viewModel.IsRunning);
        Assert.Equal("15 min", viewModel.ShortText);
    }

    [Fact]
    public async Task Focus_AtTheEnd_AlertsOnce_AndWaitsAtTheBreak()
    {
        var (widget, viewModel) = await StartFocusAsync();
        viewModel.ToggleCommand.Execute(null);

        Advance(TimeSpan.FromMinutes(25));

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Focus session done", alert.Alert.Title);
        Assert.Equal("Time for a 5 min break.", alert.Alert.Subtitle);
        Assert.Equal(AlertPriority.Critical, alert.Alert.Priority);
        Assert.Equal("Short break", viewModel.PhaseText);
        Assert.True(viewModel.IsIdle);
        Assert.False(widget.IsLive);

        _services.Alerts.DismissCurrent();
        Advance(TimeSpan.FromMinutes(5));
        Assert.Null(_services.Alerts.Current);
    }

    [Fact]
    public async Task Focus_KeepsRunning_AcrossARestart()
    {
        var (first, viewModel) = await StartFocusAsync();
        viewModel.ToggleCommand.Execute(null);
        Advance(TimeSpan.FromMinutes(4));
        await first.StopAsync();
        await first.DisposeAsync();

        Advance(TimeSpan.FromMinutes(6));
        var (second, again) = await StartFocusAsync();

        Assert.True(second.IsLive);
        Assert.Equal("15:00", again.TimeText);
    }

    [Fact]
    public async Task Focus_EndedWhileClosed_IsReportedOnce_AsLate()
    {
        var (first, viewModel) = await StartFocusAsync();
        viewModel.ToggleCommand.Execute(null);
        await first.StopAsync();
        await first.DisposeAsync();

        Advance(TimeSpan.FromMinutes(40));
        var (second, _) = await StartFocusAsync();

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Focus session done", alert.Alert.Title);
        Assert.StartsWith("It ended at ", alert.Alert.Subtitle, StringComparison.Ordinal);
        Assert.Null(alert.Alert.SoundKey);
        Assert.False(second.IsLive);
    }

    [Fact]
    public async Task Focus_TicksOnlyWhileRunningAndShown()
    {
        var (_, viewModel) = await StartFocusAsync();
        viewModel.ToggleCommand.Execute(null);
        // Hidden: only the exact wake at the end.
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        _visibility.Value = WidgetVisibility.Compact;
        Assert.Equal(2, _services.Scheduler.Stats.ArmedItems);
        _visibility.Value = WidgetVisibility.Expanded;
        Assert.Equal(2, _services.Scheduler.Stats.ArmedItems);

        viewModel.ToggleCommand.Execute(null);
        Assert.True(viewModel.IsPaused);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Stopwatch_CountsAcrossARestart_WithLaps()
    {
        var (first, viewModel) = await StartAsync<StopwatchWidget, StopwatchViewModel>(new StopwatchDescriptor());
        viewModel.ToggleCommand.Execute(null);
        Advance(TimeSpan.FromSeconds(75));
        viewModel.LapCommand.Execute(null);
        Assert.True(first.IsLive);
        Assert.Equal("Lap 1", Assert.Single(viewModel.Laps).Name);
        await first.StopAsync();
        await first.DisposeAsync();

        Advance(TimeSpan.FromMinutes(10));
        var (second, again) = await StartAsync<StopwatchWidget, StopwatchViewModel>(new StopwatchDescriptor());

        Assert.True(second.IsLive);
        Assert.Equal("11:15", again.TimeText);
        Assert.Equal("1:15", Assert.Single(again.Laps).Split);
    }

    [Fact]
    public async Task Stopwatch_Hidden_HasNoWakes()
    {
        var (_, viewModel) = await StartAsync<StopwatchWidget, StopwatchViewModel>(new StopwatchDescriptor());
        viewModel.ToggleCommand.Execute(null);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);

        _visibility.Value = WidgetVisibility.Panel;
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        Advance(TimeSpan.FromSeconds(3));
        viewModel.ToggleCommand.Execute(null);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        Assert.True(viewModel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public async Task WorldClock_ReadsEachPlace_AgainstHome()
    {
        var (_, viewModel) = await StartAsync<WorldClockWidget, WorldClockViewModel>(new WorldClockDescriptor());

        Assert.Equal(["London", "Tokyo", "Riyadh"], viewModel.Rows.Select(r => r.City));
        Assert.Equal("Same time", viewModel.Rows[0].Offset);
        Assert.Equal("+9 h", viewModel.Rows[1].Offset);
        Assert.Equal("+3 h", viewModel.Rows[2].Offset);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);

        _visibility.Value = WidgetVisibility.Expanded;
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
    }

    private Task<(FocusTimerWidget Widget, FocusTimerViewModel ViewModel)> StartFocusAsync() =>
        StartAsync<FocusTimerWidget, FocusTimerViewModel>(new FocusTimerDescriptor());

    private async Task<(TWidget Widget, TViewModel ViewModel)> StartAsync<TWidget, TViewModel>(IWidgetDescriptor descriptor)
        where TWidget : WidgetBase
    {
        var lifetime = new OwnerLifetime(descriptor.TypeId);
        _lifetimes.Add(lifetime);
        var widget = Assert.IsType<TWidget>(descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, lifetime, _visibility)));
        await widget.StartAsync(new SettingValues(descriptor.Settings, null));
        return (widget, Assert.IsType<TViewModel>(widget.ViewModel));
    }

    /// <summary>A second at a time, so every wake on the way runs.</summary>
    private void Advance(TimeSpan span)
    {
        for (var left = span; left > TimeSpan.Zero; left -= TimeSpan.FromSeconds(1))
            _services.Time.Advance(TimeSpan.FromSeconds(1));
    }
}
