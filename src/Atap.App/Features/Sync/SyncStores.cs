using System.Buffers;
using System.Text.Json;
using Atap.Core.Layout;
using Atap.Core.Rules;
using Atap.Core.Settings;
using Atap.Core.Sync;
using Atap.Core.Widgets;

namespace Atap.Features.Sync;

/// <summary>
/// Sync's view of settings.json and layout.json: their units as they are now, the check a remote value
/// must pass, and the narrowest write that applies a unit. Use it on the UI thread, like the stores.
/// </summary>
internal sealed class SyncStores
{
    private readonly Dictionary<string, SettingDefinition> _appSettings = new(StringComparer.Ordinal);
    private readonly Func<string, IReadOnlyList<SettingDefinition>?> _widgetSettings;

    /// <param name="settings">The app settings.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="appSettings">Every registered app setting, to check remote settings values.</param>
    /// <param name="widgetSettings">The settings of a widget type id, or null for a type this PC does not know.</param>
    public SyncStores(
        IAppSettings settings,
        ILayoutService layout,
        IEnumerable<SettingDefinition> appSettings,
        Func<string, IReadOnlyList<SettingDefinition>?> widgetSettings)
    {
        Settings = settings;
        Layout = layout;
        foreach (var definition in appSettings)
            _appSettings.TryAdd(definition.Key, definition);
        _widgetSettings = widgetSettings;
    }

    public IAppSettings Settings { get; }

    public ILayoutService Layout { get; }

    /// <summary>
    /// Every unit of both documents. A document that is read-only (unreadable, or from a newer Atap) is left
    /// out, so its defaults in memory never reach other PCs.
    /// </summary>
    public Dictionary<string, JsonElement> ReadUnits() => SyncUnits.From(
        Layout.IsReadOnly ? null : JsonSerializer.SerializeToElement(Layout.Document, LayoutStore.JsonOptions),
        Settings.IsReadOnly ? null : SettingsJson());

    /// <summary>Writes queued store changes now, so the files on disk match what is in memory.</summary>
    public void Flush()
    {
        Settings.Flush();
        Layout.Flush();
    }

    /// <summary>The merge's validation hook: <see cref="SettingValidator"/> for settings keys, layout normalization for layout units.</summary>
    public SyncValidation Validate(string unitName, JsonElement value)
    {
        if (unitName == SyncUnitNames.Rules)
            return RuleJson.Parse(value.GetRawText()) is null ? SyncValidation.Reject("not a list of rules") : SyncValidation.Accept(value);
        if (SyncUnitNames.TryGetSettingKey(unitName, out var key))
        {
            return _appSettings.TryGetValue(key, out var definition)
                ? SyncDefinitions.Check(definition, value)
                : SyncValidation.Reject("not registered");
        }
        if (SyncUnitNames.TryGetInstanceId(unitName, out var instanceId))
            return ValidateWidgetSettings(instanceId, value);
        return ValidateLayout(unitName, value);
    }

    /// <summary>Applies the merge's changes. Returns the units that could not be applied.</summary>
    public IReadOnlyList<string> Apply(IReadOnlyList<SyncChange> changes)
    {
        var failed = new List<string>();
        var layoutChanges = new List<SyncChange>();
        foreach (var change in changes)
        {
            switch (SyncUnitNames.FamilyOf(change.UnitName))
            {
                case SyncFamily.Settings:
                    if (!ApplySetting(change))
                        failed.Add(change.UnitName);
                    break;
                case SyncFamily.Layout:
                    layoutChanges.Add(change);
                    break;
                default:
                    failed.Add(change.UnitName);
                    break;
            }
        }

        if (layoutChanges.Count > 0)
        {
            // One write and one Changed for the whole layout. Each run deserializes again, because the store
            // applies the change to the document in memory and later to the file as it is on disk.
            var result = Layout.Update(document =>
            {
                foreach (var change in layoutChanges)
                {
                    if (change.Kind == SyncChangeKind.Set && change.Value is { } value)
                        SetLayoutUnit(document, change.UnitName, value);
                    else if (SyncUnitNames.TryGetInstanceId(change.UnitName, out var instanceId))
                        document.WidgetSettings.Remove(instanceId);
                }
            });
            if (result == LayoutWriteResult.ReadOnly)
                failed.AddRange(layoutChanges.Select(c => c.UnitName));
        }
        return failed;
    }

    private bool ApplySetting(SyncChange change)
    {
        SettingWriteResult result;
        if (change.UnitName == SyncUnitNames.Rules)
        {
            if (change.Value is not { } rules)
                return false;
            result = Settings.SetRules(rules);
        }
        else if (SyncUnitNames.TryGetSettingKey(change.UnitName, out var key))
        {
            result = Settings.Set(key, change.Kind == SyncChangeKind.Set ? change.Value : null);
        }
        else
        {
            return false;
        }
        return result is not (SettingWriteResult.Invalid or SettingWriteResult.ReadOnly);
    }

    /// <summary>settings.json as SyncUnits reads it: the values and the rules.</summary>
    private JsonElement SettingsJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("values");
            foreach (var (key, value) in Settings.Values.Raw)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.WritePropertyName("rules");
            Settings.Rules.WriteTo(writer);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <summary>Each field the widget type defines is checked like a settings value. Fields it does not define are kept.</summary>
    private SyncValidation ValidateWidgetSettings(string instanceId, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return SyncValidation.Reject("not an object");
        var definitions = _widgetSettings(SyncUnitNames.TypeIdOf(instanceId));
        if (definitions is null)
            return SyncValidation.Accept(value);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                var field = property.Value;
                if (field.ValueKind != JsonValueKind.Null && definitions.FirstOrDefault(d => d.Key == property.Name) is { } definition)
                {
                    var check = SyncDefinitions.Check(definition, field);
                    if (!check.IsAccepted)
                        return SyncValidation.Reject($"{property.Name}: {check.Reason}");
                    field = check.Value;
                }
                writer.WritePropertyName(property.Name);
                field.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return SyncValidation.Accept(document.RootElement.Clone());
    }

    /// <summary>
    /// Puts the unit into a copy of the layout and normalizes it as the store does, then reads the unit back.
    /// That is the value the store will hold, so it hashes the same after it is applied.
    /// </summary>
    private SyncValidation ValidateLayout(string unitName, JsonElement value)
    {
        try
        {
            var copy = JsonSerializer.Deserialize<LayoutDocument>(JsonSerializer.SerializeToUtf8Bytes(Layout.Document, LayoutStore.JsonOptions), LayoutStore.JsonOptions);
            if (copy is null)
                return SyncValidation.Reject("no layout");
            SetLayoutUnit(copy, unitName, value);
            copy.Normalize();
            return SyncUnits.FromLayout(JsonSerializer.SerializeToElement(copy, LayoutStore.JsonOptions)).TryGetValue(unitName, out var normalized)
                ? SyncValidation.Accept(normalized)
                : SyncValidation.Reject("dropped by the layout");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return SyncValidation.Reject("does not fit the layout");
        }
    }

    private static void SetLayoutUnit(LayoutDocument document, string unitName, JsonElement value)
    {
        var options = LayoutStore.JsonOptions;
        switch (unitName)
        {
            case SyncUnitNames.EnabledWidgets:
                document.EnabledWidgets = value.Deserialize<List<string>>(options) ?? [];
                return;
            case SyncUnitNames.States:
                document.States = value.Deserialize<StateLayouts>(options) ?? new();
                return;
            case SyncUnitNames.CompactPriority:
                document.CompactPriority = value.Deserialize<List<string>>(options) ?? [];
                return;
            case SyncUnitNames.Profiles:
                document.Profiles = value.Deserialize<List<LayoutProfile>>(options) ?? [];
                return;
        }
        if (!SyncUnitNames.TryGetInstanceId(unitName, out var instanceId))
            throw new InvalidOperationException($"'{unitName}' is not a layout unit.");
        var settings = value.Deserialize<Dictionary<string, JsonElement>>(options) ?? [];
        document.WidgetSettings[instanceId] = new Dictionary<string, JsonElement>(settings, StringComparer.Ordinal);
    }
}
