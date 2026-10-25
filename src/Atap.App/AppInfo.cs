using System.Reflection;

namespace Atap;

internal static class AppInfo
{
    /// <summary>The app version from the informational version, without build metadata, for example "0.1.0-preview.1".</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Who installed Atap, from where it runs. Scoop installs are updated by Scoop, not by Atap.</summary>
    public static Core.Updates.InstallSource InstallSource { get; } = Core.Updates.InstallSources.Detect(
        Environment.ProcessPath, Environment.GetEnvironmentVariable("SCOOP"), Environment.GetEnvironmentVariable("SCOOP_GLOBAL"));

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        // The SDK appends "+<commit>" metadata; it is noise in a menu.
        var metadata = informational.IndexOf('+', StringComparison.Ordinal);
        return metadata >= 0 ? informational[..metadata] : informational;
    }
}
