using System.Text.Json;
using System.Text.Json.Nodes;
using Atap.Core.Storage;
using Atap.Core.Widgets;

namespace Atap.Core.Settings;

/// <summary>
/// App settings in %APPDATA%\Atap\settings.json: <c>{ "schemaVersion": 1, "values": { ... }, "rules": [ ... ] }</c>.
/// A missing file is not created until the first <see cref="Set"/>, so its absence still tells an
/// upgraded install apart from a new one.
/// </summary>
/// <remarks>
/// Built on <see cref="JsonFileStore{T}"/> over the raw JSON, so keys and fields this version does not know
/// round-trip. A change applies in memory at once and reaches the file 500 ms after the last one, on top of
/// the file as it is then, so hand edits survive. The file is watched, and an outside change raises
/// <see cref="Changed"/> with the keys that differ. Use it on the UI thread, the one it was made on.
/// </remarks>
public sealed class SettingsStore : IAppSettings, IDisposable
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonElement NoRules = JsonSerializer.SerializeToElement(new JsonArray());

    private readonly JsonFileStore<JsonObject> _file;
    private readonly List<SettingDefinition> _definitions = [];
    private Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private SettingValues _view = SettingValues.Empty;
    private JsonElement _rules = NoRules;

    /// <param name="filePath">The settings file.</param>
    /// <param name="time">Clock for the write and watch delays and for corrupt-file backup names.</param>
    /// <param name="ui">Where outside changes are applied and <see cref="Changed"/> raised. Defaults to the current synchronization context.</param>
    /// <param name="watch">Reports outside changes to the file; defaults to <see cref="FolderWatch.Shared"/>.</param>
    public SettingsStore(string filePath, TimeProvider? time = null, SynchronizationContext? ui = null, IFileWatch? watch = null)
    {
        var format = new JsonFileFormat<JsonObject>
        {
            CurrentVersion = CurrentSchemaVersion,
            CreateDefaults = () => new JsonObject { ["schemaVersion"] = CurrentSchemaVersion },
            Parse = root => root,
            Serialize = Serialize,
            WriteDefaultsAtLoad = false,
        };
        _file = new JsonFileStore<JsonObject>(filePath, format, time, ui, watch ?? FolderWatch.Shared);
        _file.Reloaded += OnReloaded;
    }

    public string FilePath => _file.FilePath;

    public FileLoadOutcome LastLoadOutcome => _file.LastLoadOutcome;

    /// <summary>True when settings.json existed at load.</summary>
    public bool FileExisted { get; private set; }

    public bool IsReadOnly => _file.IsReadOnly;

    /// <summary>Why changes are not reaching the file right now, for a banner.</summary>
    public StoreProblem Problem => _file.Problem;

    /// <summary>True while changes wait to be written.</summary>
    public bool HasPendingChanges => _file.HasPendingChanges;

    public SettingValues Values => _view;

    public JsonElement Rules => _rules;

    public event EventHandler<SettingsChangedEventArgs>? Changed;

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

    public FileLoadOutcome Load()
    {
        FileExisted = File.Exists(FilePath);
        var outcome = _file.Load();
        Refresh();
        return outcome;
    }

    /// <summary>Adds definitions from feature descriptors, so their defaults and limits apply.</summary>
    public void Register(IEnumerable<SettingDefinition> definitions)
    {
        _definitions.AddRange(definitions);
        _view = new SettingValues(_definitions, _values);
    }

    public SettingWriteResult Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        JsonElement? element = value is null ? null : JsonSerializer.SerializeToElement(value, value.GetType());
        var definition = _definitions.Find(d => string.Equals(d.Key, key, StringComparison.Ordinal));
        if (element is { } candidate && definition is not null && !Fits(definition, candidate))
            return SettingWriteResult.Invalid;

        var had = _values.TryGetValue(key, out var old);
        if (element is null ? !had : had && old.GetRawText() == element.Value.GetRawText())
            return SettingWriteResult.Unchanged;

        var text = element?.GetRawText();
        var result = _file.Update(root =>
        {
            if (root["values"] is not JsonObject values)
            {
                values = [];
                root["values"] = values;
            }
            values.Remove(key);
            if (text is not null)
                values[key] = JsonNode.Parse(text);
        });
        Refresh();
        Changed?.Invoke(this, new SettingsChangedEventArgs([key]));
        return Map(result);
    }

    public SettingWriteResult SetRules(JsonElement rules)
    {
        if (rules.ValueKind != JsonValueKind.Array)
            return SettingWriteResult.Invalid;
        if (rules.GetRawText() == _rules.GetRawText())
            return SettingWriteResult.Unchanged;

        var text = rules.GetRawText();
        var result = _file.Update(root => root["rules"] = JsonNode.Parse(text));
        Refresh();
        Changed?.Invoke(this, new SettingsChangedEventArgs([SettingsChangedEventArgs.RulesKey]));
        return Map(result);
    }

    /// <summary>Writes queued changes now. Use it before the process ends.</summary>
    public SettingWriteResult Flush() => Map(_file.Flush());

    /// <summary>Writes queued changes, then stops watching the file.</summary>
    public void Dispose() => _file.Dispose();

    private static byte[] Serialize(JsonObject root)
    {
        root["schemaVersion"] = CurrentSchemaVersion;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            root.WriteTo(writer, WriteOptions);
        return buffer.ToArray();
    }

    /// <summary>Builds the values and rules from the JSON in memory. Each build is a new snapshot, so older ones never change.</summary>
    private void Refresh()
    {
        var root = _file.Document;
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root["values"] is JsonObject stored)
        {
            foreach (var (key, node) in stored)
            {
                if (node is not null)
                    values[key] = JsonSerializer.SerializeToElement(node);
            }
        }
        _values = values;
        _rules = root["rules"] is JsonArray rules ? JsonSerializer.SerializeToElement(rules) : NoRules;
        _view = new SettingValues(_definitions, _values);
    }

    /// <summary>The file changed outside Atap. Raises <see cref="Changed"/> with the keys that differ, if any.</summary>
    private void OnReloaded(object? sender, EventArgs e)
    {
        var oldValues = _values;
        var oldRules = _rules;
        Refresh();

        var changed = oldValues.Keys.Union(_values.Keys, StringComparer.Ordinal)
            .Where(key => !oldValues.TryGetValue(key, out var before) || !_values.TryGetValue(key, out var after) || before.GetRawText() != after.GetRawText())
            .ToList();
        if (oldRules.GetRawText() != _rules.GetRawText())
            changed.Add(SettingsChangedEventArgs.RulesKey);
        if (changed.Count > 0)
            Changed?.Invoke(this, new SettingsChangedEventArgs(changed));
    }

    private static SettingWriteResult Map(FileWriteResult result) => result switch
    {
        FileWriteResult.Applied => SettingWriteResult.Applied,
        FileWriteResult.ReadOnly => SettingWriteResult.ReadOnly,
        _ => SettingWriteResult.Failed,
    };

    private static bool Fits(SettingDefinition definition, JsonElement value) => definition.Type switch
    {
        SettingType.Bool => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        SettingType.Int or SettingType.Double or SettingType.Duration => value.ValueKind == JsonValueKind.Number,
        SettingType.StringList => value.ValueKind == JsonValueKind.Array,
        SettingType.Choice => value.ValueKind == JsonValueKind.String && (definition.Choices is null || definition.Choices.Contains(value.GetString()!)),
        // Only a "secret:<id>" reference. The secret itself goes to ISecretStore and never into this file, which syncs.
        SettingType.Secret => value.ValueKind == JsonValueKind.String && IsSecretRef(value.GetString()),
        SettingType.Custom => true,
        _ => value.ValueKind == JsonValueKind.String,
    };

    private static bool IsSecretRef(string? text) =>
        text is not null
        && text.StartsWith(SettingValues.SecretPrefix, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(text[SettingValues.SecretPrefix.Length..]);
}
