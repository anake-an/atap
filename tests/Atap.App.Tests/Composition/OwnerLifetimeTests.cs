using Atap.Composition;

namespace Atap.Tests.Composition;

public sealed class OwnerLifetimeTests
{
    [Fact]
    public void Dispose_ReleasesEverythingNewestFirst()
    {
        var order = new List<string>();
        var lifetime = new OwnerLifetime("owner");
        lifetime.Add(new Named("first", order));
        lifetime.Add(new Named("second", order));

        lifetime.Dispose();

        Assert.Equal(["second", "first"], order);
        Assert.True(lifetime.Stopping.IsCancellationRequested);
        Assert.Equal(0, lifetime.ResourceCount);
    }

    [Fact]
    public void ThrowingStoppingCallback_IsReported_AndResourcesAreStillReleased()
    {
        var lifetime = new OwnerLifetime("owner");
        var failures = new List<Exception>();
        lifetime.DisposeFailed += (_, ex) => failures.Add(ex);
        var resource = new TrackedResource();
        lifetime.Add(resource);
        var laterCallbackRan = false;
        lifetime.Stopping.Register(() => laterCallbackRan = true);
        lifetime.Stopping.Register(() => throw new InvalidOperationException("callback failed"));

        lifetime.Dispose();

        Assert.True(resource.IsDisposed);
        Assert.True(laterCallbackRan);
        Assert.IsType<AggregateException>(Assert.Single(failures));
    }

    [Fact]
    public void ThrowingResource_IsReported_AndTheRestAreReleased()
    {
        var lifetime = new OwnerLifetime("owner");
        var failures = new List<Exception>();
        lifetime.DisposeFailed += (_, ex) => failures.Add(ex);
        var resource = new TrackedResource();
        lifetime.Add(resource);
        lifetime.Add(new Throwing());

        lifetime.Dispose();

        Assert.True(resource.IsDisposed);
        Assert.IsType<InvalidOperationException>(Assert.Single(failures));
    }

    [Fact]
    public void AddAfterDispose_ReleasesAtOnce()
    {
        var lifetime = new OwnerLifetime("owner");
        lifetime.Dispose();
        var resource = new TrackedResource();

        lifetime.Add(resource);

        Assert.True(resource.IsDisposed);
    }

    private sealed class Named(string name, List<string> order) : IDisposable
    {
        public void Dispose() => order.Add(name);
    }

    private sealed class Throwing : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("release failed");
    }
}
