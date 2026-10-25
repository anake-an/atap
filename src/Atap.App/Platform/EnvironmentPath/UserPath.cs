using Microsoft.Win32;

namespace Atap.Platform.EnvironmentPath;

/// <summary>
/// Adds and removes one folder in a PATH value. Folders compare without case and without a trailing slash, and
/// every other entry keeps its place and spelling, so adding and removing again gives back the same value.
/// </summary>
internal static class PathList
{
    public static bool Contains(string? path, string folder) => Entries(path).Any(e => Same(e, folder));

    public static string Add(string? path, string folder)
    {
        if (Contains(path, folder))
            return path ?? string.Empty;
        var trimmed = (path ?? string.Empty).TrimEnd(';');
        return trimmed.Length == 0 ? folder : $"{trimmed};{folder}";
    }

    public static string Remove(string? path, string folder)
    {
        if (!Contains(path, folder))
            return path ?? string.Empty;
        return string.Join(';', (path ?? string.Empty).Split(';').Where(e => !Same(e, folder) && e.Length > 0));
    }

    private static IEnumerable<string> Entries(string? path) =>
        (path ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Same(string entry, string folder) =>
        string.Equals(entry.Trim().TrimEnd('\\', '/'), folder.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>The signed-in user's own PATH. Tests pass their own.</summary>
internal interface IUserPath
{
    string? Read();

    void Write(string value);
}

/// <summary>
/// <c>HKCU\Environment\Path</c>. Writing keeps it expandable and tells running programs, so a new terminal sees it.
/// Never the machine-wide PATH.
/// </summary>
internal sealed class RegistryUserPath : IUserPath
{
    /// <summary>The folder with <c>atap.exe</c>, the same one uninstalling removes from PATH.</summary>
    public static string CliFolder => Composition.UninstallCleanup.CliFolder;

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment");
        key.SetValue("Path", value, RegistryValueKind.ExpandString);
        Composition.UninstallCleanupNative.BroadcastEnvironmentChange();
    }
}
