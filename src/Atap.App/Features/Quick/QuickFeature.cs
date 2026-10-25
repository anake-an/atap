using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.Quick;
using Atap.Notch;

[assembly: AtapFeature(typeof(QuickDescriptor))]

namespace Atap.Features.Quick;

/// <summary>
/// Quick settings: a small panel with what people change most, from the icon by the clock or the notch's menu.
/// Also Pause alerts for 1 hour. Runs in safe mode too, since the tray icon is a way back to Settings.
/// </summary>
internal sealed class QuickDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "quick";
    public const string TrayIconKey = "tray.show";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(TrayIconKey, "quick.trayIcon", SettingType.Bool, true)
        {
            SectionId = SettingsSections.General,
            DescriptionKey = "quick.trayIconHint",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) => new QuickFeature(
        context.Settings, context.Notch, context.Commands, context.Scheduler, context.Time, context.Layout, context.Text, FeatureId,
        new QuickShell());
}

/// <summary>The windows the feature opens. Tests pass their own, so nothing appears on screen.</summary>
internal interface IQuickShell : IDisposable
{
    /// <summary>Shows or removes the icon by the clock.</summary>
    void SetTrayIcon(bool shown, Action open, Action menu);

    /// <summary>Opens the quick panel next to the pointer, or closes it when it is open.</summary>
    void ToggleQuickPanel(QuickPanelViewModel viewModel);

    /// <summary>Opens the tray icon's menu at the pointer.</summary>
    void ShowTrayMenu(IReadOnlyList<TrayMenuItem> items);
}

/// <summary>One line of the tray icon's menu. Null header means a separator.</summary>
internal sealed record TrayMenuItem(string? Header, Action? Run = null, bool IsChecked = false, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new(Header: null);
}

/// <summary>Holds the tray icon and the one-hour pause. Holds no timer otherwise.</summary>
internal sealed class QuickFeature(
    IAppSettings settings,
    INotchControl notch,
    ICommandRegistry commands,
    IScheduler scheduler,
    TimeProvider time,
    ILayoutService layout,
    ILocalizer text,
    string ownerId,
    IQuickShell shell) : IAppFeature
{
    public const string OpenCommand = "quick.open";
    public const string PauseAlertsCommand = "alerts.pause";

    public static readonly TimeSpan PauseFor = TimeSpan.FromHours(1);

    private readonly List<IDisposable> _registrations = [];
    private IDisposable? _resume;

    /// <summary>True while Pause alerts for 1 hour holds alerts quiet. Critical alerts still show.</summary>
    public bool AreAlertsPaused => _resume is not null;

    public Task StartAsync()
    {
        _registrations.Add(commands.Register(new AtapCommand(OpenCommand, "quick.command.open", _ =>
        {
            OpenQuickPanel();
            return Task.CompletedTask;
        })
        {
            MenuGroup = Features.SettingsWindow.SettingsWindowDescriptor.MenuGroup,
            MenuOrder = -90,
            KeywordsKey = "quick.keywords",
        }));
        _registrations.Add(commands.Register(new AtapCommand(PauseAlertsCommand, "quick.command.pauseAlerts", _ =>
        {
            SetAlertsPaused(!AreAlertsPaused);
            return Task.CompletedTask;
        })
        {
            MenuGroup = Features.NotchSize.NotchSizeFeature.MenuGroup,
            MenuOrder = 14,
            KeywordsKey = "quick.pauseKeywords",
            IsChecked = () => AreAlertsPaused,
        }));
        settings.Changed += OnSettingsChanged;
        ApplyTrayIcon();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;
        _resume?.Dispose();
        _resume = null;
        foreach (var registration in _registrations)
            registration.Dispose();
        _registrations.Clear();
        shell.Dispose();
        // The feature host clears the quiet flag once the feature has stopped.
        return ValueTask.CompletedTask;
    }

    /// <summary>Pauses alerts for an hour, or ends the pause. Critical alerts still show. The hour counts through sleep.</summary>
    public void SetAlertsPaused(bool paused)
    {
        _resume?.Dispose();
        _resume = paused
            ? scheduler.At(time.GetUtcNow() + PauseFor, () => SetAlertsPaused(false), new ScheduleOptions(Missed: MissedRun.RunOnce))
            : null;
        notch.SetFlag(NotchFlag.QuietAlerts, ownerId, paused);
    }

    public void OpenQuickPanel() =>
        shell.ToggleQuickPanel(new QuickPanelViewModel(settings, commands, layout, text, () => AreAlertsPaused, SetAlertsPaused));

    /// <summary>The tray icon's menu, built fresh each time so the ticks and words are current.</summary>
    public IReadOnlyList<TrayMenuItem> TrayMenu()
    {
        List<TrayMenuItem> items =
        [
            new(text["quick.command.open"], OpenQuickPanel, IsDefault: true),
            TrayMenuItem.Separator,
            Toggle(Features.NotchSize.NotchSizeFeature.HideForAnHourCommand, "quick.hideNotch", Features.NotchSize.NotchSizeFeature.ShowNowCommand),
            new(text["quick.command.pauseAlerts"], () => SetAlertsPaused(!AreAlertsPaused), AreAlertsPaused),
        ];
        if (Available(Features.Alerts.AlertsFeature.StopSoundCommand))
            items.Add(new(text["alerts.command.stopSound"], Run(Features.Alerts.AlertsFeature.StopSoundCommand)));
        items.Add(TrayMenuItem.Separator);
        items.Add(new(text["quick.menu.settings"], Run(AppCommands.OpenSettings)));
        if (Available(AppCommands.CheckForUpdates))
            items.Add(new(text["app.command.checkUpdates"], Run(AppCommands.CheckForUpdates)));
        items.Add(new(text["quick.menu.about"], Run(AppCommands.OpenSettings, Atap.Settings.Shell.SettingsPages.About)));
        items.Add(TrayMenuItem.Separator);
        items.Add(new(text["quick.menu.quit"], Run(AppCommands.Quit)));
        return items;
    }

    private TrayMenuItem Toggle(string onCommand, string titleKey, string offCommand)
    {
        var on = commands.All.FirstOrDefault(c => c.Id == onCommand)?.IsChecked?.Invoke() ?? false;
        return new(text[titleKey], Run(on ? offCommand : onCommand), on);
    }

    private bool Available(string id) =>
        commands.All.FirstOrDefault(c => c.Id == id) is { } command && (command.IsAvailable?.Invoke() ?? true);

    private Action Run(string id, string? argument = null) => () => _ = commands.TryRunAsync(id, argument);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(QuickDescriptor.TrayIconKey))
            ApplyTrayIcon();
    }

    private void ApplyTrayIcon() =>
        shell.SetTrayIcon(settings.Values.GetBool(QuickDescriptor.TrayIconKey), OpenQuickPanel, () => shell.ShowTrayMenu(TrayMenu()));
}
