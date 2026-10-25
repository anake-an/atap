using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Atap.Core.Storage;

namespace Atap.Core.Layout;

/// <summary>Loads and saves layout.json, and serves it as the live <see cref="ILayoutService"/>.</summary>
/// <remarks>
/// Built on <see cref="JsonFileStore{T}"/>: a change applies in memory at once and reaches the file 500 ms after
/// the last one, on top of the file as it is then, so hand edits survive. The file is watched, and an outside
/// change raises <see cref="Changed"/>. Use it on the UI thread, the one it was made on.
/// </remarks>
public sealed class LayoutStore : ILayoutService, IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly JsonFileStore<LayoutDocument> _file;
    private EffectiveLayout _active;
    private string _activeProfileId = EffectiveLayout.DefaultProfileId;
    private Func<string, bool>? _worksWithoutPlacement;

    /// <param name="filePath">Layout file path; defaults to <see cref="DefaultFilePath"/>.</param>
    /// <param name="createDefaults">Factory for the default layout; defaults to <see cref="DefaultLayout.Create"/>.</param>
    /// <param name="timeProvider">Clock for the write and watch delays and for corrupt-file backup names.</param>
    /// <param name="ui">Where outside changes are applied and <see cref="Changed"/> raised. Defaults to the current synchronization context.</param>
    /// <param name="watch">Reports outside changes to the file; defaults to <see cref="FolderWatch.Shared"/>.</param>
    public LayoutStore(
        string? filePath = null,
        Func<LayoutDocument>? createDefaults = null,
        TimeProvider? timeProvider = null,
        SynchronizationContext? ui = null,
        IFileWatch? watch = null)
    {
        var format = new JsonFileFormat<LayoutDocument>
        {
            CurrentVersion = LayoutDocument.CurrentSchemaVersion,
            CreateDefaults = createDefaults ?? DefaultLayout.Create,
            Parse = Parse,
            Serialize = layout =>
            {
                layout.SchemaVersion = LayoutDocument.CurrentSchemaVersion;
                return JsonSerializer.SerializeToUtf8Bytes(layout, JsonOptions);
            },
            Normalize = layout => layout.Normalize(),
            Migrations = LayoutMigrations.All,
        };
        _file = new JsonFileStore<LayoutDocument>(filePath ?? DefaultFilePath, format, timeProvider, ui, watch ?? FolderWatch.Shared);
        _file.Reloaded += OnReloaded;
        _active = LayoutResolver.Resolve(_file.Document, _activeProfileId, _worksWithoutPlacement);
    }

    /// <summary>%APPDATA%\Atap\layout.json</summary>
    public static string DefaultFilePath => AtapPaths.Default.LayoutFile;

    public string FilePath => _file.FilePath;

    public FileLoadOutcome LastLoadOutcome => _file.LastLoadOutcome;

    public LayoutDocument Document => _file.Document;

    public EffectiveLayout Active => _active;

    public string ActiveProfileId => _activeProfileId;

    public bool IsReadOnly => _file.IsReadOnly;

    /// <summary>
    /// True for instances that keep running without a placement, such as alarms. The app sets it once from the widget
    /// catalog, before anything reads <see cref="Active"/>, so a profile with its own widget list still runs them.
    /// </summary>
    public Func<string, bool>? WorksWithoutPlacement
    {
        get => _worksWithoutPlacement;
        set
        {
            _worksWithoutPlacement = value;
            _active = LayoutResolver.Resolve(_file.Document, _activeProfileId, value);
        }
    }

    /// <summary>Why changes are not reaching the file right now, for a banner.</summary>
    public StoreProblem Problem => _file.Problem;

    /// <summary>True while changes wait to be written.</summary>
    public bool HasPendingChanges => _file.HasPendingChanges;

    public event EventHandler? Changed;

    /// <summary>The file was written.</summary>
    public event EventHandler? Saved
    {
        add => _file.Saved += value;
        remove => _file.Saved -= value;
    }

    public event EventHandler? ProblemChanged
    {
        add => _file.ProblemChanged += value;
        remove => _file.ProblemChanged -= value;
    }

    /// <summary>
    /// Loads the layout. A missing file gets defaults written; a corrupt file is renamed to
    /// layout.corrupt-&lt;timestamp&gt;.json and replaced with defaults; a file from an older schema is
    /// backed up as layout.v&lt;n&gt;.bak.json, upgraded and saved back; a file from a newer schema is
    /// used read-only, or replaced by defaults in memory when its shape cannot be read.
    /// </summary>
    public LayoutDocument Load()
    {
        _file.Load();
        _active = LayoutResolver.Resolve(_file.Document, _activeProfileId, _worksWithoutPlacement);
        return _file.Document;
    }

    /// <summary>
    /// Saves <paramref name="layout"/> at once and atomically, skipping the read-only and newer-file checks.
    /// Tests only: the app goes through <see cref="Update"/>.
    /// </summary>
    internal void Save(LayoutDocument layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        AtomicFile.Write(FilePath, stream => JsonSerializer.Serialize(stream, layout, JsonOptions));
    }

    public void SetActiveProfile(string profileId)
    {
        var id = _file.Document.FindProfile(profileId) is null ? EffectiveLayout.DefaultProfileId : profileId;
        if (id == _activeProfileId)
            return;
        _activeProfileId = id;
        _active = LayoutResolver.Resolve(_file.Document, id, _worksWithoutPlacement);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the layout in memory at once and queues it. 500 ms after the last
    /// change it is applied again to the file as it is on disk then, and saved, so edits made by hand while
    /// Atap runs are kept. A missing file gets the whole layout. Nothing is written when the session is read-only.
    /// </summary>
    public LayoutWriteResult Update(Action<LayoutDocument> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var result = _file.Update(change);
        // The change may have deleted the active profile.
        if (_file.Document.FindProfile(_activeProfileId) is null)
            _activeProfileId = EffectiveLayout.DefaultProfileId;
        _active = LayoutResolver.Resolve(_file.Document, _activeProfileId, _worksWithoutPlacement);
        Changed?.Invoke(this, EventArgs.Empty);
        return Map(result);
    }

    /// <summary>Writes queued changes now. Use it before the process ends.</summary>
    public LayoutWriteResult Flush() => Map(_file.Flush());

    /// <summary>Writes queued changes, then stops watching the file.</summary>
    public void Dispose() => _file.Dispose();

    /// <summary>Typed parse. Null when the JSON has the wrong shape.</summary>
    private static LayoutDocument? Parse(JsonObject root)
    {
        try
        {
            return root.Deserialize<LayoutDocument>(JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static LayoutWriteResult Map(FileWriteResult result) => result switch
    {
        FileWriteResult.Applied => LayoutWriteResult.Applied,
        FileWriteResult.ReadOnly => LayoutWriteResult.ReadOnly,
        _ => LayoutWriteResult.Failed,
    };

    /// <summary>The file changed outside Atap. A profile it no longer has falls back to the Default.</summary>
    private void OnReloaded(object? sender, EventArgs e)
    {
        if (_file.Document.FindProfile(_activeProfileId) is null)
            _activeProfileId = EffectiveLayout.DefaultProfileId;
        _active = LayoutResolver.Resolve(_file.Document, _activeProfileId, _worksWithoutPlacement);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
