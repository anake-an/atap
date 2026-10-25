namespace Atap.Core.Sync;

/// <summary>The document a unit belongs to.</summary>
public enum SyncFamily
{
    /// <summary>A unit from settings.json.</summary>
    Settings,

    /// <summary>A unit from layout.json.</summary>
    Layout,

    /// <summary>A unit this version does not know, for example from a newer Atap.</summary>
    Other,
}

/// <summary>
/// Names of sync units. A unit is the smallest part of the settings that syncs on its own:
/// one settings key, the rules, one part of the layout, or the settings of one widget instance.
/// </summary>
public static class SyncUnitNames
{
    /// <summary>Longest type id, and longest name after <c>#</c>, in a widget instance id.</summary>
    private const int MaxIdPartLength = 32;

    public const string SettingPrefix = "settings:";

    public const string LayoutPrefix = "layout:";

    public const string WidgetSettingsPrefix = "layout:widgetSettings:";

    public const string Rules = "settings:rules";

    public const string EnabledWidgets = "layout:enabledWidgets";

    public const string States = "layout:states";

    public const string CompactPriority = "layout:compactPriority";

    public const string Profiles = "layout:profiles";

    /// <summary>Where the notch sits on this PC. Never synced.</summary>
    public const string Placement = "layout:placement";

    /// <summary>
    /// Units that always exist while their document exists. They are never deleted by sync, and
    /// a missing one means its document is missing, not that the user removed it.
    /// </summary>
    public static IReadOnlyList<string> FixedUnits { get; } = [Rules, EnabledWidgets, States, CompactPriority, Profiles];

    /// <summary>The unit that shows a document was read: rules for settings.json, enabled widgets for layout.json.</summary>
    public static string? SentinelFor(SyncFamily family) => family switch
    {
        SyncFamily.Settings => Rules,
        SyncFamily.Layout => EnabledWidgets,
        _ => null,
    };

    public static string Setting(string key)
    {
        if (!IsSettingKey(key))
            throw new ArgumentException($"'{key}' is not a settings key of the form <featureId>.<name>.", nameof(key));
        return SettingPrefix + key;
    }

    public static string WidgetSettings(string instanceId)
    {
        if (!IsInstanceId(instanceId))
            throw new ArgumentException($"'{instanceId}' is not a widget instance id.", nameof(instanceId));
        return WidgetSettingsPrefix + instanceId;
    }

    public static bool IsFixed(string unitName) => unitName is Rules or EnabledWidgets or States or CompactPriority or Profiles;

    public static SyncFamily FamilyOf(string unitName) =>
        unitName.StartsWith(SettingPrefix, StringComparison.Ordinal) ? SyncFamily.Settings
        : unitName.StartsWith(LayoutPrefix, StringComparison.Ordinal) ? SyncFamily.Layout
        : SyncFamily.Other;

    /// <summary>Gets the key of a <c>settings:&lt;key&gt;</c> unit. False for the rules unit and anything else.</summary>
    public static bool TryGetSettingKey(string unitName, out string key)
    {
        key = string.Empty;
        if (!unitName.StartsWith(SettingPrefix, StringComparison.Ordinal) || unitName == Rules)
            return false;
        var candidate = unitName[SettingPrefix.Length..];
        if (!IsSettingKey(candidate))
            return false;
        key = candidate;
        return true;
    }

    /// <summary>Gets the instance id of a <c>layout:widgetSettings:&lt;instanceId&gt;</c> unit.</summary>
    public static bool TryGetInstanceId(string unitName, out string instanceId)
    {
        instanceId = string.Empty;
        if (!unitName.StartsWith(WidgetSettingsPrefix, StringComparison.Ordinal))
            return false;
        var candidate = unitName[WidgetSettingsPrefix.Length..];
        if (!IsInstanceId(candidate))
            return false;
        instanceId = candidate;
        return true;
    }

    /// <summary>
    /// True for a name of the form <c>&lt;area&gt;:&lt;rest&gt;</c>: a lowercase area, then printable
    /// ASCII without spaces, at most <see cref="SyncLimits.MaxUnitNameLength"/> long. Anything
    /// else is never read, applied or written.
    /// </summary>
    public static bool IsWellFormed(string? unitName)
    {
        if (string.IsNullOrEmpty(unitName) || unitName.Length > SyncLimits.MaxUnitNameLength)
            return false;
        var colon = unitName.IndexOf(':');
        if (colon < 1 || colon == unitName.Length - 1 || !char.IsAsciiLetterLower(unitName[0]))
            return false;
        for (var i = 1; i < colon; i++)
        {
            if (!char.IsAsciiLetterOrDigit(unitName[i]))
                return false;
        }
        for (var i = colon + 1; i < unitName.Length; i++)
        {
            if (unitName[i] is < '!' or > '~')
                return false;
        }
        return true;
    }

    /// <summary>A key of the form <c>&lt;featureId&gt;.&lt;name&gt;</c>, such as <c>malaysia.state</c>.</summary>
    public static bool IsSettingKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128)
            return false;
        var segmentLength = 0;
        var dots = 0;
        foreach (var c in key)
        {
            if (c == '.')
            {
                if (segmentLength == 0)
                    return false;
                dots++;
                segmentLength = 0;
            }
            else if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            {
                segmentLength++;
            }
            else
            {
                return false;
            }
        }
        return dots > 0 && segmentLength > 0;
    }

    /// <summary>
    /// A widget instance id, by the same rule as <c>WidgetInstanceId</c>: a type id of 2 to 32
    /// lowercase letters, digits and dashes that starts with a letter, then optionally <c>#</c> and a
    /// name of 1 to 32 lowercase letters, digits and dashes that starts with a letter or digit.
    /// </summary>
    public static bool IsInstanceId(string? instanceId)
    {
        // Mirrors WidgetInstanceId.TryParse in Atap.Core.Widgets. Call it instead once it is on main.
        if (string.IsNullOrEmpty(instanceId))
            return false;
        var hash = instanceId.IndexOf('#');
        var typeId = hash < 0 ? instanceId.AsSpan() : instanceId.AsSpan(0, hash);
        if (typeId.Length is < 2 or > MaxIdPartLength || !char.IsAsciiLetterLower(typeId[0]) || !IsIdPart(typeId))
            return false;
        if (hash < 0)
            return true;
        var name = instanceId.AsSpan(hash + 1);
        return name.Length is >= 1 and <= MaxIdPartLength && IsLowerOrDigit(name[0]) && IsIdPart(name);
    }

    /// <summary>The widget type id of an instance id: the part before <c>#</c>.</summary>
    public static string TypeIdOf(string instanceId)
    {
        var hash = instanceId.IndexOf('#');
        return hash < 0 ? instanceId : instanceId[..hash];
    }

    private static bool IsLowerOrDigit(char c) => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c);

    private static bool IsIdPart(ReadOnlySpan<char> part)
    {
        foreach (var c in part)
        {
            if (!(IsLowerOrDigit(c) || c == '-'))
                return false;
        }
        return true;
    }
}
