using System.Text.Json;
using System.Windows;
using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Storage;
using Atap.Diagnostics;
using Atap.Notch;
using Atap.Updates;
using Atap.Widgets.Script;

namespace Atap;

/// <summary>Started from <see cref="Program.Main"/>, which runs Velopack and the single-instance guard first.</summary>
public partial class App : Application
{
    private AppServices? _services;
    private FeatureHost? _features;
    private SecondInstance? _secondInstance;
    private IUpdateService? _updates;
    private Ui.Theme.HighContrastTheme? _highContrast;
    private Dictionary<string, string> _widgetSettings = new(StringComparer.Ordinal);
    private bool _quitting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Before any window, so everything draws in high contrast colours from the first frame when it is on.
        _highContrast = new Ui.Theme.HighContrastTheme(Resources, Ui.Controls.Contrast.Current);

        AppServices services;
        WidgetHost widgets;
        FeatureHost features;
        try
        {
            var paths = AtapPaths.Default;
            var mode = SafeMode.ConsumeMarker(paths) ? AppMode.Safe : AppMode.Normal;
            SafeMode.Current = mode;

            // Files, text, core services, catalogs and the hosts. Only descriptors exist so far, and no window.
            _updates = CreateUpdateService();
            services = AppServices.Create(Dispatcher, mode, typeof(App).Assembly, paths, new WidgetViewLoader(Resources), _updates);
            _services = services;
            LogStartupProblems(services);
            MarkUpgradedInstall(services);
            // Covers removals made while Atap was closed, such as from a synced layout.
            ForgetRemovedScripts(services);

            // A second launch opens Settings, through a command the settings window registers.
            ListenForSecondLaunch(services);

            // Enabled instances are created now, so the windows show them; they start after the windows are up.
            widgets = services.Widgets;
            await widgets.ApplyAsync(EnabledWidgets());
            _widgetSettings = SnapshotWidgetSettings();
            services.Layout.Changed += OnLayoutChanged;

            AppCommands.Register(services.Commands, () => _ = Dispatcher.InvokeAsync(QuitAsync), _updates);
            var menu = new NotchMenuViewModel(services.NotchHost, _updates, services.Commands, services.Text);
            menu.QuitRequested += async (_, _) => await QuitAsync();
            services.NotchHost.Start(menu);

            features = new FeatureHost(
                services.Features,
                services.Settings,
                mode,
                services.CreateFeatureContext,
                services.Notch.ClearOwner,
                (message, ex) => Log.Warn(message, ex));
            _features = features;
        }
        catch (Exception ex)
        {
            // Exit rather than linger invisibly, which would also block the next launch.
            Log.Error("Atap failed to start.", ex);
            Shutdown(1);
            return;
        }

        // Each feature and widget catches its own start errors; one broken part should not take the notch down.
        await features.StartAsync();
        await widgets.StartAsync();

        if (services.Mode == AppMode.Safe)
        {
            Log.Warn("Atap started in safe mode after a crash.");
            services.Alerts.For("app").Show(new Alert("safeMode", AlertPriority.High, services.Text["app.safeMode.title"])
            {
                Subtitle = services.Text["app.safeMode.subtitle"],
                Tone = AlertTone.Warning,
            });
        }

        _updates?.Start();
    }

    /// <summary>
    /// Windows is signing out or shutting down and may end the process before Quit runs, so changes still
    /// waiting are written now.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _services?.Settings.Flush();
        _services?.Layout.Flush();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Runs on Quit and when Windows signs out. A downloaded update installs once this process is gone.
        _updates?.ApplyPendingOnExit();
        _updates?.Dispose();
        _highContrast?.Dispose();
        base.OnExit(e);
    }

    private IUpdateService? CreateUpdateService() =>
#if STORE
        null; // Store builds are updated by the Store.
#else
        new UpdateService(Dispatcher);
#endif

    /// <summary>The instances to run: the active profile's, or the clock alone in safe mode.</summary>
    private IReadOnlyList<string> EnabledWidgets() =>
        _services is { Mode: AppMode.Safe } ? SafeMode.Widgets : _services?.Layout.Active.EnabledWidgets ?? [];

    /// <summary>
    /// A layout change applies as a diff, and only instances whose settings changed restart. The host
    /// queues each apply behind the one before, so quick changes run in order.
    /// </summary>
    private async void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (_quitting || _services is null)
            return;
        var widgets = _services.Widgets;
        try
        {
            var settings = SnapshotWidgetSettings();
            foreach (var (id, json) in settings)
            {
                if (_widgetSettings.TryGetValue(id, out var before) && before != json)
                    widgets.RestartSoon(id);
            }
            _widgetSettings = settings;
            await widgets.ApplyAsync(EnabledWidgets());
            // After the apply, so a removed script instance has stopped before its approval goes.
            ForgetRemovedScripts(_services);
        }
        catch (Exception ex)
        {
            Log.Warn("The layout change could not be applied to the widgets.", ex);
        }
    }

    /// <summary>
    /// A script instance that is no longer in the layout loses its program, approval and key. Ids are reused, so without
    /// this a new script widget, or a synced layout, with the same id would run the old program with no prompt.
    /// Only script folders are touched. Never throws.
    /// </summary>
    private static void ForgetRemovedScripts(AppServices services)
    {
        try
        {
            // A layout that could not be read is not the real list, so nothing is forgotten from it.
            if (services.Layout.IsReadOnly)
                return;
            var keep = ScriptStore.InstancesIn(services.Layout.Document);
            foreach (var id in ScriptStore.ForgetRemoved(services.Paths.MachineState, keep, services.Secrets))
                Log.Info($"Forgot the program and approval of removed widget '{id}'.");
        }
        catch (Exception ex)
        {
            Log.Warn("Removed script widgets could not be cleaned up.", ex);
        }
    }

    private Dictionary<string, string> SnapshotWidgetSettings()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_services is null)
            return snapshot;
        foreach (var id in _services.Widgets.InstanceIds)
            snapshot[id] = JsonSerializer.Serialize(_services.Layout.Document.GetWidgetSettings(id));
        return snapshot;
    }

    /// <summary>
    /// An existing layout.json with no settings.json is an install from before settings existed, so the
    /// onboarding wizard is marked done. A new install records "not done" now, so a later start without
    /// the wizard finished is not mistaken for an upgrade.
    /// </summary>
    private static void MarkUpgradedInstall(AppServices services)
    {
        if (services.Settings.FileExisted)
            return;
        var upgraded = services.Layout.LastLoadOutcome != FileLoadOutcome.CreatedDefaults;
        services.Settings.Set(AppServices.OnboardingDoneKey, upgraded);
    }

    private void ListenForSecondLaunch(AppServices services)
    {
        try
        {
            // Starting Atap again also ends Hide for 1 hour, so a hidden notch is never out of reach.
            _secondInstance = SecondInstance.Listen(services.Ui, () => _ = RunSecondLaunchAsync());
        }
        catch (Exception ex)
        {
            // For example another account in this session made the event first and shut this user out.
            // Only the second-launch signal is lost: a second launch then just exits.
            Log.Warn("A second launch cannot open Settings in this session.", ex);
        }
    }

    private async Task RunSecondLaunchAsync()
    {
        await RunCommandAsync(AppCommands.ShowNotch);
        await RunCommandAsync(AppCommands.OpenSettings);
    }

    private async Task RunCommandAsync(string id)
    {
        try
        {
            await (_services?.Commands.TryRunAsync(id) ?? Task.FromResult(false));
        }
        catch (Exception ex)
        {
            Log.Warn($"Command '{id}' failed.", ex);
        }
    }

    private static void LogStartupProblems(AppServices services)
    {
        LogLoadOutcome("layout.json", services.Layout.LastLoadOutcome);
        LogLoadOutcome("settings.json", services.Settings.LastLoadOutcome);
        foreach (var problem in services.Catalog.Problems)
            Log.Warn($"Widget type '{problem.Source}' was left out: {problem.Reason}.", problem.Exception);
        foreach (var problem in services.FeatureProblems)
            Log.Warn(problem);

        services.Text.MissingKey += (_, key) => Log.Warn($"Missing text for '{key}'.");
        services.Scheduler.JobFailed += (_, fault) => Log.Warn("A scheduled item failed.", fault.Exception);
        services.Shared.DisposeFailed += (_, ex) => Log.Warn("A shared source failed to stop.", ex);
    }

    private static void LogLoadOutcome(string file, FileLoadOutcome outcome)
    {
        switch (outcome)
        {
            case FileLoadOutcome.Migrated:
                Log.Info($"{file} was upgraded to the current format. The old file was kept as a .bak.json copy.");
                break;
            case FileLoadOutcome.RecoveredFromCorrupt:
                Log.Warn($"{file} could not be parsed; it was backed up and replaced with defaults.");
                break;
            case FileLoadOutcome.Unreadable:
                Log.Warn($"{file} could not be read or backed up; it is left alone and not written this session.");
                break;
            case FileLoadOutcome.NewerVersionReadOnly:
                Log.Warn($"{file} is from a newer Atap; it is used read-only and never written.");
                break;
        }
    }

    /// <summary>Shuts down in order: features, notch windows, widgets, then services.</summary>
    private async Task QuitAsync()
    {
        if (_quitting)
            return;
        _quitting = true;

        if (_services is not null)
            _services.Layout.Changed -= OnLayoutChanged;
        _secondInstance?.Dispose();

        if (_features is not null)
            await _features.DisposeAsync();
        if (_services is not null)
        {
            _services.NotchHost.Dispose();
            await _services.Widgets.DisposeAsync();
            _services.Dispose();
        }

        Shutdown();
    }
}
