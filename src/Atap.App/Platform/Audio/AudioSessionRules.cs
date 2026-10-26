namespace Atap.Platform.Audio;

/// <summary>What Windows says about one audio session and the program behind it. Read once per refresh.</summary>
/// <param name="Id">The session instance id. Never shown.</param>
/// <param name="IsSystemSounds">The Windows system sounds session, which has no program and no name.</param>
/// <param name="DisplayName">The name the app gave its session, if any.</param>
/// <param name="FileDescription">The program's file description, such as "Spotify".</param>
/// <param name="ProductName">The program's product name, such as "Apple Music".</param>
/// <param name="ProcessName">The process name, such as "AMPLibraryAgent".</param>
/// <param name="ExePath">The program file, so two sessions of one program are one row.</param>
internal sealed record AudioSessionInfo(
    string Id,
    bool IsSystemSounds,
    string? DisplayName,
    string? FileDescription,
    string? ProductName,
    string? ProcessName,
    string? ExePath,
    int VolumePercent,
    bool Muted);

/// <summary>
/// How the mixer names and groups sessions, pure. A session is named by its own display name, then the program's
/// description or product name, then the process name, and never by a raw id. Sessions of one app (the same
/// program, or programs of the same product, such as Apple Music and its library helper) are one row.
/// </summary>
internal static class AudioSessionRules
{
    private const char IdSeparator = '\n';

    /// <summary>The row name. Empty when nothing usable is known, so the mixer shows a plain "Another app".</summary>
    public static string NameOf(AudioSessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsSystemSounds)
            return string.Empty;
        return FirstUsable(session.DisplayName, session.FileDescription, session.ProductName, session.ProcessName) ?? string.Empty;
    }

    /// <summary>
    /// One row per app, in the order apps first appear. The volume is the first session's, mute is on only when
    /// every session is muted, and the row id names every session so a change reaches all of them.
    /// </summary>
    public static IReadOnlyList<AppAudio> Merge(IEnumerable<AudioSessionInfo> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return sessions
            .GroupBy(KeyOf, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var members = group.ToList();
                // The app's own session sets the volume shown, not a helper's.
                var first = AppOf(members);
                return new AppAudio(
                    JoinIds(members.Select(s => s.Id)),
                    GroupName(members),
                    first.VolumePercent,
                    members.All(s => s.Muted),
                    first.IsSystemSounds);
            })
            .ToList();
    }

    /// <summary>The session ids a row stands for.</summary>
    public static IReadOnlyList<string> SplitIds(string rowId) =>
        string.IsNullOrEmpty(rowId) ? [] : rowId.Split(IdSeparator);

    private static string JoinIds(IEnumerable<string> ids) => string.Join(IdSeparator, ids);

    // An app's own session name wins. For several programs of one product the product names the row, so a helper
    // never names it. Otherwise the first session's name.
    private static string GroupName(List<AudioSessionInfo> members)
    {
        if (members.Select(m => Usable(m.DisplayName)).FirstOrDefault(n => n is not null) is { } displayName)
            return displayName;
        var programs = members.Select(m => m.ExePath ?? m.ProcessName ?? m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (programs == 1)
            return NameOf(members[0]);
        var app = AppOf(members);
        return UsableProduct(app.ProductName) ?? NameOf(app);
    }

    // The member that names a row and sets its volume: not a helper by its process name ("AMPLibraryAgent",
    // "steamwebhelper"), then one with a name of its own over one named only by its process.
    private static AudioSessionInfo AppOf(List<AudioSessionInfo> members) =>
        members.OrderBy(HelperRank).First();

    private static readonly string[] HelperWords = ["helper", "agent", "service", "host", "webview", "crashpad"];

    private static int HelperRank(AudioSessionInfo session)
    {
        var process = session.ProcessName?.Trim() ?? string.Empty;
        if (HelperWords.Any(word => process.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return 2;
        return string.Equals(NameOf(session), process, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static string KeyOf(AudioSessionInfo session)
    {
        if (session.IsSystemSounds)
            return "system";
        // Programs of one Store app share its package folder, even when their product names differ.
        if (PackageOf(session.ExePath) is { } package)
            return "package:" + package;
        if (UsableProduct(session.ProductName) is { } product)
            return "product:" + product;
        return "program:" + (Usable(session.ExePath) ?? Usable(session.ProcessName) ?? session.Id);
    }

    // "...\WindowsApps\AppleInc.AppleMusicWin_1.1540.0_x64__nzyj5cx40ttqa\AMPLibraryAgent.exe" is "AppleInc.AppleMusicWin".
    private static string? PackageOf(string? exePath)
    {
        const string marker = @"\WindowsApps\";
        var at = exePath?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
        if (at < 0)
            return null;
        var rest = exePath![(at + marker.Length)..];
        var slash = rest.IndexOf('\\', StringComparison.Ordinal);
        if (slash <= 0)
            return null;
        var name = rest[..slash].Split('_')[0];
        return name.Length > 0 ? name : null;
    }

    // Windows' own programs all say "Microsoft Windows Operating System", which is not one app.
    private static string? UsableProduct(string? product) =>
        Usable(product) is { } name && !name.Contains("Operating System", StringComparison.OrdinalIgnoreCase) ? name : null;

    private static string? FirstUsable(params string?[] names) => names.Select(Usable).FirstOrDefault(n => n is not null);

    // Resource references ("@%SystemRoot%...") and ids ("{0.0.0.00000000}.{...}") are not names.
    private static string? Usable(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed[0] is '@' or '{' ? null : trimmed;
    }
}
