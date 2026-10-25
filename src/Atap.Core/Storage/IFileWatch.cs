namespace Atap.Core.Storage;

/// <summary>What a file watch reports.</summary>
public enum FileChange
{
    /// <summary>The file was written, replaced, renamed or deleted.</summary>
    Changed,

    /// <summary>Events may have been lost, for example when the event buffer overflowed. Read the file again.</summary>
    EventsLost,

    /// <summary>
    /// Watching ended, for example because the folder was deleted. Read the file again, and ask for a new watch
    /// after the next write.
    /// </summary>
    Stopped,
}

/// <summary>Tells a store when its file may have changed. The system reports changes, so nothing polls.</summary>
public interface IFileWatch
{
    /// <summary>
    /// Starts watching one file. <paramref name="changed"/> runs on any thread. Null when the folder does not
    /// exist yet or cannot be watched. Dispose the result to stop.
    /// </summary>
    IDisposable? Watch(string filePath, Action<FileChange> changed);
}
