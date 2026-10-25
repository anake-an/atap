using System.Globalization;
using Atap.Composition;
using Atap.Core.Localization;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Atap.Updates;

namespace Atap.Tests.Composition;

/// <summary>
/// The real <see cref="AppServices"/>, built on the test UI thread over a temp folder. <see cref="TestServices"/>
/// leaves <see cref="FeatureContext.Services"/> null, so this is where features and settings cards prove they
/// reach the update service and Quit.
/// </summary>
public sealed class AppServicesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeUpdates _updates = new();
    private int _quits;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public Task FeatureContext_ReachesUpdatesAndQuit() => WithServicesAsync(_updates, async services =>
    {
        using var lifetime = new OwnerLifetime("probe");
        var context = services.CreateFeatureContext(services.Features[0], lifetime);

        Assert.Same(services, context.Services);
        Assert.Same(_updates, context.Services.Updates);
        Assert.True(await context.Commands.TryRunAsync(AppCommands.CheckForUpdates));
        Assert.True(await context.Commands.TryRunAsync(AppCommands.Quit));

        Assert.Equal(1, _updates.Checks);
        Assert.Equal(1, _quits);
    });

    [Fact]
    public Task SettingsCardContext_ReachesUpdatesAndQuit() => WithServicesAsync(_updates, async services =>
    {
        // The settings window is a feature. It gives each card the services from its own context.
        using var feature = new OwnerLifetime("probe");
        using var window = new OwnerLifetime("probe.window");
        var card = new SettingsCardContext(services.CreateFeatureContext(services.Features[0], feature).Services, window);

        Assert.Same(_updates, card.Services.Updates);
        Assert.True(await card.Services.Commands.TryRunAsync(AppCommands.CheckForUpdates));
        Assert.True(await card.Services.Commands.TryRunAsync(AppCommands.Quit));

        Assert.Equal(1, _updates.Checks);
        Assert.Equal(1, _quits);
    });

    [Fact]
    public Task StoreBuild_FeatureContextHasQuitAndNoUpdates() => WithServicesAsync(null, async services =>
    {
        using var lifetime = new OwnerLifetime("probe");
        var context = services.CreateFeatureContext(services.Features[0], lifetime);

        Assert.Null(context.Services.Updates);
        Assert.False(context.Commands.Contains(AppCommands.CheckForUpdates));
        Assert.True(await context.Commands.TryRunAsync(AppCommands.Quit));
        Assert.Equal(1, _quits);
    });

    /// <summary>
    /// Builds the services as App.OnStartup does, with the app commands registered, on the test UI thread.
    /// The UI language and culture are put back afterwards, for the other tests on that thread.
    /// </summary>
    private Task WithServicesAsync(IUpdateService? updates, Func<AppServices, Task> test) => Sta.RunAsync(async () =>
    {
        var paths = new AtapPaths(Path.Combine(_folder, "roaming"), Path.Combine(_folder, "local"));
        // English whatever this PC's language, as on the rest of the test UI thread.
        Directory.CreateDirectory(paths.Roaming);
        File.WriteAllText(paths.SettingsFile, """{ "schemaVersion": 1, "values": { "language.choice": "en" } }""");

        var text = Localizer.Instance;
        var uiCulture = CultureInfo.CurrentUICulture;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var services = AppServices.Create(Sta.Dispatcher, AppMode.Normal, typeof(App).Assembly, paths, new NoViews(), updates);
        try
        {
            Assert.NotEmpty(services.Features);
            AppCommands.Register(services.Commands, () => _quits++, updates);
            await test(services);
        }
        finally
        {
            services.Dispose();
            Localizer.Instance = text;
            CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }
    });

    private sealed class NoViews : IWidgetViews
    {
        public IDisposable Acquire(IWidgetDescriptor descriptor) => new TrackedResource();
    }

    private sealed class FakeUpdates : IUpdateService
    {
        public bool IsSupported => true;

        public UpdateStatus Status => UpdateStatus.Idle;

        public int Checks { get; private set; }

        public event EventHandler? StatusChanged
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public Task CheckNowAsync()
        {
            Checks++;
            return Task.CompletedTask;
        }

        public void ApplyPendingOnExit()
        {
        }

        public void Dispose()
        {
        }
    }
}
