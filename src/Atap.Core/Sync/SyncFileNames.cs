namespace Atap.Core.Sync;

/// <summary>File names in the sync folder, <c>&lt;OneDrive&gt;\Apps\Atap</c>.</summary>
public static class SyncFileNames
{
    /// <summary>The folder under the OneDrive root.</summary>
    public static string FolderUnderOneDrive { get; } = Path.Combine("Apps", "Atap");

    public const string Bundle = "atap-sync.json";

    /// <summary>Written first, then renamed over <see cref="Bundle"/>. Never taken for a conflict copy.</summary>
    public const string Temp = "atap-sync.json.tmp";

    /// <summary>Pattern for a watcher or a folder listing. Matches the bundle and its conflict copies.</summary>
    public const string WatchPattern = "atap-sync*.json";

    /// <summary>
    /// True for a OneDrive conflict copy such as <c>atap-sync-DESKTOP-1234.json</c> or
    /// <c>atap-sync-DESKTOP-1234-2.json</c>. The bundle itself and the temp file are not copies.
    /// </summary>
    public static bool IsConflictCopy(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        const string prefix = "atap-sync-";
        const string suffix = ".json";
        var name = Path.GetFileName(fileName);
        return name.Length > prefix.Length + suffix.Length
            && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
