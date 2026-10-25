using System.Text.Json;

namespace Atap.Core.Sync;

/// <summary>
/// Splits layout.json and settings.json into sync units. Works on the JSON of the documents, so
/// it does not depend on their model classes. Every name it returns still goes through
/// <see cref="SyncScope"/>, so a Machine key found here is never synced.
/// </summary>
public static class SyncUnits
{
    /// <summary>
    /// Layout units: enabled widgets, states, compact priority, profiles and the settings of each
    /// widget instance. Placement is never a unit. Enabled widgets is always present, as an empty
    /// list when the document lacks it, because it shows the layout was read.
    /// </summary>
    public static Dictionary<string, JsonElement> FromLayout(JsonElement layout)
    {
        var units = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (layout.ValueKind != JsonValueKind.Object)
            return units;

        units[SyncUnitNames.EnabledWidgets] = PropertyOr(layout, "enabledWidgets", EmptyArray());
        AddIfPresent(units, layout, "states", SyncUnitNames.States);
        AddIfPresent(units, layout, "compactPriority", SyncUnitNames.CompactPriority);
        AddIfPresent(units, layout, "profiles", SyncUnitNames.Profiles);

        if (layout.TryGetProperty("widgetSettings", out var widgetSettings) && widgetSettings.ValueKind == JsonValueKind.Object)
        {
            foreach (var instance in widgetSettings.EnumerateObject())
            {
                if (instance.Value.ValueKind == JsonValueKind.Object && SyncUnitNames.IsInstanceId(instance.Name))
                    units[SyncUnitNames.WidgetSettingsPrefix + instance.Name] = instance.Value.Clone();
            }
        }
        return units;
    }

    /// <summary>
    /// Settings units: one per key in <c>values</c>, and the rules. Rules are always present, as an
    /// empty list when the document lacks them, because they show the settings were read. A key with
    /// a null value counts as unset.
    /// </summary>
    public static Dictionary<string, JsonElement> FromSettings(JsonElement settings)
    {
        var units = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (settings.ValueKind != JsonValueKind.Object)
            return units;

        units[SyncUnitNames.Rules] = PropertyOr(settings, "rules", EmptyArray());
        if (settings.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in values.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Null && SyncUnitNames.IsSettingKey(property.Name))
                    units[SyncUnitNames.SettingPrefix + property.Name] = property.Value.Clone();
            }
        }
        return units;
    }

    /// <summary>
    /// Both documents' units in one dictionary. Pass null for a document that was not loaded from
    /// its file, such as one with defaults in memory because the file is locked or unreadable.
    /// </summary>
    public static Dictionary<string, JsonElement> From(JsonElement? layout, JsonElement? settings)
    {
        var units = layout is { } layoutRoot ? FromLayout(layoutRoot) : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (settings is { } settingsRoot)
        {
            foreach (var (name, value) in FromSettings(settingsRoot))
                units[name] = value;
        }
        return units;
    }

    private static void AddIfPresent(Dictionary<string, JsonElement> units, JsonElement document, string property, string unitName)
    {
        if (document.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null)
            units[unitName] = value.Clone();
    }

    private static JsonElement PropertyOr(JsonElement document, string property, JsonElement fallback) =>
        document.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.Clone() : fallback;

    private static JsonElement EmptyArray() => SyncValues.Parse("[]"u8);
}
