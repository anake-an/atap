namespace Atap.Core.Net;

/// <summary>What clearing a cache folder did.</summary>
/// <param name="FreedBytes">The size of the files that were deleted.</param>
/// <param name="FilesLeft">Files that could not be deleted, because something had them open.</param>
public readonly record struct ClearResult(long FreedBytes, int FilesLeft)
{
    public static ClearResult operator +(ClearResult a, ClearResult b) => new(a.FreedBytes + b.FreedBytes, a.FilesLeft + b.FilesLeft);
}

/// <summary>Measures and empties a cache folder. Everything in it can be fetched again, so all of it may go.</summary>
public static class CacheFiles
{
    // Links inside the folder are skipped, so clearing never reaches outside it.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>The total size of the files under <paramref name="folder"/>. Zero when it does not exist.</summary>
    public static long Size(string folder)
    {
        if (!Directory.Exists(folder))
            return 0;
        long total = 0;
        foreach (var file in Files(folder))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gone or locked while counting: leave it out.
            }
        }
        return total;
    }

    /// <summary>Deletes every file under <paramref name="folder"/>, then the empty folders inside it. The folder itself stays.</summary>
    public static ClearResult Clear(string folder)
    {
        if (!Directory.Exists(folder))
            return default;
        long freed = 0;
        var left = 0;
        foreach (var file in Files(folder))
        {
            try
            {
                var length = new FileInfo(file).Length;
                File.Delete(file);
                freed += length;
            }
            catch (FileNotFoundException)
            {
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                left++;
            }
        }
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(folder, "*", Options).OrderByDescending(d => d.Length).ToList())
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind costs nothing.
        }
        return new ClearResult(freed, left);
    }

    private static IEnumerable<string> Files(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", Options).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
