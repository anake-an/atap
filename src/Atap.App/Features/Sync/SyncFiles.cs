using System.Globalization;
using System.IO;
using System.Text;
using Atap.Core.Storage;
using Atap.Core.Sync;

namespace Atap.Features.Sync;

/// <summary>atap-sync.json as read from the sync folder.</summary>
/// <param name="Bundle">The bundle when it was read, otherwise null.</param>
/// <param name="CanBeReplaced">False for a file this PC must not write over.</param>
/// <param name="Outcome">How the read went, or null when there is no file.</param>
/// <param name="Unavailable">
/// The file is there but will not open, for example a cloud-only file while OneDrive is offline, or a
/// locked one. Nothing is merged then, and the next folder event or start tries again.
/// </param>
internal sealed record SyncMainRead(SyncBundle? Bundle, bool CanBeReplaced, SyncBundleReadOutcome? Outcome, bool Unavailable)
{
    public static SyncMainRead Missing { get; } = new(null, true, null, false);
}

/// <summary>A conflict copy that was read, and where it is, so it can be deleted once merged.</summary>
internal sealed record SyncConflictCopy(string Path, SyncBundle Bundle);

/// <summary>The files in the sync folder, <c>&lt;OneDrive&gt;\Apps\Atap</c>. Atap only reads and writes local files: OneDrive uploads them.</summary>
internal sealed class SyncFolderFiles(string folder)
{
    public string Folder { get; } = folder;

    public string BundlePath => Path.Combine(Folder, SyncFileNames.Bundle);

    public bool BundleExists => File.Exists(BundlePath);

    public SyncMainRead ReadMain()
    {
        var path = BundlePath;
        if (!File.Exists(path))
            return SyncMainRead.Missing;
        try
        {
            if (new FileInfo(path).Length > SyncLimits.MaxBundleFileBytes)
                return new(null, false, SyncBundleReadOutcome.TooLarge, false);
            var result = SyncBundleReader.Read(ReadBytes(path));
            return new(result.Bundle, result.CanBeReplaced, result.Outcome, false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return SyncMainRead.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, false, null, true);
        }
    }

    /// <summary>
    /// The OneDrive conflict copies that could be read. A copy that will not open, or that is not a bundle
    /// this version reads, is left where it is.
    /// </summary>
    public List<SyncConflictCopy> ReadCopies(out bool someUnavailable)
    {
        someUnavailable = false;
        var copies = new List<SyncConflictCopy>();
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(Folder, SyncFileNames.WatchPattern).Where(SyncFileNames.IsConflictCopy).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            someUnavailable = true;
            return copies;
        }

        foreach (var path in paths)
        {
            try
            {
                if (new FileInfo(path).Length > SyncLimits.MaxBundleFileBytes)
                    continue;
                if (SyncBundleReader.Read(ReadBytes(path)).Bundle is { } bundle)
                    copies.Add(new SyncConflictCopy(path, bundle));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Gone meanwhile.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                someUnavailable = true;
            }
        }
        return copies;
    }

    /// <summary>Writes a temp file, then renames it over atap-sync.json.</summary>
    public void WriteBundle(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        AtomicFile.Write(BundlePath, stream => stream.Write(bytes));
    }

    /// <summary>Deletes a merged conflict copy. One that cannot go now is merged again next time, which changes nothing.</summary>
    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] ReadBytes(string path)
    {
        using var stream = AtomicFile.OpenRead(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>sync-state.json in %LOCALAPPDATA%\AtapData, which never leaves this PC, and the backups in %APPDATA%\Atap.</summary>
internal sealed class SyncLocalFiles(AtapPaths paths)
{
    public const string StateFileName = "sync-state.json";

    public AtapPaths Paths { get; } = paths;

    public string StatePath => Path.Combine(Paths.Local, StateFileName);

    /// <summary>The saved state, or null when there is none or it cannot be read.</summary>
    public SyncState? ReadState()
    {
        try
        {
            return File.Exists(StatePath) ? SyncStateFile.Read(File.ReadAllText(StatePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool TryWriteState(SyncState state, Action<string, Exception?>? log = null)
    {
        var bytes = Encoding.UTF8.GetBytes(SyncStateFile.Write(state));
        try
        {
            AtomicFile.Write(StatePath, stream => stream.Write(bytes));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("Sync could not save sync-state.json.", ex);
            return false;
        }
    }

    /// <summary>
    /// Copies <paramref name="files"/> that exist into a new <c>%APPDATA%\Atap\backup-&lt;timestamp&gt;</c> folder
    /// and returns it. Throws when a copy fails, so nothing else changes.
    /// </summary>
    public string Backup(DateTimeOffset now, IEnumerable<string> files)
    {
        var name = "backup-" + now.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var folder = Path.Combine(Paths.Roaming, name);
        for (var n = 2; Directory.Exists(folder); n++)
            folder = Path.Combine(Paths.Roaming, string.Create(CultureInfo.InvariantCulture, $"{name}-{n}"));
        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            if (File.Exists(file))
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }
        return folder;
    }
}
