namespace Atap.Core.Storage;

/// <param name="Deleted">Files, links and folders that were deleted.</param>
/// <param name="Left">Ones that could not be deleted, for example a file another program has open.</param>
public readonly record struct EraseResult(int Deleted, int Left);

/// <summary>
/// "Delete all my Atap data": deletes the roaming folder (settings, layout, profiles, to-dos, notes and
/// logs) and the local folder (saved passwords and keys, cache and state), and nothing else. The
/// installed program and its updater live in %LOCALAPPDATA%\Atap, which is neither, so they stay.
/// </summary>
/// <remarks>
/// It only ever deletes inside a folder named exactly <see cref="RoamingFolderName"/> or
/// <see cref="LocalFolderName"/>, so a wrong path can never empty another folder. A link inside (a
/// junction or a symbolic link) is removed as a link, and what it points to is never touched. Run it
/// once nothing writes there any more, as Atap quits.
/// </remarks>
public static class AtapDataEraser
{
    public const string RoamingFolderName = "Atap";
    public const string LocalFolderName = "AtapData";

    // Hidden and system files go too. Links are seen, so they are removed as links.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>Deletes both folders. Throws, before deleting anything, when either is not one of Atap's own.</summary>
    public static EraseResult Erase(AtapPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var roaming = OwnFolder(paths.Roaming, RoamingFolderName);
        var local = OwnFolder(paths.Local, LocalFolderName);
        var counts = new Counts();
        DeleteFolder(new DirectoryInfo(roaming), counts);
        DeleteFolder(new DirectoryInfo(local), counts);
        return new EraseResult(counts.Deleted, counts.Left);
    }

    /// <summary>
    /// True for a full local path whose last folder is <paramref name="name"/>, such as
    /// "C:\Users\Aisyah\AppData\Roaming\Atap". Drive roots, relative and network paths are refused.
    /// </summary>
    public static bool IsOwnFolder(string? path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            return false;
        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        return Path.GetDirectoryName(full) is { Length: > 0 }
            && string.Equals(Path.GetFileName(full), name, StringComparison.OrdinalIgnoreCase);
    }

    private static string OwnFolder(string path, string name) =>
        IsOwnFolder(path, name)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
            : throw new InvalidOperationException($"'{path}' is not Atap's own {name} folder, so it is not deleted.");

    private static void DeleteFolder(DirectoryInfo folder, Counts counts)
    {
        if (!folder.Exists)
            return;
        // A link: remove the link only. What it points to is outside Atap's folders.
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Delete(folder.Delete, counts);
            return;
        }
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = folder.EnumerateFileSystemInfos("*", Options).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            counts.Left++;
            return;
        }
        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo inner)
                DeleteFolder(inner, counts);
            else
                DeleteFile((FileInfo)entry, counts);
        }
        Delete(folder.Delete, counts);
    }

    private static void DeleteFile(FileInfo file, Counts counts)
    {
        try
        {
            // A read-only flag would stop the delete. A link keeps its own, since changing it could reach the target.
            if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint) && file.IsReadOnly)
                file.IsReadOnly = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The delete below then says whether it can go.
        }
        Delete(file.Delete, counts);
    }

    private static void Delete(Action delete, Counts counts)
    {
        try
        {
            delete();
            counts.Deleted++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            counts.Left++;
        }
    }

    private sealed class Counts
    {
        public int Deleted { get; set; }

        public int Left { get; set; }
    }
}
