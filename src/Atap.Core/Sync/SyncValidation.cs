using System.Text.Json;

namespace Atap.Core.Sync;

/// <summary>
/// The validation hook. The App checks a remote value before it is applied, with
/// <c>SettingValidator</c> for settings keys and layout normalization for layout units.
/// </summary>
/// <remarks>
/// The value has already passed <see cref="SyncUnitShapes.Fits"/>, has no secret references from
/// the bundle, and has this PC's secret references, Machine fields and Secret fields put back.
/// Return the value exactly as the store will hold it after applying, so the next stamp sees the
/// same hash and the unit does not bounce back as a new change.
/// </remarks>
/// <param name="unitName">The unit, such as <c>settings:malaysia.state</c>.</param>
/// <param name="value">The value to check.</param>
public delegate SyncValidation SyncUnitValidator(string unitName, JsonElement value);

/// <summary>The answer of a <see cref="SyncUnitValidator"/>.</summary>
public readonly struct SyncValidation
{
    private SyncValidation(bool accepted, JsonElement value, string? reason)
    {
        IsAccepted = accepted;
        Value = value;
        Reason = reason;
    }

    public bool IsAccepted { get; }

    /// <summary>The value to apply, possibly normalized. Only set when accepted.</summary>
    public JsonElement Value { get; }

    /// <summary>Why the value was refused, for the log. Must not contain the value.</summary>
    public string? Reason { get; }

    public static SyncValidation Accept(JsonElement value) => new(true, value, null);

    public static SyncValidation Reject(string reason) => new(false, default, reason);
}

/// <summary>The JSON kind each known unit must have. A remote value of another kind is never applied.</summary>
public static class SyncUnitShapes
{
    public static bool Fits(string unitName, JsonElement value)
    {
        switch (unitName)
        {
            case SyncUnitNames.EnabledWidgets:
            case SyncUnitNames.CompactPriority:
                return IsArrayOf(value, JsonValueKind.String);
            case SyncUnitNames.Rules:
            case SyncUnitNames.Profiles:
                return IsArrayOf(value, JsonValueKind.Object);
            case SyncUnitNames.States:
                return value.ValueKind == JsonValueKind.Object;
        }
        if (unitName.StartsWith(SyncUnitNames.WidgetSettingsPrefix, StringComparison.Ordinal))
            return value.ValueKind == JsonValueKind.Object;
        // A settings key may hold any kind but null. SettingValidator checks it against its definition.
        // The store reads a null value as unset, so applying one would come back as a deletion.
        return value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
    }

    private static bool IsArrayOf(JsonElement value, JsonValueKind kind)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != kind)
                return false;
        }
        return true;
    }
}
