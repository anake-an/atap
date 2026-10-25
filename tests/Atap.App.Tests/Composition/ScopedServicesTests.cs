using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Shared;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Composition;

public sealed class ScopedServicesTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly List<Exception> _errors = [];
    private readonly OwnerLifetime _lifetime;

    public ScopedServicesTests() => _lifetime = new OwnerLifetime("owner", _errors.Add);

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Scheduler_ItemsEndWithTheLifetime()
    {
        var scheduler = new ScopedScheduler(_services.Scheduler, _lifetime);
        scheduler.Every(TimeSpan.FromMinutes(1), () => { });
        scheduler.EveryMinute(() => { });
        scheduler.At(_services.Time.GetUtcNow().AddHours(1), () => { });
        Assert.Equal(3, _services.Scheduler.Stats.ArmedItems);

        _lifetime.Dispose();

        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public void Scheduler_ForgetsOneShotItemsOnceTheyRan()
    {
        var scheduler = new ScopedScheduler(_services.Scheduler, _lifetime);
        var runs = 0;
        for (var i = 1; i <= 50; i++)
            scheduler.At(_services.Time.GetUtcNow().AddSeconds(i), () => runs++);

        for (var i = 0; i < 50; i++)
            _services.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(50, runs);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public void Scheduler_ReportsThrowingCallbacksToTheOwner()
    {
        var scheduler = new ScopedScheduler(_services.Scheduler, _lifetime);
        scheduler.At(_services.Time.GetUtcNow().AddSeconds(1), () => throw new InvalidOperationException("boom"));

        _services.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.IsType<InvalidOperationException>(Assert.Single(_errors));
    }

    [Fact]
    public void Commands_AreRemovedWithTheLifetime()
    {
        var commands = new ScopedCommands(_services.Commands, _lifetime);
        commands.Register(new AtapCommand("owner.a", "owner.a", _ => Task.CompletedTask));
        var b = commands.Register(new AtapCommand("owner.b", "owner.b", _ => Task.CompletedTask));
        b.Dispose();

        _lifetime.Dispose();

        Assert.Empty(_services.Commands.All);
    }

    [Fact]
    public void Commands_ChangedHandlersEndWithTheLifetime()
    {
        var commands = new ScopedCommands(_services.Commands, _lifetime);
        var calls = 0;
        commands.Changed += (_, _) => calls++;
        _services.Commands.Register(new AtapCommand("other.a", "other.a", _ => Task.CompletedTask));
        Assert.Equal(1, calls);

        _lifetime.Dispose();
        _services.Commands.Register(new AtapCommand("other.b", "other.b", _ => Task.CompletedTask));
        commands.Changed += (_, _) => calls++;
        _services.Commands.Register(new AtapCommand("other.c", "other.c", _ => Task.CompletedTask));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Commands_RemovedHandlerIsNoLongerCalled()
    {
        var commands = new ScopedCommands(_services.Commands, _lifetime);
        var calls = 0;
        EventHandler handler = (_, _) => calls++;
        commands.Changed += handler;

        commands.Changed -= handler;
        _services.Commands.Register(new AtapCommand("other.a", "other.a", _ => Task.CompletedTask));

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Shared_LeasesEndWithTheLifetime()
    {
        var shared = new ScopedShared(_services.Shared, _lifetime);
        var first = shared.Acquire<Source>();
        shared.Acquire<Source>();
        first.Dispose();
        Assert.Equal(1, shared.LeaseCount);

        _lifetime.Dispose();

        Assert.Equal(0, _services.Shared.LeaseCount);
        _services.Time.Advance(SharedServices.GracePeriod);
        Assert.Equal(0, _services.Shared.ActiveCount);
    }

    [Fact]
    public void Lifetime_DisposesNewestFirstAndLateAdditionsAtOnce()
    {
        var order = new List<int>();
        _lifetime.Add(new Callback(() => order.Add(1)));
        _lifetime.Add(new Callback(() => order.Add(2)));

        _lifetime.Dispose();
        var late = new TrackedResource();
        _lifetime.Add(late);

        Assert.Equal([2, 1], order);
        Assert.True(late.IsDisposed);
        Assert.True(_lifetime.Stopping.IsCancellationRequested);
    }

    private sealed class Callback(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private sealed class Source : ISharedService<Source>
    {
        public static Source Create(SharedServiceContext context) => new();

        public void Dispose()
        {
        }
    }
}
