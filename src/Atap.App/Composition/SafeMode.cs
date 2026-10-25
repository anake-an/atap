using System.IO;
using Atap.Core.Layout;
using Atap.Core.Storage;

namespace Atap.Composition;

/// <summary>
/// After a crash Atap starts in safe mode: clock only, and no feature that turns itself off for
/// <see cref="AppMode.Safe"/>. The marker file %LOCALAPPDATA%\AtapData\crash.marker says the last run crashed.
/// </summary>
internal static class SafeMode
{
    /// <summary>The only widget that runs in safe mode.</summary>
    public static IReadOnlyList<string> Widgets { get; } = [DefaultLayout.ClockWidgetId];

    /// <summary>
    /// The mode this run started in. The app sets it once at startup, before any widget exists, and it is null until then.
    /// For parts that must not act in safe mode and have no <see cref="FeatureContext"/>, such as the script widget.
    /// They treat null as safe mode.
    /// </summary>
    public static AppMode? Current { get; set; }

    /// <summary>Written when Atap closes because of an error. Never throws.</summary>
    public static void WriteMarker(AtapPaths paths)
    {
        try
        {
            Directory.CreateDirectory(paths.Local);
            File.WriteAllText(paths.CrashMarker, DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done while closing.
        }
    }

    /// <summary>True when the last run crashed. The marker is deleted, so the next start is normal again.</summary>
    public static bool ConsumeMarker(AtapPaths paths)
    {
        if (!File.Exists(paths.CrashMarker))
            return false;
        try
        {
            File.Delete(paths.CrashMarker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still start safe; the marker is retried next time.
        }
        return true;
    }
}
