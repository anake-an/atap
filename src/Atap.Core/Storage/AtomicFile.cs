namespace Atap.Core.Storage;

/// <summary>File helpers shared by the stores.</summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes a temp file next to <paramref name="path"/>, then replaces it, so a crash never leaves half a file.
    /// A leftover temp file is overwritten. When the replace fails, the temp file is removed and the error thrown.
    /// </summary>
    public static void Write(string path, Action<Stream> write)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Opens for reading without blocking editors or sync clients that have the file open.</summary>
    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next write overwrites it.
        }
    }
}
