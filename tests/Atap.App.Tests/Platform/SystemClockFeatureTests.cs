using Atap.Composition;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Platform.Clock;

namespace Atap.Tests.Platform;

public sealed class SystemClockFeatureTests
{
    private readonly CountingScheduler _scheduler = new();
    private readonly FakeClockEvents _source = new();

    [Fact]
    public async Task ClockChangesAndResume_ReachTheScheduler()
    {
        var feature = new SystemClockEvents(_scheduler, new InlineContext(), _source);
        await feature.StartAsync();

        _source.RaiseClockChanged();
        _source.RaiseResumed();
        _source.RaiseClockChanged();

        Assert.Equal(2, _scheduler.ClockChanges);
        Assert.Equal(1, _scheduler.Resumes);
    }

    [Fact]
    public async Task AfterStop_EventsAreIgnoredAndTheSourceIsReleased()
    {
        var feature = new SystemClockEvents(_scheduler, new InlineContext(), _source);
        await feature.StartAsync();

        await feature.DisposeAsync();
        _source.RaiseClockChanged();
        _source.RaiseResumed();

        Assert.Equal(0, _scheduler.ClockChanges + _scheduler.Resumes);
        Assert.True(_source.IsDisposed);
        Assert.False(_source.HasHandlers);
    }

    [Fact]
    public void Descriptor_IsRegisteredAndAlwaysOn()
    {
        var problems = new List<string>();
        var features = Discovery.Features(typeof(App).Assembly, problems);

        var clock = Assert.Single(features, f => f.Id == SystemClockFeature.FeatureId);
        Assert.Empty(problems);
        Assert.Empty(clock.EnabledBy);
        Assert.True(clock.IsEnabled(SettingValues.Empty, AppMode.Normal));
        Assert.True(clock.IsEnabled(SettingValues.Empty, AppMode.Safe));
    }

    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class CountingScheduler : ISchedulerControl
    {
        public int ClockChanges { get; private set; }

        public int Resumes { get; private set; }

        public SchedulerStats Stats => default;

        public bool SaverActive => false;

        public event EventHandler<SchedulerFault>? JobFailed
        {
            add { }
            remove { }
        }

        public void NotifyClockChanged() => ClockChanges++;

        public void NotifyResumed() => Resumes++;
    }

    private sealed class FakeClockEvents : IClockEventSource
    {
        public event EventHandler? ClockChanged;

        public event EventHandler? Resumed;

        public bool IsDisposed { get; private set; }

        public bool HasHandlers => ClockChanged is not null || Resumed is not null;

        public void RaiseClockChanged() => ClockChanged?.Invoke(this, EventArgs.Empty);

        public void RaiseResumed() => Resumed?.Invoke(this, EventArgs.Empty);

        public void Dispose() => IsDisposed = true;
    }
}
