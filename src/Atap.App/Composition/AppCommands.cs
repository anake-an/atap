using Atap.Core.Commands;
using Atap.Updates;

namespace Atap.Composition;

/// <summary>
/// Commands the app itself owns. They are registered once at startup, so the tray, the command bar and
/// hotkeys reach Quit and the update check by id, like any feature command.
/// </summary>
internal static class AppCommands
{
    public const string Quit = "app.quit";

    /// <summary>Not registered in Store builds, where the Store updates Atap.</summary>
    public const string CheckForUpdates = "updates.check";

    /// <summary>Registered by the settings window, not here. A second launch runs it.</summary>
    public const string OpenSettings = SecondInstance.CommandId;

    /// <summary>Registered by the notch size feature: ends Hide for 1 hour. A second launch runs it first.</summary>
    public const string ShowNotch = Features.NotchSize.NotchSizeFeature.ShowNowCommand;

    /// <param name="commands">Where to register. The commands last as long as the app.</param>
    /// <param name="quit">Starts the shutdown. It should run after the command returns, so the caller is not disposed mid-call.</param>
    /// <param name="updates">Null in Store builds.</param>
    public static void Register(ICommandRegistry commands, Action quit, IUpdateService? updates)
    {
        commands.Register(new AtapCommand(Quit, "app.command.quit", _ =>
        {
            quit();
            return Task.CompletedTask;
        }));

        if (updates is null)
            return;
        commands.Register(new AtapCommand(CheckForUpdates, "app.command.checkUpdates", _ => updates.CheckNowAsync())
        {
            // Also while an update is ready, because pressing it then installs the update at once.
            IsAvailable = () => updates is { IsSupported: true, Status: UpdateStatus.Idle or UpdateStatus.UpToDate or UpdateStatus.ReadyToInstall },
        });
    }
}
