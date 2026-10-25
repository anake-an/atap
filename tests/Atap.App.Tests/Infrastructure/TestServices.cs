using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Shared;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Notch;
using Atap.Platform.Network;
using Atap.Platform.Power;
using Atap.Platform.Secrets;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Tests.Infrastructure;

/// <summary>Real Core services on a fake clock, for building widget and feature contexts in tests.</summary>
internal sealed class TestServices : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));
    private LayoutStore? _layout;
    private WidgetHost? _widgets;

    public TestServices()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
        Scheduler = new Scheduler(Time);
        Shared = new SharedServices(Ui, Time, Scheduler);
        Paths = new AtapPaths(Path.Combine(_folder, "roaming"), Path.Combine(_folder, "local"));
        Settings = new SettingsStore(Paths.SettingsFile, Time);
        Http = new HttpDataService("test", Network);
    }

    public FakeTimeProvider Time { get; }

    public SynchronizationContext Ui { get; } = new();

    public Scheduler Scheduler { get; }

    public CommandRegistry Commands { get; } = new();

    public SharedServices Shared { get; }

    public AlertQueue Alerts { get; } = new(TimeProvider.System);

    public Localizer Text { get; } = new(new AssemblyStringResources(typeof(App).Assembly), Localizer.English);

    public AtapPaths Paths { get; }

    public SettingsStore Settings { get; }

    public NetworkState Network { get; } = new();

    public HttpDataService Http { get; }

    public List<string> Log { get; } = [];

    /// <summary>A layout store on the temp folder. Load it in the test when the file matters.</summary>
    public LayoutStore Layout => _layout ??= new LayoutStore(Paths.LayoutFile, timeProvider: Time);

    public void Warn(string message, Exception? exception) => Log.Add(exception is null ? message : $"{message} {exception.GetType().Name}");

    /// <summary>A widget host over <paramref name="catalog"/>, on the fake clock, with errors handled inline.</summary>
    public WidgetHost CreateWidgetHost(WidgetCatalog catalog, IWidgetViews? views = null) => new(
        catalog,
        CreateContext,
        views ?? new NoViews(),
        (_, descriptor) => new SettingValues(descriptor.Settings, null),
        Time,
        Scheduler,
        ui: null,
        Warn);

    /// <summary>
    /// What <see cref="AppServices.CreateFeatureContext"/> builds, from these services. <see cref="FeatureContext.Services"/>
    /// is left null: AppServices needs the UI thread and a foreground hook, so a feature that reaches it is
    /// tested through its own parts instead. AppServicesTests covers the real context.
    /// </summary>
    public FeatureContext CreateFeatureContext(
        IFeatureDescriptor descriptor, OwnerLifetime lifetime, AppMode mode, INotchControl notch, IWidgetHostView? widgets = null) => new()
    {
        FeatureId = descriptor.Id,
        Mode = mode,
        Ui = Ui,
        Time = Time,
        Scheduler = new ScopedScheduler(Scheduler, lifetime),
        SchedulerControl = Scheduler,
        Http = Http,
        Text = Text,
        Commands = new ScopedCommands(Commands, lifetime),
        Shared = new ScopedShared(Shared, lifetime),
        Power = new PowerState(new FakePowerReadings(), Settings),
        Network = Network,
        Secrets = new DpapiSecretStore(Paths.SecretsFile),
        Settings = Settings,
        Layout = Layout,
        Foreground = new ForegroundStub(),
        Alerts = Alerts,
        Notch = notch,
        Widgets = widgets ?? (_widgets ??= CreateWidgetHost(WidgetCatalog.FromDescriptors([]))),
        SettingsCards = [],
        Services = null!,
        Paths = Paths,
        Lifetime = lifetime,
    };

    public WidgetContext CreateContext(string instanceId, IWidgetDescriptor descriptor, OwnerLifetime lifetime, IObservableValue<WidgetVisibility> visibility) =>
        CreateContext(instanceId, descriptor, lifetime, visibility, Http.For(descriptor.Capabilities));

    /// <summary>The same, with a fake network in place of the real HTTP client.</summary>
    public WidgetContext CreateContext(
        string instanceId, IWidgetDescriptor descriptor, OwnerLifetime lifetime, IObservableValue<WidgetVisibility> visibility, IHttpDataService http) => new()
    {
        InstanceId = instanceId,
        Descriptor = descriptor,
        Ui = Ui,
        Time = Time,
        Scheduler = new ScopedScheduler(Scheduler, lifetime),
        Http = http,
        Text = Text,
        Alerts = Alerts.For(instanceId),
        Commands = new ScopedCommands(Commands, lifetime),
        Shared = new ScopedShared(Shared, lifetime),
        App = Settings,
        Power = new PowerState(new FakePowerReadings(), Settings),
        Network = Network,
        Secrets = new DpapiSecretStore(Paths.SecretsFile),
        Storage = new WidgetStorage(instanceId, Paths),
        Visibility = visibility,
        Lifetime = lifetime,
    };

    public void Dispose()
    {
        Shared.Dispose();
        Scheduler.Dispose();
        Http.Dispose();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private sealed class NoViews : IWidgetViews
    {
        public IDisposable Acquire(IWidgetDescriptor descriptor) => new Nothing();
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>Full-screen state without the real foreground hook, which needs a message loop.</summary>
internal sealed class ForegroundStub : Atap.Core.Notch.IForegroundState
{
    public Atap.Core.Notch.FullScreenKind FullScreen => Atap.Core.Notch.FullScreenKind.None;

    public string? ProcessName => null;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public IDisposable WantProcessNames() => new Release();

    private sealed class Release : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
