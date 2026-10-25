using System.IO;
using Atap.Core.Sync;

namespace Atap.Platform.OneDrive;

/// <summary>Tells sync when the bundle or a conflict copy may have changed. The system reports changes, so nothing polls.</summary>
internal interface ISyncFolderWatch
{
    /// <summary>
    /// Starts watching <paramref name="folder"/>. <paramref name="changed"/> runs on any thread, also when
    /// events were lost. Null when the folder cannot be watched. Dispose the result to stop.
    /// </summary>
    IDisposable? Watch(string folder, Action changed);
}

/// <summary>
/// One <see cref="FileSystemWatcher"/> on the sync folder, non-recursive, for atap-sync*.json only. The temp
/// file renamed over the bundle shows up as a rename. Deletes do not wake it: sync deletes the copies itself.
/// </summary>
internal sealed class SyncFolderWatch : ISyncFolderWatch
{
    public IDisposable? Watch(string folder, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if (!Directory.Exists(folder))
            return null;

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(folder, SyncFileNames.WatchPattern)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, _) => changed();
            watcher.Created += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();
            watcher.Error += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The folder went away meanwhile, or the system has no watcher to spare. The next start tries again.
            watcher?.Dispose();
            return null;
        }
    }
}
