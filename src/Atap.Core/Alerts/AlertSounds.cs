namespace Atap.Core.Alerts;

/// <summary>Where an alert sound comes from.</summary>
public enum AlertSoundKind
{
    /// <summary>The Windows notification sound.</summary>
    Windows,

    /// <summary>An audio file the user picked on this PC.</summary>
    File,

    /// <summary>A sound that ships inside Atap, embedded as "Atap.Data.&lt;name&gt;".</summary>
    Bundled,
}

/// <param name="Kind">Where it comes from.</param>
/// <param name="Value">The file path or the bundled name. Empty for the Windows sound.</param>
public sealed record AlertSound(AlertSoundKind Kind, string Value);

/// <summary>
/// The values of <see cref="Alert.SoundKey"/>. Null means no sound. Every sound plays once and never loops.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>default</c>: the sound the user picked in Settings (the Windows sound, their own file or none).</item>
/// <item><c>windows</c>: the Windows notification sound.</item>
/// <item><c>file:C:\Sounds\azan.mp3</c>: a file on this PC. Network paths are refused.</item>
/// <item><c>data:azan.ogg</c>: a sound bundled with Atap.</item>
/// </list>
/// </remarks>
public static class AlertSounds
{
    public const string Default = "default";
    public const string Windows = "windows";
    public const string FilePrefix = "file:";
    public const string BundledPrefix = "data:";

    /// <summary>Audio files Windows can usually play. Others are refused rather than failing later.</summary>
    public static IReadOnlySet<string> FileExtensions { get; } =
        new HashSet<string>([".wav", ".mp3", ".wma", ".m4a", ".aac", ".ogg", ".flac"], StringComparer.OrdinalIgnoreCase);

    public static string ForFile(string path) => FilePrefix + path;

    public static string ForBundled(string name) => BundledPrefix + name;

    /// <summary>
    /// What a sound key plays, or null for no sound or a key that cannot be used.
    /// </summary>
    /// <param name="key">The alert's <see cref="Alert.SoundKey"/>.</param>
    /// <param name="userDefault">What <see cref="Default"/> stands for: the user's pick, itself a sound key or null.</param>
    public static AlertSound? Resolve(string? key, string? userDefault)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        if (key == Default)
            return userDefault == Default ? null : Resolve(userDefault, null);
        if (key == Windows)
            return new AlertSound(AlertSoundKind.Windows, string.Empty);
        if (key.StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            var path = key[FilePrefix.Length..];
            return IsUsableFile(path) ? new AlertSound(AlertSoundKind.File, path) : null;
        }
        if (key.StartsWith(BundledPrefix, StringComparison.Ordinal))
        {
            var name = key[BundledPrefix.Length..];
            return IsUsableBundledName(name) ? new AlertSound(AlertSoundKind.Bundled, name) : null;
        }
        return null;
    }

    /// <summary>
    /// A full local path such as "C:\Sounds\chime.wav", with a known audio extension. Relative, network
    /// and device paths are refused, so playing a sound never reaches another computer.
    /// </summary>
    public static bool IsUsableFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 4 || path.Length > 1024)
            return false;
        var drive = char.ToUpperInvariant(path[0]);
        if (drive is < 'A' or > 'Z' || path[1] != ':' || path[2] is not ('\\' or '/'))
            return false;
        if (path.IndexOfAny(['\0', '<', '>', '"', '|', '?', '*']) >= 0 || path.IndexOf(':', 2) >= 0)
            return false;
        return FileExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>A bundled file name such as "azan.ogg": lower-case letters, digits, dots and dashes.</summary>
    public static bool IsUsableBundledName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 100 || name[0] is '.' or '-')
            return false;
        foreach (var c in name)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-'))
                return false;
        }
        return FileExtensions.Contains(Path.GetExtension(name));
    }

    /// <summary>
    /// Whether a sound may play now. Critical sounds always play on time, even in full screen. Other
    /// sounds respect quiet hours and full screen.
    /// </summary>
    public static bool MayPlay(AlertPriority priority, bool quiet, bool hidden) =>
        priority == AlertPriority.Critical || (!quiet && !hidden);
}
