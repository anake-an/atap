namespace Atap.Core.Commands;

/// <summary>
/// A named action, for example "media.playPause". Menus, hotkeys, gestures and the command bar run
/// actions only through their ids.
/// </summary>
/// <param name="Id">Unique id, "&lt;area&gt;.&lt;action&gt;".</param>
/// <param name="TitleKey">String key of the title in menus and the command bar.</param>
/// <param name="Run">The action. Its argument comes from the caller, for example the command bar text.</param>
public sealed record AtapCommand(string Id, string TitleKey, Func<string?, Task> Run)
{
    /// <summary>String key of extra search words for the command bar.</summary>
    public string? KeywordsKey { get; init; }

    /// <summary>Shows the command in the notch menu, grouped with others of the same group.</summary>
    public string? MenuGroup { get; init; }

    /// <summary>Sorts menu items, within and across groups. Lowest first.</summary>
    public int MenuOrder { get; init; }

    public bool ShowInCommandBar { get; init; } = true;

    /// <summary>False greys the command out and makes it do nothing.</summary>
    public Func<bool>? IsAvailable { get; init; }

    /// <summary>Shows a check mark in menus.</summary>
    public Func<bool>? IsChecked { get; init; }
}

/// <summary>The only list of named actions. Use it on the UI thread.</summary>
public interface ICommandRegistry
{
    /// <summary>Adds a command. Dispose the result to remove it. Throws when the id is taken.</summary>
    IDisposable Register(AtapCommand command);

    bool Contains(string id);

    /// <summary>Runs a command. False when it is not registered or not available.</summary>
    Task<bool> TryRunAsync(string id, string? argument = null);

    IReadOnlyCollection<AtapCommand> All { get; }

    event EventHandler? Changed;
}
