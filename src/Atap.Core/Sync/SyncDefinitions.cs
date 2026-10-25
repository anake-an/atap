using System.Text.Json;
using Atap.Core.Settings;
using Atap.Core.Widgets;

namespace Atap.Core.Sync;

/// <summary>
/// Ties sync to the setting definitions: which keys sync, and whether a value from the bundle fits its
/// definition. The App builds the scope from the feature and widget descriptors, and checks each remote
/// settings value here before it is applied.
/// </summary>
public static class SyncDefinitions
{
    /// <summary>
    /// The scope for these definitions. App settings with scope Synced sync. Machine ones and every Secret
    /// one never do. Widget settings with scope Machine or type Secret stay on this PC.
    /// </summary>
    /// <param name="appSettings">Every registered app setting.</param>
    /// <param name="widgetSettings">Each widget type id with its settings.</param>
    public static SyncScope Scope(
        IEnumerable<SettingDefinition> appSettings,
        IEnumerable<(string TypeId, IReadOnlyList<SettingDefinition> Settings)> widgetSettings)
    {
        ArgumentNullException.ThrowIfNull(appSettings);
        ArgumentNullException.ThrowIfNull(widgetSettings);

        var app = appSettings.ToList();
        var widgets = widgetSettings.SelectMany(w => w.Settings.Select(s => (w.TypeId, Setting: s))).ToList();
        return new SyncScope(
            app.Where(s => s.Scope == SettingScope.Synced && s.Type != SettingType.Secret).Select(s => s.Key),
            app.Where(s => s.Scope == SettingScope.Machine).Select(s => s.Key),
            widgets.Where(w => w.Setting.Scope == SettingScope.Machine).Select(w => (w.TypeId, w.Setting.Key)),
            app.Where(s => s.Type == SettingType.Secret).Select(s => s.Key),
            widgets.Where(w => w.Setting.Type == SettingType.Secret).Select(w => (w.TypeId, w.Setting.Key)));
    }

    /// <summary>
    /// Checks a remote value with <see cref="SettingValidator"/> and returns it as the settings store will
    /// hold it, so the next stamp sees the same hash. A value that only means "back to the default" is
    /// refused, because a set unit must keep a value.
    /// </summary>
    public static SyncValidation Check(SettingDefinition definition, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Type == SettingType.Custom)
            return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? SyncValidation.Reject("empty") : SyncValidation.Accept(value);
        if (Typed(definition.Type, value) is not { } typed)
            return SyncValidation.Reject("wrong type");

        var check = SettingValidator.Check(definition, typed);
        if (!check.IsValid)
            return SyncValidation.Reject(check.MessageKey ?? "invalid");
        if (check.Value is null)
            return SyncValidation.Reject("empty");
        return SyncValidation.Accept(JsonSerializer.SerializeToElement(check.Value, check.Value.GetType()));
    }

    /// <summary>The value as <see cref="SettingValidator.Check"/> takes it, or null when its JSON kind is wrong.</summary>
    private static object? Typed(SettingType type, JsonElement value) => type switch
    {
        SettingType.Bool => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        },
        SettingType.Int or SettingType.Double or SettingType.Duration =>
            value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null,
        SettingType.StringList => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(i => i.ValueKind == JsonValueKind.String)
            ? value.EnumerateArray().Select(i => i.GetString()!).ToList()
            : null,
        _ => value.ValueKind == JsonValueKind.String ? value.GetString() : null,
    };
}
