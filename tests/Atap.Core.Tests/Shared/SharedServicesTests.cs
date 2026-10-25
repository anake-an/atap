using Atap.Core.Scheduling;
using Atap.Core.Shared;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Shared;

public sealed class SharedServicesTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly Scheduler _scheduler;
    private readonly SharedServices _shared;

    public SharedServicesTests()
    {
        _scheduler = new Scheduler(_time);
        _shared = new SharedServices(new SynchronizationContext(), _time, _scheduler);
        CountingSource.Reset();
    }

    public void Dispose()
    {
        _shared.Dispose();
        _scheduler.Dispose();
    }

    [Fact]
    public void NothingIsCreatedUntilTheFirstLease()
    {
        Assert.Equal(0, CountingSource.Created);
        Assert.Equal(0, _shared.ActiveCount);
    }

    [Fact]
    public void Acquire_CreatesOnceAndSharesTheInstance()
    {
        using var first = _shared.Acquire<CountingSource>();
        using var second = _shared.Acquire<CountingSource>();

        Assert.Same(first.Value, second.Value);
        Assert.Equal(1, CountingSource.Created);
        Assert.Equal(1, _shared.ActiveCount);
        Assert.Equal(2, _shared.LeaseCount);
        Assert.Same(_shared, first.Value.Context.Shared);
    }

    [Fact]
    public void Release_KeepsTheSourceWhileAnyLeaseRemains()
    {
        var first = _shared.Acquire<CountingSource>();
        var second = _shared.Acquire<CountingSource>();

        first.Dispose();
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(second.Value.IsDisposed);
        second.Dispose();
    }

    [Fact]
    public void LastRelease_DisposesAfterTheGracePeriod()
    {
        var lease = _shared.Acquire<CountingSource>();
        var source = lease.Value;

        lease.Dispose();
        _time.Advance(SharedServices.GracePeriod - TimeSpan.FromMilliseconds(1));
        Assert.False(source.IsDisposed);
        Assert.Equal(1, _shared.ActiveCount);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(source.IsDisposed);
        Assert.Equal(0, _shared.ActiveCount);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void AcquireDuringGrace_ReusesTheSourceWithoutChurn()
    {
        var lease = _shared.Acquire<CountingSource>();
        var source = lease.Value;
        lease.Dispose();
        _time.Advance(TimeSpan.FromSeconds(10));

        using var again = _shared.Acquire<CountingSource>();
        _time.Advance(SharedServices.GracePeriod * 2);

        Assert.Same(source, again.Value);
        Assert.False(source.IsDisposed);
        Assert.Equal(1, CountingSource.Created);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void AcquireAfterDisposal_CreatesAFreshSource()
    {
        var lease = _shared.Acquire<CountingSource>();
        var first = lease.Value;
        lease.Dispose();
        _time.Advance(SharedServices.GracePeriod);

        using var again = _shared.Acquire<CountingSource>();

        Assert.NotSame(first, again.Value);
        Assert.Equal(2, CountingSource.Created);
    }

    [Fact]
    public void DisposingALeaseTwice_ReleasesOnce()
    {
        var first = _shared.Acquire<CountingSource>();
        using var second = _shared.Acquire<CountingSource>();

        first.Dispose();
        first.Dispose();

        Assert.Equal(1, _shared.LeaseCount);
        Assert.True(first.IsReleased);
    }

    [Fact]
    public void Dispose_DisposesEverySourceAtOnce()
    {
        var lease = _shared.Acquire<CountingSource>();

        _shared.Dispose();

        Assert.True(lease.Value.IsDisposed);
        Assert.Equal(0, _shared.ActiveCount);
    }

    private sealed class CountingSource : ISharedService<CountingSource>
    {
        private CountingSource(SharedServiceContext context) => Context = context;

        public static int Created { get; private set; }

        public SharedServiceContext Context { get; }

        public bool IsDisposed { get; private set; }

        public static CountingSource Create(SharedServiceContext context)
        {
            Created++;
            return new CountingSource(context);
        }

        public static void Reset() => Created = 0;

        public void Dispose() => IsDisposed = true;
    }
}
