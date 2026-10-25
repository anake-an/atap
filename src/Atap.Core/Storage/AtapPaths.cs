namespace Atap.Core.Storage;

/// <summary>
/// Atap's folders. Roaming files (settings, layout, user data) live in %APPDATA%\Atap. Machine files
/// (state, secrets, cache, markers) live in %LOCALAPPDATA%\AtapData, outside Velopack's install root
/// at %LOCALAPPDATA%\Atap. Nothing here creates a folder.
/// </summary>
public sealed record AtapPaths(string Roaming, string Local)
{
    public static AtapPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Atap"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtapData"));

    public string LayoutFile => Path.Combine(Roaming, "layout.json");

    public string SettingsFile => Path.Combine(Roaming, "settings.json");

    public string UserData => Path.Combine(Roaming, "data");

    public string MachineState => Path.Combine(Local, "state");

    public string Cache => Path.Combine(Local, "cache");

    public string SecretsFile => Path.Combine(Local, "secrets.json");

    public string CrashMarker => Path.Combine(Local, "crash.marker");
}
