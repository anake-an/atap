using System.IO;
using System.Text.Json;
using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Core.Sync;
using Atap.Core.Widgets;
using Atap.Diagnostics;
using Atap.Features.Sync;
using Atap.Platform.OneDrive;

[assembly: AtapFeature(typeof(SyncDescriptor))]

namespace Atap.Features.Sync;

/// <summary>
/// OneDrive sync of settings and layout. Off by default. The feature, its watcher and its timers exist only
/// while it is on, and Atap itself makes no network call for it: it reads and writes a file in the OneDrive
/// folder, and the OneDrive app does the rest.
/// </summary>
internal sealed class SyncDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "sync";
    public const string EnabledKey = "sync.enabled";

    /// <summary>The OneDrive folder the user chose, for a PC where it is not found. Empty means "find it".</summary>
    public const string FolderKey = "sync.folder";

    // A file reset to defaults yields to OneDrive once per session, not on every turn on.
    private bool _resetHandled;

    public string Id => FeatureId;

    /// <summary>Both Machine scope, and always Machine keys in sync too. The Sync card on the General page shows them.</summary>
    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(EnabledKey, "sync.enabled", SettingType.Bool, false) { Scope = SettingScope.Machine },
        new(FolderKey, "sync.folder", SettingType.Folder, string.Empty) { Scope = SettingScope.Machine },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [EnabledKey];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode == AppMode.Normal && settings.GetBool(EnabledKey);

    public IAppFeature Create(FeatureContext context)
    {
        var services = context.Services;
        var reset = new List<SyncFamily>();
        if (!_resetHandled)
        {
            _resetHandled = true;
            if (services.Settings.LastLoadOutcome == FileLoadOutcome.RecoveredFromCorrupt)
                reset.Add(SyncFamily.Settings);
            if (services.Layout.LastLoadOutcome == FileLoadOutcome.RecoveredFromCorrupt)
                reset.Add(SyncFamily.Layout);
        }
        var environment = new WindowsOneDriveEnvironment();
        return new SyncFeature(
            SyncParts.Stores(context.Settings, context.Layout, services.Features, context.Widgets.Catalog),
            SyncParts.Scope(services.Features, context.Widgets.Catalog),
            new SyncLocalFiles(context.Paths),
            () => OneDriveLocator.SyncFolder(context.Settings.Values.GetString(FolderKey), environment),
            new SyncFolderWatch(),
            context.Scheduler,
            context.Time,
            context.Ui,
            reset,
            Log.Warn);
    }
}

/// <summary>What the feature and the Sync card both build from the registered features and widgets.</summary>
internal static class SyncParts
{
    public static IEnumerable<SettingDefinition> AppSettings(IEnumerable<IFeatureDescriptor> features) => features.SelectMany(f => f.Settings);

    public static SyncScope Scope(IEnumerable<IFeatureDescriptor> features, WidgetCatalog catalog) =>
        SyncDefinitions.Scope(AppSettings(features), catalog.Descriptors.Select(d => (d.TypeId, d.Settings)));

    public static SyncStores Stores(IAppSettings settings, ILayoutService layout, IEnumerable<IFeatureDescriptor> features, WidgetCatalog catalog) =>
        new(settings, layout, AppSettings(features), typeId => catalog.TryGet(typeId, out var descriptor) ? descriptor.Settings : null);
}

internal enum SyncHealth
{
    Off,
    Working,

    /// <summary>No OneDrive folder was found and none was chosen.</summary>
    NoFolder,

    /// <summary>The sync file is there but will not open yet, for example while OneDrive is offline.</summary>
    Waiting,

    /// <summary>The sync file is from a newer Atap. It is never written over.</summary>
    NewerFile,

    /// <summary>The sync file is too big or too deep to read. It is never written over.</summary>
    DamagedFile,

    /// <summary>The sync file could not be written. The next change or folder event tries again.</summary>
    CannotWrite,
}

/// <summary>How sync is doing, for the Sync card. Set on the UI thread.</summary>
internal static class SyncStatus
{
    public static SyncHealth Health { get; private set; }

    public static event EventHandler? Changed;

    public static void Set(SyncHealth health)
    {
        if (health == Health)
            return;
        Health = health;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>
/// Stamps each local settings or layout change, exports 5 seconds after the last one, and imports at start
/// and 300 ms after the sync folder changes. Export and import are one merge pass. Use it on the UI thread.
/// </summary>
internal sealed class SyncFeature : IAppFeature
{
    public static readonly TimeSpan ExportDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>A conflict copy that would not open, such as while a virus scan reads it, is tried again after this.</summary>
    public static readonly TimeSpan CopyRetryDelay = TimeSpan.FromSeconds(5);

    private readonly SyncStores _stores;
    private readonly SyncScope _scope;
    private readonly SyncLocalFiles _local;
    private readonly Func<string?> _findFolder;
    private readonly ISyncFolderWatch _watch;
    private readonly IScheduler _scheduler;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext _ui;
    private readonly IReadOnlyCollection<SyncFamily> _resetDocuments;
    private readonly Action<string, Exception?> _log;
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);
    private SyncFolderFiles? _folder;
    private SyncState? _state;
    private IDisposable? _watcher;
    private IDisposable? _export;
    private IDisposable? _import;
    private bool _applying;
    private bool _disposed;

    /// <param name="stores">Settings and layout.</param>
    /// <param name="scope">Which units sync.</param>
    /// <param name="local">sync-state.json.</param>
    /// <param name="findFolder">The sync folder, or null when there is none.</param>
    /// <param name="watch">Watches the sync folder.</param>
    /// <param name="scheduler">The export and settle timers.</param>
    /// <param name="time">Stamps and timer due times.</param>
    /// <param name="ui">Where folder events are handled.</param>
    /// <param name="resetDocuments">Documents reset to defaults at load, which take the OneDrive copy.</param>
    /// <param name="log">Warnings.</param>
    public SyncFeature(
        SyncStores stores,
        SyncScope scope,
        SyncLocalFiles local,
        Func<string?> findFolder,
        ISyncFolderWatch watch,
        IScheduler scheduler,
        TimeProvider time,
        SynchronizationContext ui,
        IReadOnlyCollection<SyncFamily> resetDocuments,
        Action<string, Exception?> log)
    {
        _stores = stores;
        _scope = scope;
        _local = local;
        _findFolder = findFolder;
        _watch = watch;
        _scheduler = scheduler;
        _time = time;
        _ui = ui;
        _resetDocuments = resetDocuments;
        _log = log;
    }

    /// <summary>The sync folder in use, or null when there is none.</summary>
    public string? Folder => _folder?.Folder;

    public Task StartAsync()
    {
        var local = _stores.ReadUnits();
        var saved = _local.ReadState();
        // No saved state: this PC takes the OneDrive copy, so a lost state never overwrites the other PCs.
        _state = saved ?? SyncStamper.Initialize(Guid.NewGuid(), local, _scope, SyncStartChoice.UseOneDrive, null, _time.GetUtcNow());
        foreach (var family in _resetDocuments)
            _state = SyncStamper.YieldToOneDrive(_state, family, local, _scope);
        if (saved is null || _resetDocuments.Count > 0)
            _local.TryWriteState(_state, _log);

        _stores.Settings.Changed += OnSettingsChanged;
        _stores.Layout.Changed += OnLayoutChanged;
        OpenFolder();
        return Task.CompletedTask;
    }

    /// <summary>A change still waiting for its export goes out now.</summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _stores.Settings.Changed -= OnSettingsChanged;
        _stores.Layout.Changed -= OnLayoutChanged;
        if (_export is not null)
            RunPass();
        _disposed = true;
        CloseFolder();
        SyncStatus.Set(SyncHealth.Off);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// One merge pass: reads the bundle and its conflict copies, applies what is newer there, writes what is
    /// newer here, then deletes the copies it merged.
    /// </summary>
    public void RunPass()
    {
        CancelTimers();
        if (_disposed || _folder is not { } folder || _state is null)
            return;

        var main = folder.ReadMain();
        if (main.Unavailable)
        {
            // A cloud-only file that will not open now. The next folder event or start tries again.
            LogOnce("unavailable", "Sync is waiting for atap-sync.json to open.");
            SyncStatus.Set(SyncHealth.Waiting);
            return;
        }
        if (main.Outcome is { } outcome and not SyncBundleReadOutcome.Loaded)
            LogOnce("main:" + outcome, $"Sync ignored atap-sync.json: {outcome}.");

        var copies = folder.ReadCopies(out var copyWaiting);
        var result = SyncMerger.Merge(new SyncMergeRequest
        {
            State = _state,
            Scope = _scope,
            LocalUnits = _stores.ReadUnits(),
            Main = main.Bundle,
            MainCanBeReplaced = main.CanBeReplaced,
            ConflictCopies = copies.Select(c => c.Bundle).ToList(),
            Validator = _stores.Validate,
            Now = _time.GetUtcNow(),
        });
        LogSkips(result.Skipped);

        // The state first, so the store writes that follow do not count as local changes.
        _state = result.State;
        IReadOnlyList<string> failed = [];
        if (result.Changes.Count > 0)
        {
            _applying = true;
            try
            {
                failed = _stores.Apply(result.Changes);
            }
            finally
            {
                _applying = false;
            }
        }
        if (failed.Count > 0)
            _state = result.WithoutChangesTo(failed);
        if (result.StateChanged || failed.Count > 0)
            _local.TryWriteState(_state, _log);

        var settled = result.BundleOutcome == SyncBundleOutcome.Unchanged;
        if (result.BundleOutcome == SyncBundleOutcome.Write)
            settled = TryWrite(folder, result.BundleJson!);
        if (settled)
        {
            foreach (var copy in copies)
                SyncFolderFiles.TryDelete(copy.Path);
        }

        SyncStatus.Set(result.BundleOutcome switch
        {
            SyncBundleOutcome.Blocked => main.Outcome == SyncBundleReadOutcome.NewerFormat ? SyncHealth.NewerFile : SyncHealth.DamagedFile,
            SyncBundleOutcome.TooLarge => SyncHealth.CannotWrite,
            _ => settled ? SyncHealth.Working : SyncHealth.CannotWrite,
        });

        // Before, a copy that would not open waited for the next folder event, which may never come.
        if (copyWaiting)
            _import = _scheduler.At(_time.GetUtcNow() + CopyRetryDelay, RunPass);
    }

    private bool TryWrite(SyncFolderFiles folder, string json)
    {
        try
        {
            folder.WriteBundle(json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log("Sync could not write atap-sync.json.", ex);
            return false;
        }
    }

    /// <summary>Finds the folder, starts watching it and runs the first pass.</summary>
    private void OpenFolder()
    {
        var path = _findFolder();
        if (path is null)
        {
            LogOnce("noFolder", "Sync found no OneDrive folder.");
            SyncStatus.Set(SyncHealth.NoFolder);
            return;
        }
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log("Sync could not make its folder in OneDrive.", ex);
        }
        _folder = new SyncFolderFiles(path);
        _watcher = _watch.Watch(path, OnFolderChanged);
        RunPass();
    }

    private void CloseFolder()
    {
        CancelTimers();
        _watcher?.Dispose();
        _watcher = null;
        _folder = null;
    }

    private void CancelTimers()
    {
        _export?.Dispose();
        _export = null;
        _import?.Dispose();
        _import = null;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(SyncDescriptor.FolderKey))
        {
            CloseFolder();
            OpenFolder();
        }
        Stamp();
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => Stamp();

    /// <summary>A local write. Units whose hash changed get a stamp, and the export waits for 5 quiet seconds.</summary>
    private void Stamp()
    {
        if (_applying || _disposed || _state is null)
            return;
        var stamped = SyncStamper.Stamp(_state, _stores.ReadUnits(), _scope, _time.GetUtcNow());
        if (ReferenceEquals(stamped, _state))
            return;
        _state = stamped;
        _local.TryWriteState(_state, _log);
        if (_folder is null)
            return;
        _export?.Dispose();
        _export = _scheduler.At(_time.GetUtcNow() + ExportDelay, RunPass);
    }

    /// <summary>Runs on any thread. The pass waits until the folder has been quiet for 300 ms.</summary>
    private void OnFolderChanged() => _ui.Post(_ =>
    {
        if (_disposed || _folder is null)
            return;
        _import?.Dispose();
        _import = _scheduler.At(_time.GetUtcNow() + SettleDelay, RunPass);
    }, null);

    private void LogSkips(IReadOnlyList<SyncSkip> skipped)
    {
        foreach (var skip in skipped)
        {
            // Excluded and unknown units are normal: Machine keys, placement, keys from another version.
            if (skip.Reason is SyncSkipReason.Excluded or SyncSkipReason.Unknown or SyncSkipReason.LocalOnly)
                continue;
            LogOnce($"{skip.UnitName}:{skip.Reason}", $"Sync skipped {skip.UnitName}: {skip.Reason}{(skip.Detail is null ? string.Empty : $" ({skip.Detail})")}.");
        }
    }

    private void LogOnce(string key, string message)
    {
        if (_logged.Add(key))
            _log(message, null);
    }
}

/// <summary>
/// Turning sync on for the first time when OneDrive already has a sync file: the user keeps one side, and
/// the other side is copied to %APPDATA%\Atap\backup-&lt;timestamp&gt; first.
/// </summary>
internal sealed class SyncSetup(SyncStores stores, SyncScope scope, SyncLocalFiles local, TimeProvider time)
{
    /// <summary>True when this PC has never synced and the folder already has a sync file.</summary>
    public bool NeedsChoice(string folder) => local.ReadState() is null && new SyncFolderFiles(folder).BundleExists;

    /// <summary>
    /// Backs up the losing side, then saves the state for the choice. Returns the backup folder, or null
    /// when the backup or the state could not be saved. Nothing has changed then.
    /// </summary>
    public string? Start(string folder, SyncStartChoice choice)
    {
        var now = time.GetUtcNow();
        var files = new SyncFolderFiles(folder);
        var main = files.ReadMain();
        var copies = files.ReadCopies(out _);
        string backup;
        try
        {
            if (choice == SyncStartChoice.KeepThisPc)
            {
                backup = local.Backup(now, [files.BundlePath, .. copies.Select(c => c.Path)]);
            }
            else
            {
                stores.Flush();
                backup = local.Backup(now, [local.Paths.SettingsFile, local.Paths.LayoutFile]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var units = stores.ReadUnits();
        var state = SyncStamper.Initialize(Guid.NewGuid(), units, scope, choice, main.Bundle, now, copies.Select(c => c.Bundle).ToList());
        return local.TryWriteState(state) ? backup : null;
    }
}
