using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Composition;

public sealed class WidgetHostTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly CountingViews _views = new();
    private readonly TestDescriptor _alpha = new("alpha");
    private readonly TestDescriptor _beta = new("beta");
    private readonly TestDescriptor _gamma = new("gamma");
    private readonly TestDescriptor _multi = new("multi", multi: true);

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Apply_CreatesEnabledInstancesInOrder_AndSkipsUnknownMalformedAndDuplicateIds()
    {
        var host = CreateHost();

        await host.ApplyAsync(["beta", "nope", "alpha", "beta", "Bad Id", "alpha#named"]);

        Assert.Equal(["beta", "alpha"], host.Widgets.Select(w => w.Id));
        Assert.Empty(_gamma.Created);
        Assert.Equal(3, _services.Log.Count(l => l.Contains("unknown or not valid", StringComparison.Ordinal)));
        // Created, not started, until the host starts.
        Assert.Equal(0, _alpha.Created[0].Starts);
        Assert.Equal(WidgetStatus.Stopped, host.StatusOf("alpha"));
    }

    [Fact]
    public async Task Start_StartsEveryInstanceOnce()
    {
        var host = CreateHost();
        await host.ApplyAsync(["alpha", "beta"]);

        await host.StartAsync();

        Assert.Equal(1, _alpha.Created[0].Starts);
        Assert.Equal(1, _beta.Created[0].Starts);
        Assert.Equal(WidgetStatus.Running, host.StatusOf("alpha"));
    }

    [Fact]
    public async Task Apply_IsADiff()
    {
        var host = CreateHost();
        await host.ApplyAsync(["alpha", "beta"]);
        await host.StartAsync();
        var alpha = _alpha.Created[0];
        var beta = _beta.Created[0];

        await host.ApplyAsync(["beta", "gamma"]);

        // Removed: stopped and disposed.
        Assert.Equal(1, alpha.Stops);
        Assert.Equal(1, alpha.Disposals);
        // Kept: the same instance, not restarted.
        Assert.Same(beta, Assert.Single(_beta.Created));
        Assert.Equal(1, beta.Starts);
        Assert.Equal(0, beta.Stops);
        // Added: created and started at once, because the host has started.
        Assert.Equal(1, Assert.Single(_gamma.Created).Starts);
        Assert.Equal(["beta", "gamma"], host.Widgets.Select(w => w.Id));
    }

    [Fact]
    public async Task Stop_DisposesTheLifetimeAndEverythingItHeld()
    {
        var resource = new TrackedResource();
        _alpha.OnStart = widget =>
        {
            var context = widget.WidgetContext;
            context.Commands.Register(new AtapCommand("alpha.test", "alpha.test", _ => Task.CompletedTask));
            context.Scheduler.Every(TimeSpan.FromMinutes(1), () => { });
            context.Scheduler.EveryMinute(() => { });
            context.Lifetime.Add(resource);
            return Task.CompletedTask;
        };
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);
        await host.StartAsync();
        var stopping = _alpha.Created[0].WidgetContext.Lifetime.Stopping;
        Assert.True(_services.Commands.Contains("alpha.test"));
        Assert.Equal(2, _services.Scheduler.Stats.ArmedItems);

        await host.ApplyAsync([]);

        Assert.False(_services.Commands.Contains("alpha.test"));
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        Assert.True(resource.IsDisposed);
        Assert.True(stopping.IsCancellationRequested);
        Assert.Empty(host.Widgets);
    }

    [Fact]
    public async Task FiveErrorsWithin60Seconds_MarkFailedAndStop()
    {
        var host = CreateHost();
        await host.ApplyAsync(["alpha", "beta"]);
        await host.StartAsync();
        var lifetime = _alpha.Created[0].WidgetContext.Lifetime;

        for (var i = 0; i < 4; i++)
        {
            lifetime.ReportError(new InvalidOperationException("tick failed"));
            _services.Time.Advance(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(WidgetStatus.Running, host.StatusOf("alpha"));

        lifetime.ReportError(new InvalidOperationException("tick failed"));

        Assert.Equal(WidgetStatus.Failed, host.StatusOf("alpha"));
        Assert.Equal(1, _alpha.Created[0].Stops);
        Assert.Equal(["beta"], host.Widgets.Select(w => w.Id));
        Assert.Equal(["alpha", "beta"], host.InstanceIds);
    }

    [Fact]
    public async Task ErrorsSpreadOverMoreThan60Seconds_DoNotFail()
    {
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);
        await host.StartAsync();
        var lifetime = _alpha.Created[0].WidgetContext.Lifetime;

        for (var i = 0; i < 10; i++)
        {
            lifetime.ReportError(new InvalidOperationException("tick failed"));
            _services.Time.Advance(TimeSpan.FromSeconds(16));
        }

        Assert.Equal(WidgetStatus.Running, host.StatusOf("alpha"));
    }

    [Fact]
    public async Task ThrowingScheduledCallbacks_CountTowardFailed()
    {
        _alpha.OnStart = widget =>
        {
            widget.WidgetContext.Scheduler.Every(TimeSpan.FromSeconds(1), () => throw new InvalidOperationException("boom"));
            return Task.CompletedTask;
        };
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);
        await host.StartAsync();

        for (var i = 0; i < 5; i++)
            _services.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(WidgetStatus.Failed, host.StatusOf("alpha"));
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task StartThatTakesLongerThan10Seconds_TimesOutAndFails()
    {
        _alpha.OnStart = _ => new TaskCompletionSource().Task;
        var host = CreateHost();
        await host.ApplyAsync(["alpha", "beta"]);

        var start = host.StartAsync();
        _services.Time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(start.IsCompleted);
        Assert.Equal(WidgetStatus.Stopped, host.StatusOf("alpha"));

        _services.Time.Advance(TimeSpan.FromSeconds(1));
        await start;

        Assert.Equal(WidgetStatus.Failed, host.StatusOf("alpha"));
        Assert.Equal(1, _alpha.Created[0].Stops);
        Assert.Contains(_services.Log, l => l.Contains("did not start within 10 seconds", StringComparison.Ordinal));
        // The next widget still starts.
        Assert.Equal(WidgetStatus.Running, host.StatusOf("beta"));
    }

    [Fact]
    public async Task StartThatTimesOut_IsCancelledBeforeTheWidgetStops()
    {
        // A start that waits on its token. Before the widget's stop runs, the token is cancelled and the start has given up.
        Task? starting = null;
        bool? startGaveUpBeforeStop = null;
        _alpha.OnStart = widget => starting = Task.Delay(Timeout.InfiniteTimeSpan, widget.WidgetContext.Lifetime.Stopping);
        _alpha.OnStop = widget =>
        {
            startGaveUpBeforeStop = widget.WidgetContext.Lifetime.Stopping.IsCancellationRequested && starting!.IsCanceled;
            return Task.CompletedTask;
        };
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);

        var start = host.StartAsync();
        _services.Time.Advance(WidgetHost.StartTimeout);
        await start;

        Assert.Equal(WidgetStatus.Failed, host.StatusOf("alpha"));
        Assert.True(startGaveUpBeforeStop);
    }

    [Fact]
    public async Task StartThatThrows_Fails()
    {
        _alpha.OnStart = _ => throw new InvalidOperationException("no device");
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);

        await host.StartAsync();

        Assert.Equal(WidgetStatus.Failed, host.StatusOf("alpha"));
        Assert.Empty(host.Widgets);
    }

    [Fact]
    public async Task CreateThatThrows_FailsWithoutLeakingViews()
    {
        _alpha.ThrowOnCreate = true;
        var host = CreateHost();

        await host.ApplyAsync(["alpha"]);

        Assert.Equal(WidgetStatus.Failed, host.StatusOf("alpha"));
        Assert.Equal(0, _views.Loaded["alpha"]);
    }

    [Fact]
    public async Task Restart_AfterFailed_RunsAFreshInstance()
    {
        _alpha.OnStart = _ => throw new InvalidOperationException("once");
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);
        await host.StartAsync();
        _alpha.OnStart = null;

        await host.RestartAsync("alpha");

        Assert.Equal(WidgetStatus.Running, host.StatusOf("alpha"));
        Assert.Equal(2, _alpha.Created.Count);
        Assert.Same(_alpha.Created[1], Assert.Single(host.Widgets));
    }

    [Fact]
    public async Task RestartSoon_RestartsOnce500MsAfterTheLastCall()
    {
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);
        await host.StartAsync();

        host.RestartSoon("alpha");
        _services.Time.Advance(TimeSpan.FromMilliseconds(300));
        host.RestartSoon("alpha");
        _services.Time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Single(_alpha.Created);

        _services.Time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(2, _alpha.Created.Count);
        Assert.Equal(1, _alpha.Created[0].Disposals);
        Assert.Equal(1, _alpha.Created[1].Starts);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Visibility_IsTheHighestOverEveryWindow()
    {
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);
        var visibility = _alpha.Created[0].WidgetContext.Visibility;
        var changes = 0;
        visibility.Changed += (_, _) => changes++;
        object primary = new(), secondary = new();

        host.ReportVisibility(primary, new Dictionary<string, WidgetVisibility> { ["alpha"] = WidgetVisibility.Idle });
        host.ReportVisibility(secondary, new Dictionary<string, WidgetVisibility> { ["alpha"] = WidgetVisibility.Expanded });
        Assert.Equal(WidgetVisibility.Expanded, visibility.Value);

        // The same report again from another window changes nothing.
        host.ReportVisibility(primary, new Dictionary<string, WidgetVisibility> { ["alpha"] = WidgetVisibility.Expanded });
        Assert.Equal(2, changes);

        host.ReportVisibility(primary, new Dictionary<string, WidgetVisibility>());
        host.RemoveReporter(secondary);
        Assert.Equal(WidgetVisibility.Hidden, visibility.Value);
        Assert.Equal(WidgetVisibility.Hidden, host.VisibilityOf("alpha"));
    }

    [Fact]
    public async Task NewInstance_StartsWithTheCurrentVisibility()
    {
        var host = CreateHost();
        host.ReportVisibility(this, new Dictionary<string, WidgetVisibility> { ["alpha"] = WidgetVisibility.Compact });

        await host.ApplyAsync(["alpha"]);

        Assert.Equal(WidgetVisibility.Compact, _alpha.Created[0].WidgetContext.Visibility.Value);
    }

    [Fact]
    public async Task Views_AreLoadedOncePerTypeAndReleasedAfterTheLastInstance()
    {
        var host = CreateHost();

        await host.ApplyAsync(["multi#a", "multi#b", "alpha"]);
        Assert.Equal(2, _views.Loaded["multi"]);
        Assert.Equal(2, _views.TotalLoads);

        await host.ApplyAsync(["multi#b"]);
        Assert.Equal(1, _views.Loaded["multi"]);
        Assert.Equal(0, _views.Loaded["alpha"]);

        await host.DisposeAsync();
        Assert.Equal(0, _views.Loaded["multi"]);
    }

    [Fact]
    public async Task Dispose_StopsEverythingNewestFirst()
    {
        var host = CreateHost();
        await host.ApplyAsync(["alpha", "beta"]);
        await host.StartAsync();

        await host.DisposeAsync();

        Assert.Equal(1, _alpha.Created[0].Stops);
        Assert.Equal(1, _beta.Created[0].Disposals);
        Assert.Empty(host.Widgets);
        Assert.Equal(0, _services.Shared.ActiveCount);
    }

    [Fact]
    public async Task DisableDuringStart_StopsOnlyAfterTheStartFinished()
    {
        // For example media turned on and off again while it still waits for the media session manager.
        var starting = new TaskCompletionSource();
        _alpha.OnStart = _ => starting.Task;
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);
        var start = host.StartAsync();

        var stop = host.ApplyAsync([]);
        Assert.False(stop.IsCompleted);
        Assert.Equal(0, _alpha.Created[0].Stops);

        starting.SetResult();
        await start;
        await stop;

        Assert.Equal(["alpha start", "alpha started", "alpha stop", "alpha dispose"], _alpha.Events);
        Assert.Empty(host.Widgets);
    }

    [Fact]
    public async Task DisableAndEnableQuickly_StartsTheNewInstanceAfterTheOldOneLetGo()
    {
        var stopping = new TaskCompletionSource();
        _alpha.OnStart = widget =>
        {
            widget.WidgetContext.Commands.Register(new AtapCommand("alpha.playPause", "alpha.playPause", _ => Task.CompletedTask));
            return Task.CompletedTask;
        };
        _alpha.OnStop = _ => stopping.Task;
        var host = CreateHost();
        await host.ApplyAsync(["alpha"]);
        await host.StartAsync();

        var off = host.ApplyAsync([]);
        var on = host.ApplyAsync(["alpha"]);
        Assert.Single(_alpha.Created);

        stopping.SetResult();
        await off;
        await on;

        Assert.Equal(2, _alpha.Created.Count);
        Assert.Equal(WidgetStatus.Running, host.StatusOf("alpha"));
        Assert.True(_services.Commands.Contains("alpha.playPause"));
        Assert.DoesNotContain(_services.Log, l => l.Contains("failed to start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidIds_AreLoggedWithoutControlCharactersAndCut()
    {
        var host = CreateHost();

        await host.ApplyAsync(["bad\r\nWARN forged line‮" + new string('x', 500)]);

        var line = Assert.Single(_services.Log);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('‮', line);
        Assert.Contains("badWARN forged line", line, StringComparison.Ordinal);
        Assert.True(line.Length < 200, line);
    }

    [Fact]
    public async Task AFileFullOfBadIds_CannotFillTheLog()
    {
        var host = CreateHost();

        await host.ApplyAsync(Enumerable.Range(0, 1000).Select(i => $"bad id {i}"));

        Assert.True(_services.Log.Count <= 50);
    }

    private WidgetHost CreateHost() => new(
        WidgetCatalog.FromDescriptors([_alpha, _beta, _gamma, _multi]),
        _services.CreateContext,
        _views,
        (_, descriptor) => new SettingValues(descriptor.Settings, null),
        _services.Time,
        _services.Scheduler,
        ui: null,
        _services.Warn);
}
