using System.Globalization;
using System.Reflection;
using System.Windows.Threading;
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
using Atap.Diagnostics;
using Atap.Notch;
using Atap.Platform.Foreground;
using Atap.Platform.Network;
using Atap.Platform.Power;
using Atap.Platform.Secrets;
using Atap.Updates;

namespace Atap.Composition;

/// <summary>
/// The app's services, created once at startup in a fixed order: files, text, core services, Windows
/// state, the catalogs, then the widget and notch hosts. There is no container: every service is a
/// plain property. Disposed in reverse order at shutdown, after the app has stopped the hosts.
/// </summary>
internal sealed class AppServices : IDisposable
{
    /// <summary>The setting that picks the UI language: "system", "en" or "ms".</summary>
    public const string LanguageChoiceKey = "language.choice";

    /// <summary>True once onboarding is done or skipped. The app sets it for upgraded installs, so they never see the wizard.</summary>
    public const string OnboardingDoneKey = "onboarding.done";

    private AppServices()
    {
    }

    public required AppMode Mode { get; init; }

    public required AtapPaths Paths { get; init; }

    public required SynchronizationContext Ui { get; init; }

    public required TimeProvider Time { get; init; }

    public required SettingsStore Settings { get; init; }

    public required LayoutStore Layout { get; init; }

    public required Localizer Text { get; init; }

    public required Scheduler Scheduler { get; init; }

    public required PowerState Power { get; init; }

    public required NetworkState Network { get; init; }

    public required ForegroundState Foreground { get; init; }

    public required AlertQueue Alerts { get; init; }

    public required CommandRegistry Commands { get; init; }

    public required SharedServices Shared { get; init; }

    /// <summary>Its HTTP client is created on the first request only.</summary>
    public required HttpDataService Http { get; init; }

    public required DpapiSecretStore Secrets { get; init; }

    public required NotchControl Notch { get; init; }

    public required WidgetCatalog Catalog { get; init; }

    public required IReadOnlyList<IFeatureDescriptor> Features { get; init; }

    /// <summary>Registered features that could not be used. The app logs them.</summary>
    public required IReadOnlyList<string> FeatureProblems { get; init; }

    /// <summary>Settings card types, created only while the settings window is open.</summary>
    public required IReadOnlyList<Type> SettingsCards { get; init; }

    /// <summary>Self-updates. Null in Store builds, where the Store updates Atap.</summary>
    public required IUpdateService? Updates { get; init; }

    /// <summary>The widget instances. The app applies the layout to it and starts it. Others use it as <see cref="IWidgetHostView"/>.</summary>
    public WidgetHost Widgets { get; private set; } = null!;

    /// <summary>The notch windows, the monitors and the placement. The app starts it once the widgets exist.</summary>
    public NotchHost NotchHost { get; private set; } = null!;

    /// <summary>Must run on the UI thread, before any window opens.</summary>
    /// <param name="dispatcher">The UI thread.</param>
    /// <param name="mode">Normal, or Safe after a crash.</param>
    /// <param name="appAssembly">Where the widgets, features and cards register.</param>
    /// <param name="paths">Where files live.</param>
    /// <param name="views">Merges a widget type's views while it has instances.</param>
    /// <param name="updates">Null in Store builds.</param>
    public static AppServices Create(Dispatcher dispatcher, AppMode mode, Assembly appAssembly, AtapPaths paths, IWidgetViews views, IUpdateService? updates)
    {
        var ui = new DispatcherSynchronizationContext(dispatcher);
        var time = TimeProvider.System;

        // Files first: settings, then layout.
        var settings = new SettingsStore(paths.SettingsFile, time, ui);
        settings.Load();
        var layout = new LayoutStore(paths.LayoutFile, timeProvider: time, ui: ui);
        layout.Load();

        // Text, and the UI culture, before any window opens. CurrentCulture keeps the Windows regional format.
        var language = Localizer.ResolveChoice(settings.Values.GetString(LanguageChoiceKey), CultureInfo.CurrentUICulture);
        var text = new Localizer(new AssemblyStringResources(appAssembly), language);
        Localizer.Instance = text;
        CultureInfo.CurrentUICulture = text.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = text.Culture;

        var scheduler = new Scheduler(time, ui);
        var network = new NetworkState();
        var http = new HttpDataService(AppInfo.Version, network);
        var featureProblems = new List<string>();
        var features = Discovery.Features(appAssembly, featureProblems);
        settings.Register(features.SelectMany(f => f.Settings));
        // After the feature settings, so the saver.* settings have their definitions.
        var power = new PowerState(new WindowsPowerReadings(ui), settings);

        var services = new AppServices
        {
            Mode = mode,
            Paths = paths,
            Ui = ui,
            Time = time,
            Settings = settings,
            Layout = layout,
            Text = text,
            Scheduler = scheduler,
            Power = power,
            Network = network,
            Foreground = new ForegroundState(),
            Alerts = new AlertQueue(time, ui),
            Commands = new CommandRegistry(),
            Shared = new SharedServices(ui, time, scheduler),
            Http = http,
            Secrets = new DpapiSecretStore(),
            Notch = new NotchControl(),
            Catalog = Discovery.Widgets(appAssembly),
            Features = features,
            FeatureProblems = featureProblems,
            SettingsCards = Discovery.SettingsCards(appAssembly),
            Updates = updates,
        };

        // Alarms and other widgets that need no placement keep running in a profile with its own widget list.
        layout.WorksWithoutPlacement = id => services.Catalog.TryResolve(id, out var descriptor) && descriptor.WorksWithoutPlacement;

        // Instances are created by ApplyAsync, and the windows only by NotchHost.Start.
        services.Widgets = new WidgetHost(
            services.Catalog,
            services.CreateWidgetContext,
            views,
            (id, descriptor) => new SettingValues(descriptor.Settings, layout.Document.GetWidgetSettings(id)),
            time,
            scheduler,
            ui,
            (message, ex) => Log.Warn(message, ex));
        services.NotchHost = new NotchHost(layout, services.Widgets, services.Notch, services.Foreground, dispatcher);

        // Quiet alerts follow the notch flag a rule or feature sets.
        services.Notch.Changed += (_, _) => services.Alerts.SetQuiet("notch", services.Notch.IsSet(NotchFlag.QuietAlerts));

        // Battery saver reaches the scheduler and HTTP. The power source raises Changed when the policy result changes.
        void FollowSaver()
        {
            scheduler.SaverActive = power.SaverActive;
            http.SaverActive = power.SaverActive;
        }
        power.Changed += (_, _) => FollowSaver();
        FollowSaver();
        return services;
    }

    /// <summary>The services one widget instance gets. Scheduler, commands and leases are scoped to its lifetime.</summary>
    public WidgetContext CreateWidgetContext(
        string instanceId, IWidgetDescriptor descriptor, OwnerLifetime lifetime, IObservableValue<WidgetVisibility> visibility) => new()
    {
        InstanceId = instanceId,
        Descriptor = descriptor,
        Ui = Ui,
        Time = Time,
        Scheduler = new ScopedScheduler(Scheduler, lifetime),
        Http = Http.For(descriptor.Capabilities),
        Text = Text,
        Alerts = Alerts.For(instanceId),
        Commands = new ScopedCommands(Commands, lifetime),
        Shared = new ScopedShared(Shared, lifetime),
        App = Settings,
        Power = Power,
        Network = Network,
        Secrets = Secrets,
        Storage = new WidgetStorage(instanceId, Paths),
        Visibility = visibility,
        Lifetime = lifetime,
    };

    /// <summary>The services one feature gets. Scheduler, commands and leases are scoped to its lifetime.</summary>
    public FeatureContext CreateFeatureContext(IFeatureDescriptor descriptor, OwnerLifetime lifetime) => new()
    {
        FeatureId = descriptor.Id,
        Mode = Mode,
        Ui = Ui,
        Time = Time,
        Scheduler = new ScopedScheduler(Scheduler, lifetime),
        SchedulerControl = Scheduler,
        Http = Http,
        Text = Text,
        Commands = new ScopedCommands(Commands, lifetime),
        Shared = new ScopedShared(Shared, lifetime),
        Power = Power,
        Network = Network,
        Secrets = Secrets,
        Settings = Settings,
        Layout = Layout,
        Foreground = Foreground,
        Alerts = Alerts,
        Notch = Notch,
        Widgets = Widgets,
        SettingsCards = SettingsCards,
        Services = this,
        Paths = Paths,
        Lifetime = lifetime,
    };

    /// <summary>
    /// The hosts are not disposed here: the app stops features, the notch and the widgets first. The stores go
    /// first, so changes still waiting are written while everything else is still up.
    /// </summary>
    public void Dispose()
    {
        Layout.Dispose();
        Settings.Dispose();
        Shared.Dispose();
        Http.Dispose();
        Scheduler.Dispose();
        Foreground.Dispose();
        Network.Dispose();
        Power.Dispose();
    }
}
