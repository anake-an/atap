using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atap.Core.Storage;

/// <summary>What happened to a change, or to a flush.</summary>
public enum FileWriteResult
{
    /// <summary>Saved, or queued to save.</summary>
    Applied,

    /// <summary>The session is read-only. The change applies in memory only.</summary>
    ReadOnly,

    /// <summary>
    /// The file does not parse right now, could not be written, or a newer Atap wrote it meanwhile. The change
    /// applies in memory, and is written later when the file allows it.
    /// </summary>
    Failed,
}

/// <summary>Why changes are not reaching the file. The app shows a banner for anything but <see cref="None"/>.</summary>
public enum StoreProblem
{
    None,

    /// <summary>The file stopped parsing while Atap runs, for example while someone edits it. It is left alone until it parses again.</summary>
    FileDoesNotParse,

    /// <summary>The file could not be written, even after a retry. The next change tries again.</summary>
    CannotWrite,
}

/// <summary>Timings shared by every <see cref="JsonFileStore{T}"/>.</summary>
public static class JsonFileStore
{
    /// <summary>Changes are written this long after the last one, so a burst of changes gives one write.</summary>
    public static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>The file is read again once it has had no events for this long.</summary>
    public static readonly TimeSpan QuietDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>A file that could not be written is tried once more after this long.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Tries at writing changes still waiting when Atap quits or Windows signs out.</summary>
    public const int ExitAttempts = 3;

    /// <summary>The pause between those tries.</summary>
    public static readonly TimeSpan ExitRetryDelay = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// One JSON file (layout.json, settings.json) kept in memory and written back after changes stop.
/// </summary>
/// <remarks>
/// <para>
/// Loading: a missing file gets defaults. A corrupt file is renamed to &lt;name&gt;.corrupt-&lt;stamp&gt;.json.
/// An older file is backed up as &lt;name&gt;.v&lt;n&gt;.bak.json, upgraded and saved. A file from a newer Atap is
/// read as far as this version understands it and never written. A file that cannot be read, or cannot be
/// backed up, is left alone and the session is read-only.
/// </para>
/// <para>
/// Writing: <see cref="Update"/> changes memory at once and queues the change. <see cref="JsonFileStore.FlushDelay"/>
/// after the last one the store flushes. When the file changed since this store last read or wrote it, it is read
/// again and the queued changes are replayed on it, so edits made by hand survive. Then a temp file replaces the
/// real one. A failed write is retried once, then waits for the next change. Queued changes are also written on
/// <see cref="Dispose"/> and when the process exits.
/// </para>
/// <para>
/// Watching: after <see cref="JsonFileStore.QuietDelay"/> without events the file is hashed. A hash this store read
/// or wrote itself is ignored. Anything else is read again and raises <see cref="Reloaded"/>. A file that stops
/// parsing is never backed up or replaced: the copy in memory stays and <see cref="Problem"/> says why.
/// </para>
/// <para>
/// Timer and watcher work runs on the synchronization context the store was made on, or inline when there is
/// none, so use the store on that thread. Nothing polls, and no timer is armed while nothing changes.
/// </para>
/// </remarks>
public sealed class JsonFileStore<T> : IDisposable where T : class
{
    private readonly JsonFileFormat<T> _format;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _ui;
    private readonly IFileWatch? _watch;
    private readonly object _gate = new();
    private List<Action<T>> _pending = [];
    private T _document;

    // The hash of the file as this store last read or wrote it. Null when it is missing or unknown.
    private string? _knownHash;
    private IDisposable? _watching;
    private bool _watchWanted;
    private ITimer? _flushTimer;
    private ITimer? _quietTimer;
    private bool _retried;
    private bool _exitHooked;
    private bool _disposed;

    /// <param name="filePath">The file.</param>
    /// <param name="format">How to read, upgrade and write it.</param>
    /// <param name="time">Clock for the delays and for backup names.</param>
    /// <param name="ui">Where timer and watcher work runs. Defaults to the current synchronization context.</param>
    /// <param name="watch">Reports outside changes to the file. Null watches nothing.</param>
    public JsonFileStore(string filePath, JsonFileFormat<T> format, TimeProvider? time = null, SynchronizationContext? ui = null, IFileWatch? watch = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(format);
        FilePath = filePath;
        _format = format;
        _time = time ?? TimeProvider.System;
        _ui = ui ?? SynchronizationContext.Current;
        _watch = watch;
        _document = CreateDefaults();
    }

    [Flags]
    private enum Raise
    {
        None = 0,
        Reloaded = 1,
        Saved = 2,
        Problem = 4,
    }

    private enum DiskState
    {
        /// <summary>At the current version.</summary>
        Readable,

        /// <summary>Older: backed up, then upgraded in memory.</summary>
        Upgraded,

        /// <summary>From a newer Atap: read as far as understood.</summary>
        Newer,

        /// <summary>Older, and the backup failed. Upgraded in memory only.</summary>
        NoBackup,

        DoesNotParse,
    }

    public string FilePath { get; }

    /// <summary>The document in memory. Change it through <see cref="Update"/>.</summary>
    public T Document => _document;

    public FileLoadOutcome LastLoadOutcome { get; private set; } = FileLoadOutcome.CreatedDefaults;

    /// <summary>True when the file could not be read or is from a newer Atap. Changes then stay in memory.</summary>
    public bool IsReadOnly => LastLoadOutcome is FileLoadOutcome.Unreadable or FileLoadOutcome.NewerVersionReadOnly;

    public StoreProblem Problem { get; private set; }

    /// <summary>True while changes wait to be written.</summary>
    public bool HasPendingChanges
    {
        get
        {
            lock (_gate)
                return _pending.Count > 0;
        }
    }

    /// <summary>The document was replaced by the file as it is now, after a change made outside this store.</summary>
    public event EventHandler? Reloaded;

    /// <summary>The file was written.</summary>
    public event EventHandler? Saved;

    public event EventHandler? ProblemChanged;

    /// <summary>Reads the file (see the remarks), then starts watching it. Changes not yet written are dropped.</summary>
    public FileLoadOutcome Load()
    {
        var raise = Raise.None;
        FileLoadOutcome outcome;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DropPending();
            _flushTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _retried = false;
            SetProblem(StoreProblem.None, ref raise);
            _watchWanted = true;

            // Watch first, so a change made while the file is read is not missed. A missing folder exists after the defaults are written.
            EnsureWatching();
            (_document, outcome) = LoadFile();
            LastLoadOutcome = outcome;
            EnsureWatching();
        }
        RaiseEvents(raise);
        return outcome;
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the document in memory at once, and queues it for the file. A read-only
    /// session changes memory only. <paramref name="change"/> may run again later on the file as it is on disk.
    /// </summary>
    public FileWriteResult Update(Action<T> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            change(_document);
            _format.Normalize?.Invoke(_document);
            if (IsReadOnly)
                return FileWriteResult.ReadOnly;

            _pending.Add(change);
            _retried = false;
            ArmFlush(JsonFileStore.FlushDelay);
            HookExit();
            return Problem == StoreProblem.None ? FileWriteResult.Applied : FileWriteResult.Failed;
        }
    }

    /// <summary>Writes queued changes now instead of after the delay.</summary>
    public FileWriteResult Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return FlushCore(raiseEvents: true, onExit: false);
    }

    /// <summary>Writes queued changes, trying a few times if the file is held, then stops watching.</summary>
    public void Dispose()
    {
        FlushWithRetry(onExit: false);
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            DropPending();
            _watching?.Dispose();
            _watching = null;
            _flushTimer?.Dispose();
            _flushTimer = null;
            _quietTimer?.Dispose();
            _quietTimer = null;
        }
    }

    private FileWriteResult FlushCore(bool raiseEvents, bool onExit)
    {
        var raise = Raise.None;
        FileWriteResult result;
        lock (_gate)
        {
            if (_disposed)
                return FileWriteResult.Applied;
            _flushTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            // A folder deleted meanwhile, for example with all Atap data, is not brought back on the way out.
            result = onExit && !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(FilePath)))
                ? FileWriteResult.Failed
                : WritePending(ref raise);
        }
        if (raiseEvents)
            RaiseEvents(raise);
        return result;
    }

    private FileWriteResult WritePending(ref Raise raise)
    {
        if (_pending.Count == 0)
            return IsReadOnly ? FileWriteResult.ReadOnly : FileWriteResult.Applied;
        if (IsReadOnly)
        {
            DropPending();
            return FileWriteResult.ReadOnly;
        }

        try
        {
            // A missing file gets the whole document.
            if (ReadBytes() is { } bytes)
            {
                var hash = Hash(bytes);
                if (hash != _knownHash)
                {
                    // Changed outside this store since it was last read or written here: start from the file
                    // as it is now and replay the queued changes on it, so hand edits survive.
                    var (state, disk) = ReadDisk(bytes);
                    switch (state)
                    {
                        case DiskState.DoesNotParse:
                            // Never backed up or replaced while Atap runs. The changes wait until it parses again.
                            SetProblem(StoreProblem.FileDoesNotParse, ref raise);
                            return FileWriteResult.Failed;
                        case DiskState.NoBackup:
                            return RetryLater(ref raise);
                        case DiskState.Newer:
                            // A newer Atap wrote it meanwhile, for example a synced copy. Never downgrade it.
                            LastLoadOutcome = FileLoadOutcome.NewerVersionReadOnly;
                            _document = Replay(disk!);
                            _knownHash = hash;
                            DropPending();
                            raise |= Raise.Reloaded;
                            return FileWriteResult.Failed;
                    }
                    _document = Replay(disk!);
                    _knownHash = hash;
                    raise |= Raise.Reloaded;
                }
            }

            WriteFile(_document);
            DropPending();
            _retried = false;
            SetProblem(StoreProblem.None, ref raise);
            raise |= Raise.Saved;
            EnsureWatching();
            return FileWriteResult.Applied;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return RetryLater(ref raise);
        }
    }

    private FileWriteResult RetryLater(ref Raise raise)
    {
        if (!_retried)
        {
            // Usually an editor or a sync client that holds the file for a moment.
            _retried = true;
            ArmFlush(JsonFileStore.RetryDelay);
        }
        else
        {
            _retried = false;
            SetProblem(StoreProblem.CannotWrite, ref raise);
        }
        return FileWriteResult.Failed;
    }

    /// <summary>After the folder was quiet: reads the file again when it changed outside this store.</summary>
    private void Reload()
    {
        var raise = Raise.None;
        lock (_gate)
        {
            if (_disposed || !_watchWanted)
                return;
            ReloadFromDisk(ref raise);
            // The folder may have come back after the watcher stopped: watch it again.
            EnsureWatching();
        }
        RaiseEvents(raise);
    }

    private void ReloadFromDisk(ref Raise raise)
    {
        byte[]? bytes;
        try
        {
            bytes = ReadBytes();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Locked right now. The next event or write reads it.
            return;
        }

        if (bytes is null)
        {
            // Deleted. The copy in memory stays, and the next write puts the whole file back.
            _knownHash = null;
            return;
        }

        var hash = Hash(bytes);
        if (hash == _knownHash)
            return;

        var (state, disk) = ReadDisk(bytes);
        switch (state)
        {
            case DiskState.DoesNotParse:
                // Half-typed or broken by hand: never backed up or replaced. The copy in memory stays.
                SetProblem(StoreProblem.FileDoesNotParse, ref raise);
                return;
            case DiskState.NoBackup:
                // An older file that cannot be backed up is left alone. The next event or write tries again.
                return;
            case DiskState.Newer:
                LastLoadOutcome = FileLoadOutcome.NewerVersionReadOnly;
                _document = Replay(disk!);
                DropPending();
                break;
            default:
                // A file that reads again ends a read-only session.
                if (IsReadOnly)
                    LastLoadOutcome = state == DiskState.Upgraded ? FileLoadOutcome.Migrated : FileLoadOutcome.Loaded;
                _document = Replay(disk!);
                if (state == DiskState.Upgraded)
                    _pending.Add(static _ => { });
                if (_pending.Count > 0)
                {
                    // Changes made here still need writing, now on top of the file as it is.
                    ArmFlush(JsonFileStore.FlushDelay);
                    HookExit();
                }
                if (Problem == StoreProblem.FileDoesNotParse)
                    SetProblem(StoreProblem.None, ref raise);
                break;
        }
        _knownHash = hash;
        raise |= Raise.Reloaded;
    }

    private (T Document, FileLoadOutcome Outcome) LoadFile()
    {
        byte[]? bytes;
        try
        {
            bytes = ReadBytes();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Leave the file alone. It may just be locked by an editor.
            _knownHash = null;
            return (CreateDefaults(), FileLoadOutcome.Unreadable);
        }

        if (bytes is null)
        {
            _knownHash = null;
            return _format.WriteDefaultsAtLoad
                ? WriteDefaults(FileLoadOutcome.CreatedDefaults)
                : (CreateDefaults(), FileLoadOutcome.CreatedDefaults);
        }

        var hash = Hash(bytes);
        var (state, document) = ReadDisk(bytes);
        switch (state)
        {
            case DiskState.Readable:
                _knownHash = hash;
                return (document!, FileLoadOutcome.Loaded);
            case DiskState.Newer:
                // Never written, backed up or renamed. A shape this version cannot read gives defaults, still read-only.
                _knownHash = hash;
                return (document!, FileLoadOutcome.NewerVersionReadOnly);
            case DiskState.Upgraded:
                _knownHash = hash;
                try
                {
                    WriteFile(document!);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    // Not fatal: the upgraded document is used in memory and the upgrade runs again next time.
                }
                return (document!, FileLoadOutcome.Migrated);
            case DiskState.NoBackup when document is not null:
                // Without a backup, an upgrade written now could lose the user's file. Use it read-only.
                _knownHash = hash;
                return (document, FileLoadOutcome.Unreadable);
        }

        try
        {
            BackUpCorruptFile();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Another app holds it open. Without a backup, writing defaults would lose the user's data.
            _knownHash = hash;
            return (CreateDefaults(), FileLoadOutcome.Unreadable);
        }
        _knownHash = null;
        return _format.WriteDefaultsAtLoad
            ? WriteDefaults(FileLoadOutcome.RecoveredFromCorrupt)
            : (CreateDefaults(), FileLoadOutcome.RecoveredFromCorrupt);
    }

    /// <summary>Reads file bytes as a document at the current version. An older file is backed up before its upgrade.</summary>
    private (DiskState State, T? Document) ReadDisk(byte[] bytes)
    {
        var (root, version) = ParseRoot(bytes);
        if (root is null)
            return (DiskState.DoesNotParse, null);
        if (version > _format.CurrentVersion)
            return (DiskState.Newer, TryParse(root) ?? CreateDefaults());

        var state = DiskState.Readable;
        if (version < _format.CurrentVersion)
        {
            state = TryBackUp(version) ? DiskState.Upgraded : DiskState.NoBackup;
            Upgrade(root, version);
        }
        var document = TryParse(root);
        if (document is null && state != DiskState.NoBackup)
            return (DiskState.DoesNotParse, null);
        return (state, document);
    }

    /// <summary>Applies the queued changes to a document read from the file. A change that no longer fits it is dropped.</summary>
    private T Replay(T document)
    {
        var kept = new List<Action<T>>(_pending.Count);
        foreach (var change in _pending)
        {
            try
            {
                change(document);
                kept.Add(change);
            }
            catch (Exception)
            {
                // It ran once on the copy in memory, but the file as it is now has no room for it. The file wins.
            }
        }
        _pending = kept;
        _format.Normalize?.Invoke(document);
        return document;
    }

    private void Upgrade(JsonObject root, int fromVersion)
    {
        foreach (var migration in _format.Migrations.Where(m => m.FromVersion >= fromVersion).OrderBy(m => m.FromVersion))
            migration.Apply(root);
        root["schemaVersion"] = _format.CurrentVersion;
    }

    private T? TryParse(JsonObject root)
    {
        T? document;
        try
        {
            document = _format.Parse(root);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        if (document is not null)
            _format.Normalize?.Invoke(document);
        return document;
    }

    private T CreateDefaults()
    {
        var document = _format.CreateDefaults();
        _format.Normalize?.Invoke(document);
        return document;
    }

    private (T Document, FileLoadOutcome Outcome) WriteDefaults(FileLoadOutcome outcome)
    {
        var document = CreateDefaults();
        try
        {
            WriteFile(document);
            return (document, outcome);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return (document, FileLoadOutcome.Unreadable);
        }
    }

    private void WriteFile(T document)
    {
        var bytes = _format.Serialize(document);
        AtomicFile.Write(FilePath, stream => stream.Write(bytes));
        _knownHash = Hash(bytes);
    }

    /// <summary>The whole file, or null when it does not exist. Other I/O errors are thrown.</summary>
    private byte[]? ReadBytes()
    {
        try
        {
            using var stream = AtomicFile.OpenRead(FilePath);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The raw JSON and its version. Root is null when it does not parse as an object.</summary>
    private static (JsonObject? Root, int Version) ParseRoot(byte[] bytes)
    {
        ReadOnlySpan<byte> json = bytes;
        // Some editors start the file with a byte order mark.
        if (json.StartsWith("﻿"u8))
            json = json[3..];
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return (null, 0);
            // A file without a version is from the first release.
            // Missing means the first version. Present but not a whole number means an Atap this one does not know,
            // so it counts as newer and is never written.
            var version = root["schemaVersion"] is not { } versionNode ? 1
                : versionNode is JsonValue value && value.TryGetValue<int>(out var number) ? number : int.MaxValue;
            return (root, Math.Max(version, 1));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return (null, 0);
        }
    }

    private bool TryBackUp(int version)
    {
        try
        {
            File.Copy(FilePath, SiblingPath(string.Create(CultureInfo.InvariantCulture, $".v{version}.bak.json")), overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return false;
        }
    }

    private void BackUpCorruptFile()
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        File.Move(FilePath, SiblingPath($".corrupt-{stamp}.json"), overwrite: true);
    }

    /// <summary>A file next to this one: the same name with <paramref name="suffix"/> in place of the extension.</summary>
    private string SiblingPath(string suffix) =>
        Path.Combine(Path.GetDirectoryName(FilePath) ?? string.Empty, Path.GetFileNameWithoutExtension(FilePath) + suffix);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    private void DropPending()
    {
        _pending = [];
        UnhookExit();
    }

    private void SetProblem(StoreProblem problem, ref Raise raise)
    {
        if (Problem == problem)
            return;
        Problem = problem;
        raise |= Raise.Problem;
    }

    private void RaiseEvents(Raise raise)
    {
        if ((raise & Raise.Reloaded) != 0)
            Reloaded?.Invoke(this, EventArgs.Empty);
        if ((raise & Raise.Saved) != 0)
            Saved?.Invoke(this, EventArgs.Empty);
        if ((raise & Raise.Problem) != 0)
            ProblemChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureWatching()
    {
        if (_watch is null || !_watchWanted || _watching is not null || _disposed)
            return;
        _watching = _watch.Watch(FilePath, OnFileChange);
    }

    /// <summary>Any thread. Each event pushes the read back, so a burst of events gives one read.</summary>
    private void OnFileChange(FileChange change)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (change == FileChange.Stopped)
            {
                // Watched again after the next write, which also brings back a deleted folder.
                _watching?.Dispose();
                _watching = null;
            }
            _quietTimer ??= _time.CreateTimer(static state => ((JsonFileStore<T>)state!).Post(static store => store.Reload()), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _quietTimer.Change(JsonFileStore.QuietDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void ArmFlush(TimeSpan delay)
    {
        _flushTimer ??= _time.CreateTimer(
            static state => ((JsonFileStore<T>)state!).Post(static store => store.FlushCore(raiseEvents: true, onExit: false)),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _flushTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void Post(Action<JsonFileStore<T>> work)
    {
        var ui = _ui;
        if (ui is null || SynchronizationContext.Current == ui)
            work(this);
        else
            ui.Post(_ => work(this), null);
    }

    private void HookExit()
    {
        if (_exitHooked)
            return;
        _exitHooked = true;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void UnhookExit()
    {
        if (!_exitHooked)
            return;
        _exitHooked = false;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    /// <summary>The last chance for changes still waiting. No events: the UI thread is gone by now.</summary>
    private void OnProcessExit(object? sender, EventArgs e) => FlushWithRetry(onExit: true);

    /// <summary>
    /// On the way out there is no later: a file an antivirus or sync client holds for a moment gets
    /// <see cref="JsonFileStore.ExitAttempts"/> tries, <see cref="JsonFileStore.ExitRetryDelay"/> apart.
    /// </summary>
    private void FlushWithRetry(bool onExit)
    {
        for (var attempt = 1; ; attempt++)
        {
            var result = FlushCore(raiseEvents: false, onExit);
            bool waiting;
            lock (_gate)
                waiting = !_disposed && _pending.Count > 0;
            if (result != FileWriteResult.Failed || !waiting || attempt >= JsonFileStore.ExitAttempts)
                return;
            Thread.Sleep(JsonFileStore.ExitRetryDelay);
        }
    }
}
