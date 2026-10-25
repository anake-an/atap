using Atap.Core.Rules;
using Atap.Core.Scheduling;
using Atap.Core.Tests.Scheduling;

namespace Atap.Core.Tests.Rules;

public sealed class ProfileSwitchDebouncerTests : IDisposable
{
    private readonly CountingTime _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly Scheduler _scheduler;
    private readonly List<string> _applied = [];
    private readonly ProfileSwitchDebouncer _debouncer;

    public ProfileSwitchDebouncerTests()
    {
        _scheduler = new Scheduler(_time);
        _debouncer = new ProfileSwitchDebouncer(_scheduler, _time, "default", _applied.Add);
    }

    public void Dispose()
    {
        _debouncer.Dispose();
        _scheduler.Dispose();
    }

    [Fact]
    public void Switches_OnlyAfterOnePointFiveStableSeconds()
    {
        _debouncer.Propose("gaming");
        _time.Advance(TimeSpan.FromSeconds(1));
        _debouncer.Propose("work");
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(_applied);

        _time.Advance(TimeSpan.FromSeconds(0.6));

        Assert.Equal(["work"], _applied);
        Assert.Equal("work", _debouncer.Current);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void GoingBackBeforeTheDelay_CancelsTheSwitch()
    {
        _debouncer.Propose("gaming");
        _time.Advance(TimeSpan.FromSeconds(1));
        _debouncer.Propose("default");
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Empty(_applied);
        Assert.Null(_debouncer.Pending);
    }

    [Fact]
    public void WaitsForTheEditorToClose()
    {
        _debouncer.SetEditing(true);
        _debouncer.Propose("gaming");
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Empty(_applied);
        Assert.Equal("gaming", _debouncer.Pending);

        _debouncer.SetEditing(false);

        Assert.Equal(["gaming"], _applied);
    }
}
