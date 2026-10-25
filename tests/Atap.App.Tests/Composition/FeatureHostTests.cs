using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Notch;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Composition;

public sealed class FeatureHostTests : IDisposable
{
    private const string EnabledKey = "sample.enabled";

    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly SampleFeature _descriptor = new();

    public FeatureHostTests()
    {
        _services.Settings.Load();
        _services.Settings.Register(_descriptor.Settings);
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task FeatureThatIsOff_IsNeverCreated()
    {
        var host = CreateHost(AppMode.Normal);

        await host.StartAsync();

        Assert.Equal(0, _descriptor.Created);
        Assert.Empty(host.RunningIds);
    }

    [Fact]
    public async Task TurningTheSettingOnAndOff_StartsAndStopsTheFeature()
    {
        var host = CreateHost(AppMode.Normal);
        await host.StartAsync();

        _services.Settings.Set(EnabledKey, true);
        await WaitUntil(() => host.RunningIds.Count == 1);
        var feature = _descriptor.Last!;
        Assert.True(feature.Started);
        Assert.True(_notch.IsSet(NotchFlag.Slim));
        Assert.True(_services.Commands.Contains("sample.run"));

        _services.Settings.Set(EnabledKey, false);
        await WaitUntil(() => host.RunningIds.Count == 0);

        Assert.True(feature.Disposed);
        Assert.False(_services.Commands.Contains("sample.run"));
        // Flags the feature set are cleared when it stops.
        Assert.False(_notch.IsSet(NotchFlag.Slim));
    }

    [Fact]
    public async Task UnrelatedSettingChanges_DoNotRecheck()
    {
        var host = CreateHost(AppMode.Normal);
        await host.StartAsync();
        var checks = _descriptor.Checks;

        _services.Settings.Set("other.key", 1);

        Assert.Equal(checks, _descriptor.Checks);
    }

    [Fact]
    public async Task SafeMode_KeepsTheFeatureOff()
    {
        _services.Settings.Set(EnabledKey, true);
        var host = CreateHost(AppMode.Safe);

        await host.StartAsync();

        Assert.Equal(0, _descriptor.Created);
    }

    [Fact]
    public async Task Dispose_StopsRunningFeatures()
    {
        _services.Settings.Set(EnabledKey, true);
        var host = CreateHost(AppMode.Normal);
        await host.StartAsync();

        await host.DisposeAsync();

        Assert.True(_descriptor.Last!.Disposed);
        Assert.Empty(host.RunningIds);
    }

    [Fact]
    public async Task Feature_SeesTheWidgetsAndCanRestartOne()
    {
        var widget = new TestDescriptor("alpha");
        var widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([widget]));
        await widgets.ApplyAsync(["alpha"]);
        await widgets.StartAsync();
        _services.Settings.Set(EnabledKey, true);
        var host = CreateHost(AppMode.Normal, widgets);
        await host.StartAsync();
        var view = _descriptor.Last!.Context.Widgets;
        var changes = new List<string>();
        view.StatusChanged += (_, id) => changes.Add(id);

        Assert.Contains(view.Catalog.Descriptors, d => d.TypeId == "alpha");
        Assert.Equal(WidgetStatus.Running, view.StatusOf("alpha"));
        await view.RestartAsync("alpha");

        Assert.Equal(2, widget.Created.Count);
        Assert.Equal(WidgetStatus.Running, view.StatusOf("alpha"));
        Assert.Contains("alpha", changes);
    }

    [Fact]
    public async Task TurningOffDuringStart_StopsOnlyAfterTheStartFinished()
    {
        var starting = new TaskCompletionSource();
        _descriptor.StartGate = starting.Task;
        var host = CreateHost(AppMode.Normal);
        await host.StartAsync();

        _services.Settings.Set(EnabledKey, true);
        _services.Settings.Set(EnabledKey, false);
        var feature = _descriptor.Last!;
        Assert.False(feature.Disposed);
        Assert.Empty(host.RunningIds);

        starting.SetResult();
        await WaitUntil(() => feature.Disposed);

        Assert.Equal(["start", "started", "dispose"], _descriptor.Events);
        Assert.Empty(host.RunningIds);
        Assert.False(_services.Commands.Contains("sample.run"));
        Assert.False(_notch.IsSet(NotchFlag.Slim));
    }

    [Fact]
    public async Task DisposeDuringStart_StopsAfterTheStartFinished()
    {
        var starting = new TaskCompletionSource();
        _descriptor.StartGate = starting.Task;
        _services.Settings.Set(EnabledKey, true);
        var host = CreateHost(AppMode.Normal);
        var start = host.StartAsync();

        var dispose = host.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        Assert.False(_descriptor.Last!.Disposed);

        starting.SetResult();
        await start;
        await dispose;

        Assert.Equal(["start", "started", "dispose"], _descriptor.Events);
        Assert.Empty(host.RunningIds);
    }

    [Fact]
    public async Task Dispose_StopsLastStartedFirst_AfterAFeatureWasRestarted()
    {
        var stopped = new List<string>();
        SampleFeature[] features = [new("a", stopped), new("b", stopped), new("c", stopped)];
        foreach (var feature in features)
        {
            _services.Settings.Register(feature.Settings);
            _services.Settings.Set(feature.Key, true);
        }
        var host = CreateHost(AppMode.Normal, descriptors: features);
        await host.StartAsync();

        _services.Settings.Set("a.enabled", false);
        await WaitUntil(() => host.RunningIds.Count == 2);
        _services.Settings.Set("a.enabled", true);
        await WaitUntil(() => host.RunningIds.Count == 3);
        stopped.Clear();

        await host.DisposeAsync();

        Assert.Equal(["a", "c", "b"], stopped);
    }

    [Fact]
    public async Task DisposeDuringAStartThatWaitsForStopping_DoesNotHang()
    {
        _descriptor.WaitForStopping = true;
        _services.Settings.Set(EnabledKey, true);
        var host = CreateHost(AppMode.Normal);
        var start = host.StartAsync();

        await host.DisposeAsync();
        await start;

        Assert.Equal(["start", "dispose"], _descriptor.Events);
        Assert.Empty(host.RunningIds);
        // Cancelled by Quit, so it is not reported as a failure.
        Assert.DoesNotContain(_services.Log, line => line.Contains("start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartThatNeverFinishes_GivesUpAfterTheTimeLimit()
    {
        _descriptor.StartGate = new TaskCompletionSource().Task;
        var host = CreateHost(AppMode.Normal);
        await host.StartAsync();

        _services.Settings.Set(EnabledKey, true);
        await WaitUntil(() => _descriptor.Events.Count == 1);
        // Stepped, so the limit is passed even if the host arms its timer a moment after the start began.
        await WaitUntil(() =>
        {
            _services.Time.Advance(TimeSpan.FromSeconds(1));
            return _descriptor.Last!.Disposed;
        });

        Assert.Equal(["start", "dispose"], _descriptor.Events);
        Assert.Empty(host.RunningIds);
        Assert.Contains(_services.Log, line => line.Contains("did not start within", StringComparison.Ordinal));
        Assert.False(_services.Commands.Contains("sample.run"));

        // The host still works after a stuck start.
        _descriptor.StartGate = null;
        _services.Settings.Set(EnabledKey, false);
        _services.Settings.Set(EnabledKey, true);
        await WaitUntil(() => host.RunningIds.Count == 1);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition());
    }

    private FeatureHost CreateHost(AppMode mode, IWidgetHostView? widgets = null, IReadOnlyList<IFeatureDescriptor>? descriptors = null) => new(
        descriptors ?? [_descriptor],
        _services.Settings,
        mode,
        (descriptor, lifetime) => _services.CreateFeatureContext(descriptor, lifetime, mode, _notch, widgets),
        _notch.ClearOwner,
        _services.Warn,
        _services.Time);

    /// <param name="id">Also names its setting, <c>{id}.enabled</c>, and its command, <c>{id}.run</c>.</param>
    /// <param name="stopped">Shared by several features: each adds its id when it is disposed.</param>
    private sealed class SampleFeature(string id = "sample", List<string>? stopped = null) : IFeatureDescriptor
    {
        public int Created { get; private set; }

        public int Checks { get; private set; }

        public Running? Last { get; private set; }

        public string Id => id;

        public string Key { get; } = $"{id}.enabled";

        public IReadOnlyList<SettingDefinition> Settings => [new SettingDefinition(Key, Key, SettingType.Bool, false)];

        public IReadOnlyCollection<string> EnabledBy => [Key];

        public bool IsEnabled(SettingValues settings, AppMode mode)
        {
            Checks++;
            return mode == AppMode.Normal && settings.GetBool(Key);
        }

        /// <summary>Every start waits for it. Leave it null for a start that finishes at once.</summary>
        public Task? StartGate { get; set; }

        /// <summary>Every start waits until the feature is stopped, the way long work should.</summary>
        public bool WaitForStopping { get; set; }

        public List<string>? Stopped => stopped;

        /// <summary>What every feature did, in order: "start", "started" and "dispose".</summary>
        public List<string> Events { get; } = [];

        public IAppFeature Create(FeatureContext context)
        {
            Created++;
            Last = new Running(context, this);
            return Last;
        }

        public sealed class Running(FeatureContext context, SampleFeature owner) : IAppFeature
        {
            public FeatureContext Context => context;

            public bool Started { get; private set; }

            public bool Disposed { get; private set; }

            public async Task StartAsync()
            {
                owner.Events.Add("start");
                Started = true;
                context.Notch.SetFlag(NotchFlag.Slim, context.FeatureId, true);
                context.Commands.Register(new($"{owner.Id}.run", $"{owner.Id}.run", _ => Task.CompletedTask));
                if (owner.WaitForStopping)
                    await Task.Delay(Timeout.Infinite, context.Lifetime.Stopping);
                await (owner.StartGate ?? Task.CompletedTask);
                owner.Events.Add("started");
            }

            public ValueTask DisposeAsync()
            {
                owner.Events.Add("dispose");
                owner.Stopped?.Add(owner.Id);
                Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
