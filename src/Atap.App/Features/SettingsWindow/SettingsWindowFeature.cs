using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Widgets;
using Atap.Diagnostics;
using Atap.Features.SettingsWindow;
using Atap.Settings.Shell;

[assembly: AtapFeature(typeof(SettingsWindowDescriptor))]

namespace Atap.Features.SettingsWindow;

/// <summary>
/// Settings: the "settings.open" command, in the notch menu and the command bar, and run by a second
/// launch. Always on, also in safe mode. It holds no window until the command runs.
/// </summary>
internal sealed class SettingsWindowDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "settingsWindow";

    /// <summary>The notch menu group. Its low order puts Settings at the top of the menu's commands.</summary>
    public const string MenuGroup = "settings";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings => [];

    public IReadOnlyCollection<string> EnabledBy => [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) =>
        new SettingsWindowFeature(context.Commands, page => SettingsSession.Open(context.Services, page, Log.Warn));
}

/// <summary>Opens one settings window at a time. Running the command again brings it to the front.</summary>
internal sealed class SettingsWindowFeature : IAppFeature
{
    private readonly ICommandRegistry _commands;
    private readonly Func<string?, ISettingsSession> _open;
    private ISettingsSession? _session;

    /// <param name="commands">Scoped to the feature, so the command goes when it stops.</param>
    /// <param name="open">Opens a window on a page id or section id, or Home for null.</param>
    public SettingsWindowFeature(ICommandRegistry commands, Func<string?, ISettingsSession> open)
    {
        _commands = commands;
        _open = open;
    }

    /// <summary>The open window, or null.</summary>
    internal ISettingsSession? Session => _session;

    public Task StartAsync()
    {
        _commands.Register(new AtapCommand(AppCommands.OpenSettings, "settingswindow.command.open", argument =>
        {
            Open(argument);
            return Task.CompletedTask;
        })
        {
            KeywordsKey = "settingswindow.command.keywords",
            MenuGroup = SettingsWindowDescriptor.MenuGroup,
            MenuOrder = -100,
        });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _session?.Close();
        _session = null;
        return ValueTask.CompletedTask;
    }

    private void Open(string? pageOrSection)
    {
        if (_session is { } open)
        {
            open.Show(pageOrSection);
            return;
        }
        var session = _open(pageOrSection);
        session.Closed += (_, _) =>
        {
            if (ReferenceEquals(_session, session))
                _session = null;
        };
        _session = session;
    }
}
