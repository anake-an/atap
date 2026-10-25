using System.IO;
using Microsoft.Win32;

namespace Atap.Platform.Startup;

/// <summary>Starts Atap when the user signs in to Windows.</summary>
internal interface IStartupRegistration
{
    bool IsRegistered { get; }

    /// <summary>Adds Atap, or points the entry at this copy of Atap. Does nothing when it already does.</summary>
    void Register();

    /// <summary>Adds Atap when there is no entry, or points it here when its exe is gone. Otherwise leaves it.</summary>
    void Repair();

    /// <summary>Removes Atap. Does nothing when it is not there.</summary>
    void Unregister();
}

/// <summary>A registry key of named string values, so tests never touch the real Run key.</summary>
internal interface IRunKey
{
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

/// <summary>A key under HKEY_CURRENT_USER, created on the first write.</summary>
internal sealed class CurrentUserRunKey(string subKey = RunKeyStartup.RunKeyPath) : IRunKey
{
    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// Start with Windows for the installer, portable and Scoop builds: one value in the current user's
/// Run key. Installed builds point it at the Velopack stub, which keeps working across updates.
/// </summary>
/// <param name="key">The Run key.</param>
/// <param name="exePath">The exe to start.</param>
/// <param name="fileExists">Tells whether a file exists. <see cref="File.Exists(string)"/> when null.</param>
internal sealed class RunKeyStartup(IRunKey key, string exePath, Func<string, bool>? fileExists = null) : IStartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Atap";

    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    /// <summary>The value written: the quoted path of the exe to start.</summary>
    public string Command { get; } = $"\"{exePath}\"";

    public bool IsRegistered => string.Equals(key.Get(ValueName), Command, StringComparison.OrdinalIgnoreCase);

    public void Register()
    {
        if (!IsRegistered)
            key.Set(ValueName, Command);
    }

    public void Repair()
    {
        var current = key.Get(ValueName);
        if (current is null || !_fileExists(current.Trim().Trim('"')))
            key.Set(ValueName, Command);
    }

    public void Unregister()
    {
        if (key.Get(ValueName) is not null)
            key.Delete(ValueName);
    }

    /// <summary>
    /// The exe Windows should start. Velopack installs to &lt;root&gt;\current\Atap.exe with a stub at
    /// &lt;root&gt;\Atap.exe that starts the current version, so the stub is used when it is there.
    /// </summary>
    /// <param name="processPath">The running Atap.exe.</param>
    /// <param name="fileExists">Tells whether a file exists.</param>
    public static string LaunchPath(string processPath, Func<string, bool> fileExists)
    {
        var folder = Path.GetDirectoryName(processPath);
        if (folder is not null && string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(folder) is { } root)
        {
            var stub = Path.Combine(root, Path.GetFileName(processPath));
            if (fileExists(stub))
                return stub;
        }
        return processPath;
    }
}

/// <summary>Picks how Start with Windows works in this build.</summary>
internal static class WindowsStartup
{
    /// <summary>
    /// False in Store builds. A packaged app starts at sign-in only through a StartupTask declared in its
    /// package manifest, which the Store package does not have yet, so the option is hidden there.
    /// </summary>
    public static bool IsSupported =>
#if STORE
        false;
#else
        true;
#endif

    /// <summary>The Run key entry for the running Atap.exe.</summary>
    public static IStartupRegistration CreateDefault() =>
        new RunKeyStartup(new CurrentUserRunKey(), RunKeyStartup.LaunchPath(AtapExePath(), File.Exists));

    /// <summary>The running Atap.exe. Started through the dotnet host the process is dotnet.exe, so the app folder is used.</summary>
    private static string AtapExePath() =>
        Environment.ProcessPath is { } path && string.Equals(Path.GetFileName(path), "Atap.exe", StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.Combine(AppContext.BaseDirectory, "Atap.exe");
}
