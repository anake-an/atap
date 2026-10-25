namespace Atap.Core.Privacy;

public enum Sensor
{
    Camera,
    Microphone,
}

/// <summary>
/// One app's key under CapabilityAccessManager\ConsentStore\webcam or \microphone, as Windows writes it. Store apps
/// use their package family name. Other apps sit under NonPackaged, named by their path with # for each \.
/// </summary>
public sealed record ConsentEntry(Sensor Sensor, string KeyName, bool IsPackaged, long LastUsedTimeStart, long LastUsedTimeStop);

/// <summary>An app using the camera or the microphone right now. ExePath is null for Store apps.</summary>
public sealed record SensorUse(Sensor Sensor, string AppName, string? ExePath);

/// <summary>Reads the ConsentStore entries. Pure, so the rules are tested without the registry.</summary>
public static class ConsentUsage
{
    /// <summary>Windows sets Start when an app opens the sensor and Stop when it lets go. Stop 0 means still open.</summary>
    public static bool IsInUse(ConsentEntry entry) => entry.LastUsedTimeStart != 0 && entry.LastUsedTimeStop == 0;

    public static string? ExePath(ConsentEntry entry) => entry.IsPackaged ? null : entry.KeyName.Replace('#', '\\');

    /// <summary>"Zoom" for C:#Program Files#Zoom#bin#Zoom.exe, "WindowsCamera" for Microsoft.WindowsCamera_8wekyb3d8bbwe.</summary>
    public static string AppName(ConsentEntry entry)
    {
        if (!entry.IsPackaged)
        {
            var path = entry.KeyName.Replace('#', '\\');
            var name = Path.GetFileNameWithoutExtension(path);
            return name.Length > 0 ? name : path;
        }
        var family = entry.KeyName;
        var underscore = family.IndexOf('_', StringComparison.Ordinal);
        if (underscore > 0)
            family = family[..underscore];
        var dot = family.LastIndexOf('.');
        return dot >= 0 && dot < family.Length - 1 ? family[(dot + 1)..] : family;
    }

    /// <summary>
    /// The apps using a sensor now, one row per app and sensor. An app that crashed never writes Stop, so entries
    /// whose app is not running are left out. <paramref name="isRunning"/> gets the exe path of a NonPackaged entry.
    /// Store apps have no path and are kept.
    /// </summary>
    public static IReadOnlyList<SensorUse> InUse(IEnumerable<ConsentEntry> entries, Func<string, bool> isRunning) =>
        entries
            .Where(IsInUse)
            .Select(e => new SensorUse(e.Sensor, AppName(e), ExePath(e)))
            .Where(u => u.ExePath is null || isRunning(u.ExePath))
            .DistinctBy(u => (u.Sensor, u.AppName.ToUpperInvariant()))
            .OrderBy(u => u.Sensor)
            .ThenBy(u => u.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
