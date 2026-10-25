using System.IO;
using Atap.Diagnostics;
using Atap.Platform.Startup;
using Microsoft.Win32;

namespace Atap.Composition;

/// <summary>
/// Runs from Velopack's uninstall hook. It removes the <c>&lt;install&gt;\current\cli</c> entry that the
/// "Add atap to PATH" option may have put in the user's PATH, and the Start with Windows entry when it
/// points into this install, and nothing else.
/// </summary>
internal static class UninstallCleanup
{
    /// <summary>The folder of atap.exe, next to the running Atap.exe.</summary>
    public static string CliFolder => Path.Combine(AppContext.BaseDirectory, "cli");

    /// <summary>Never throws: an uninstall must go on even if the cleanup fails.</summary>
    public static void Run()
    {
        RemoveFromPath();
        RemoveFromStartup(new CurrentUserRunKey(), Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)));
    }

    /// <summary>
    /// Deletes the Run value when it starts an exe inside <paramref name="installRoot"/>, so a portable copy
    /// the user set up elsewhere keeps its entry. Never throws.
    /// </summary>
    /// <param name="key">The Run key.</param>
    /// <param name="installRoot">The Velopack root, the folder above <c>current</c>.</param>
    public static void RemoveFromStartup(IRunKey key, string? installRoot)
    {
        try
        {
            if (installRoot is not null && key.Get(RunKeyStartup.ValueName) is { } value
                && value.Trim().Trim('"').StartsWith(Path.TrimEndingDirectorySeparator(installRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                key.Delete(RunKeyStartup.ValueName);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not remove Atap from Windows startup during uninstall.", ex);
        }
    }

    private static void RemoveFromPath()
    {
        try
        {
            using var environment = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
            if (environment?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string path)
                return;

            var updated = RemoveEntry(path, CliFolder);
            if (updated is null)
                return;

            var kind = environment.GetValueKind("Path");
            environment.SetValue("Path", updated, kind == RegistryValueKind.String ? RegistryValueKind.String : RegistryValueKind.ExpandString);
            UninstallCleanupNative.BroadcastEnvironmentChange();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not remove atap from PATH during uninstall.", ex);
        }
    }

    /// <summary>
    /// Removes every entry equal to <paramref name="entry"/> (case-insensitive, trailing slashes ignored)
    /// from a PATH value. Entries are compared with their variables expanded, so
    /// <c>%LOCALAPPDATA%\Atap\current\cli</c> matches too. The entries that stay are kept as written.
    /// Returns null when there was none, so nothing is written.
    /// </summary>
    /// <param name="pathValue">The PATH value as stored, variables not expanded.</param>
    /// <param name="entry">The folder to take out.</param>
    /// <param name="expand">Expands variables such as %LOCALAPPDATA%. Defaults to the current environment.</param>
    public static string? RemoveEntry(string pathValue, string entry, Func<string, string>? expand = null)
    {
        expand ??= Environment.ExpandEnvironmentVariables;
        var target = Normalize(entry, expand);
        if (target.Length == 0)
            return null;

        var parts = pathValue.Split(';');
        var kept = parts.Where(p => !string.Equals(Normalize(p, expand), target, StringComparison.OrdinalIgnoreCase)).ToList();
        return kept.Count == parts.Length ? null : string.Join(';', kept);
    }

    private static string Normalize(string value, Func<string, string> expand) => Trim(expand(Trim(value)));

    private static string Trim(string value) => value.Trim().Trim('"').TrimEnd('\\', '/');
}
