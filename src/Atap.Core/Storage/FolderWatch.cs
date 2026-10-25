namespace Atap.Core.Storage;

/// <summary>
/// One <see cref="FileSystemWatcher"/> per folder, shared by every store in it, so layout.json and
/// settings.json share one watcher on %APPDATA%\Atap. It is non-recursive with the filter *.json, so logs
/// and the data folder never wake it. Windows reports the changes: nothing polls, and an idle folder costs
/// no wakes. The watcher closes when the last file in its folder stops being watched.
/// </summary>
public sealed class FolderWatch : IFileWatch
{
    private const string Filter = "*.json";

    private readonly object _gate = new();
    private readonly Dictionary<string, Folder> _folders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The watch every store uses unless a test gives it another.</summary>
    public static FolderWatch Shared { get; } = new();

    /// <summary>How many folders have a watcher now.</summary>
    public int FolderCount
    {
        get
        {
            lock (_gate)
                return _folders.Count;
        }
    }

    public IDisposable? Watch(string filePath, Action<FileChange> changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(changed);

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name))
            return null;

        lock (_gate)
        {
            if (!_folders.TryGetValue(directory, out var folder))
            {
                folder = Folder.TryStart(this, directory);
                if (folder is null)
                    return null;
                _folders[directory] = folder;
            }
            var subscription = new Subscription(this, folder, name, changed);
            folder.Subscriptions.Add(subscription);
            return subscription;
        }
    }

    private void Notify(Folder folder, string? name, string? oldName)
    {
        List<Action<FileChange>> callbacks;
        lock (_gate)
        {
            callbacks = folder.Subscriptions
                .Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, oldName, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Changed)
                .ToList();
        }
        foreach (var callback in callbacks)
            callback(FileChange.Changed);
    }

    private void OnError(Folder folder)
    {
        // The watcher turns itself off on a real error, and keeps going after a buffer overflow.
        var stopped = !folder.IsRunning;
        List<Action<FileChange>> callbacks;
        lock (_gate)
        {
            callbacks = folder.Subscriptions.Select(s => s.Changed).ToList();
            if (stopped)
            {
                // The stores ask for a new watch after their next write, which also puts the folder back.
                folder.Subscriptions.Clear();
                if (_folders.TryGetValue(folder.Directory, out var current) && current == folder)
                    _folders.Remove(folder.Directory);
            }
        }
        if (stopped)
            folder.Dispose();
        foreach (var callback in callbacks)
            callback(stopped ? FileChange.Stopped : FileChange.EventsLost);
    }

    private void Remove(Subscription subscription)
    {
        Folder? closed = null;
        lock (_gate)
        {
            var folder = subscription.Folder;
            if (folder.Subscriptions.Remove(subscription) && folder.Subscriptions.Count == 0
                && _folders.TryGetValue(folder.Directory, out var current) && current == folder)
            {
                _folders.Remove(folder.Directory);
                closed = folder;
            }
        }
        closed?.Dispose();
    }

    private sealed class Folder : IDisposable
    {
        private readonly FileSystemWatcher _watcher;

        private Folder(FolderWatch owner, string directory, FileSystemWatcher watcher)
        {
            Directory = directory;
            _watcher = watcher;
            watcher.Changed += (_, e) => owner.Notify(this, e.Name, null);
            watcher.Created += (_, e) => owner.Notify(this, e.Name, null);
            watcher.Deleted += (_, e) => owner.Notify(this, e.Name, null);
            // A temp file renamed over the real one shows up here.
            watcher.Renamed += (_, e) => owner.Notify(this, e.Name, e.OldName);
            watcher.Error += (_, _) => owner.OnError(this);
        }

        public string Directory { get; }

        public List<Subscription> Subscriptions { get; } = [];

        public bool IsRunning => _watcher.EnableRaisingEvents;

        public static Folder? TryStart(FolderWatch owner, string directory)
        {
            if (!System.IO.Directory.Exists(directory))
                return null;

            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(directory, Filter)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                var folder = new Folder(owner, directory, watcher);
                watcher.EnableRaisingEvents = true;
                return folder;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // The folder went away meanwhile, or the system has no watcher to spare. The store tries again after its next write.
                watcher?.Dispose();
                return null;
            }
        }

        public void Dispose() => _watcher.Dispose();
    }

    private sealed class Subscription(FolderWatch owner, Folder folder, string name, Action<FileChange> changed) : IDisposable
    {
        private int _disposed;

        public Folder Folder { get; } = folder;

        public string Name { get; } = name;

        public Action<FileChange> Changed { get; } = changed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Remove(this);
        }
    }
}
