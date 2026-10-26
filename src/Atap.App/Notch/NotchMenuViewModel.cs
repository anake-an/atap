using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Commands;
using Atap.Core.Localization;
using Atap.Core.Placement;
using Atap.Diagnostics;
using Atap.Updates;

namespace Atap.Notch;

/// <summary>
/// The right-click menu, shared by every notch window: version, position, monitor, every registered
/// command that has a menu group, updates and quit. Placement choices are saved to layout.json right
/// away by the host. All text follows the UI language.
/// </summary>
public sealed class NotchMenuViewModel : ObservableObject
{
    /// <summary>Hides the "coming soon" item once the layout editor registers this command.</summary>
    public const string EditLayoutCommandId = "layout.edit";

    internal static readonly (NotchAnchor Anchor, string Key)[] Anchors =
    [
        (NotchAnchor.TopLeft, "notch.anchor.topLeft"),
        (NotchAnchor.TopCenter, "notch.anchor.topCenter"),
        (NotchAnchor.TopRight, "notch.anchor.topRight"),
        (NotchAnchor.LeftCenter, "notch.anchor.leftCenter"),
        (NotchAnchor.RightCenter, "notch.anchor.rightCenter"),
    ];

    private readonly NotchHost _host;
    private readonly IUpdateService? _updates;
    private readonly ICommandRegistry _commands;
    private readonly ILocalizer _text;
    private IReadOnlyList<MenuOptionViewModel> _positionOptions = [];
    private IReadOnlyList<MenuOptionViewModel> _monitorOptions = [];

    internal NotchMenuViewModel(NotchHost host, IUpdateService? updates, ICommandRegistry commands, ILocalizer text)
    {
        _host = host;
        _updates = updates;
        _commands = commands;
        _text = text;

        QuitCommand = new RelayCommand(() => QuitRequested?.Invoke(this, EventArgs.Empty));
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, CanCheckForUpdates);

        _host.Changed += (_, _) => RebuildOptions();
        _commands.Changed += (_, _) => OnPropertyChanged(nameof(ShowEditLayoutPlaceholder));
        _text.LanguageChanged += (_, _) => OnLanguageChanged();
        if (_updates is not null)
            _updates.StatusChanged += (_, _) => OnUpdateStatusChanged();

        RebuildOptions();
    }

    public event EventHandler? QuitRequested;

    /// <summary>"Atap 0.1.0", shown as a disabled header.</summary>
    public string VersionHeader => _text.Format("notch.menu.version", ("version", AppInfo.Version));

    public IReadOnlyList<MenuOptionViewModel> PositionOptions
    {
        get => _positionOptions;
        private set => SetProperty(ref _positionOptions, value);
    }

    public IReadOnlyList<MenuOptionViewModel> MonitorOptions
    {
        get => _monitorOptions;
        private set => SetProperty(ref _monitorOptions, value);
    }

    /// <summary>True until a layout editor registers <see cref="EditLayoutCommandId"/>.</summary>
    public bool ShowEditLayoutPlaceholder => !_commands.Contains(EditLayoutCommandId);

    /// <summary>False in Store builds, where the Store handles updates.</summary>
    public bool ShowUpdateItem => _updates is not null;

    public string UpdateHeader => _text[_updates?.Status switch
    {
        UpdateStatus.Checking => "notch.update.checking",
        UpdateStatus.Downloading => "notch.update.downloading",
        UpdateStatus.ReadyToInstall => "notch.update.ready",
        UpdateStatus.Installing => "notch.update.installing",
        UpdateStatus.UpToDate => "notch.update.upToDate",
        _ => "notch.update.check",
    }];

    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    public IRelayCommand QuitCommand { get; }

    /// <summary>
    /// Registered commands with a menu group, built when the menu opens. Groups are separated in the menu
    /// and sorted by their lowest <see cref="AtapCommand.MenuOrder"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<MenuOptionViewModel>> BuildCommandGroups() =>
        _commands.All
            .Where(c => c.MenuGroup is not null)
            .GroupBy(c => c.MenuGroup!, StringComparer.Ordinal)
            .OrderBy(g => g.Min(c => c.MenuOrder))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (IReadOnlyList<MenuOptionViewModel>)g
                .OrderBy(c => c.MenuOrder)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .Select(ToOption)
                .ToList())
            .ToList();

    private MenuOptionViewModel ToOption(AtapCommand command) => new(
        EscapeAccessKeys(_text[command.TitleKey]),
        command.IsChecked?.Invoke() ?? false,
        new AsyncRelayCommand(() => RunAsync(command.Id)),
        command.IsAvailable?.Invoke() ?? true);

    private async Task RunAsync(string id)
    {
        try
        {
            await _commands.TryRunAsync(id);
        }
        catch (Exception ex)
        {
            Log.Warn($"Command '{id}' failed.", ex);
        }
    }

    private bool CanCheckForUpdates() =>
        _updates is { IsSupported: true, Status: UpdateStatus.Idle or UpdateStatus.UpToDate or UpdateStatus.ReadyToInstall };

    private Task CheckForUpdatesAsync() => _updates?.CheckNowAsync() ?? Task.CompletedTask;

    private void OnUpdateStatusChanged()
    {
        OnPropertyChanged(nameof(UpdateHeader));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(VersionHeader));
        OnPropertyChanged(nameof(UpdateHeader));
        RebuildOptions();
    }

    private void RebuildOptions()
    {
        var placement = _host.Placement;
        PositionOptions = Anchors
            .Select(a => new MenuOptionViewModel(_text[a.Key], a.Anchor == placement.Anchor, new RelayCommand(() => _host.SetAnchor(a.Anchor))))
            .ToList();

        var selection = placement.Monitor;
        var monitors = _host.Monitors;
        var options = new List<MenuOptionViewModel>
        {
            new(_text["notch.monitor.primary"], selection.Mode == MonitorMode.Primary, new RelayCommand(() => _host.SetMonitorSelection(MonitorSelection.Primary()))),
        };

        foreach (var monitor in monitors)
        {
            var isChosen = selection.Mode == MonitorMode.Specific
                && string.Equals(selection.DeviceId, monitor.DeviceId, StringComparison.OrdinalIgnoreCase);
            options.Add(new(
                EscapeAccessKeys(monitor.FriendlyName),
                isChosen,
                new RelayCommand(() => _host.SetMonitorSelection(MonitorSelection.Specific(monitor)))));
        }

        // The chosen monitor is unplugged: the notch is on the primary for now, but keep the choice visible.
        if (selection.Mode == MonitorMode.Specific && MonitorResolver.Find(selection.DeviceId, monitors) is null)
        {
            var name = string.IsNullOrWhiteSpace(selection.FriendlyName) ? _text["notch.monitor.chosen"] : selection.FriendlyName;
            options.Add(new(EscapeAccessKeys(_text.Format("notch.monitor.notConnected", ("name", name))), IsChecked: true, Command: null, IsEnabled: false));
        }

        options.Add(new(_text["notch.monitor.all"], selection.Mode == MonitorMode.All, new RelayCommand(() => _host.SetMonitorSelection(MonitorSelection.All()))));
        MonitorOptions = options;
    }

    // Menu headers treat "_" as an access-key marker; monitor names and command titles are plain text.
    private static string EscapeAccessKeys(string text) => text.Replace("_", "__", StringComparison.Ordinal);
}
