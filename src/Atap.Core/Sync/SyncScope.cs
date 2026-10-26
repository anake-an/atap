using System.Diagnostics.CodeAnalysis;

namespace Atap.Core.Sync;

public enum SyncUnitKind
{
    /// <summary>Syncs on this PC: stamped, exported and imported.</summary>
    Synced,

    /// <summary>
    /// Never syncs: placement, Machine keys, Secret keys and malformed names. Never exported, never
    /// imported, and dropped from any bundle this PC writes.
    /// </summary>
    Excluded,

    /// <summary>
    /// Not known here, for example a key from a newer Atap. Never applied, but carried through
    /// unchanged when this PC writes the bundle, so PCs on different versions do not fight.
    /// </summary>
    Unknown,
}

/// <summary>
/// Decides which units sync on this PC. Built from the setting definitions: keys whose scope is
/// Synced sync, keys whose scope is Machine or whose type is Secret never do, and a fixed list of
/// Machine keys can never sync whatever the definitions say.
/// </summary>
public sealed class SyncScope
{
    private static readonly string[] MachineFeaturePrefixes = ["startup.", "localApi.", "sync.", "onboarding.", "updates.", "ai."];

    private readonly HashSet<string> _syncedKeys;
    private readonly HashSet<string> _machineKeys;
    private readonly HashSet<string> _secretKeys;
    private readonly Dictionary<string, HashSet<string>> _machineWidgetKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _secretWidgetKeys = new(StringComparer.Ordinal);

    /// <summary>Machine and Secret widget settings together: the fields that stay on this PC.</summary>
    private readonly Dictionary<string, HashSet<string>> _localWidgetKeys = new(StringComparer.Ordinal);

    /// <param name="syncedSettingKeys">Settings keys registered with scope Synced.</param>
    /// <param name="machineSettingKeys">Settings keys registered with scope Machine.</param>
    /// <param name="machineWidgetSettings">
    /// Widget settings with scope Machine, by widget type id. They stay on this PC: they are left
    /// out of the unit's value on export, and the local value is kept on import.
    /// </param>
    /// <param name="secretSettingKeys">
    /// Settings keys of type Secret. They never sync, whatever their value looks like, so a value
    /// typed by hand instead of a <c>secret:</c> reference never leaves this PC.
    /// </param>
    /// <param name="secretWidgetSettings">
    /// Widget settings of type Secret, by widget type id. They stay on this PC like Machine widget
    /// settings, whatever their value looks like.
    /// </param>
    public SyncScope(
        IEnumerable<string> syncedSettingKeys,
        IEnumerable<string>? machineSettingKeys = null,
        IEnumerable<(string WidgetTypeId, string Key)>? machineWidgetSettings = null,
        IEnumerable<string>? secretSettingKeys = null,
        IEnumerable<(string WidgetTypeId, string Key)>? secretWidgetSettings = null)
    {
        ArgumentNullException.ThrowIfNull(syncedSettingKeys);
        _syncedKeys = new HashSet<string>(syncedSettingKeys, StringComparer.Ordinal);
        _machineKeys = new HashSet<string>(machineSettingKeys ?? [], StringComparer.Ordinal);
        _secretKeys = new HashSet<string>(secretSettingKeys ?? [], StringComparer.Ordinal);
        foreach (var (typeId, key) in machineWidgetSettings ?? [])
        {
            AddField(_machineWidgetKeys, typeId, key);
            AddField(_localWidgetKeys, typeId, key);
        }
        foreach (var (typeId, key) in secretWidgetSettings ?? [])
        {
            AddField(_secretWidgetKeys, typeId, key);
            AddField(_localWidgetKeys, typeId, key);
        }
    }

    /// <summary>
    /// Settings keys that never sync, whatever their definition says. Every key of the startup,
    /// local API, sync, onboarding, updates and Ask AI features is also covered. A bundle can never turn
    /// on the local API, PATH or startup.
    /// </summary>
    public static IReadOnlyList<string> AlwaysMachineKeys { get; } =
    [
        "startup.enabled",
        "localApi.enabled",
        "localApi.onPath",
        "sync.enabled",
        "sync.folder",
        "profiles.manual",
        "onboarding.done",
        "updates.enabled",
    ];

    public static bool IsAlwaysMachineKey(string key)
    {
        foreach (var prefix in MachineFeaturePrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return AlwaysMachineKeys.Contains(key, StringComparer.Ordinal);
    }

    public SyncUnitKind Classify(string unitName)
    {
        if (!SyncUnitNames.IsWellFormed(unitName) || unitName == SyncUnitNames.Placement)
            return SyncUnitKind.Excluded;

        if (SyncUnitNames.IsFixed(unitName))
            return SyncUnitKind.Synced;

        if (unitName.StartsWith(SyncUnitNames.SettingPrefix, StringComparison.Ordinal))
        {
            if (!SyncUnitNames.TryGetSettingKey(unitName, out var key))
                return SyncUnitKind.Excluded;
            if (IsAlwaysMachineKey(key) || _machineKeys.Contains(key) || _secretKeys.Contains(key))
                return SyncUnitKind.Excluded;
            return _syncedKeys.Contains(key) ? SyncUnitKind.Synced : SyncUnitKind.Unknown;
        }

        if (unitName.StartsWith(SyncUnitNames.WidgetSettingsPrefix, StringComparison.Ordinal))
            return SyncUnitNames.TryGetInstanceId(unitName, out _) ? SyncUnitKind.Synced : SyncUnitKind.Excluded;

        // Another layout part or another area, maybe from a newer Atap.
        return SyncUnitKind.Unknown;
    }

    public bool IsSynced(string unitName) => Classify(unitName) == SyncUnitKind.Synced;

    /// <summary>
    /// True for a top-level field of a widget settings unit that stays on this PC because its
    /// setting has scope Machine.
    /// </summary>
    public bool IsMachineField(string unitName, string fieldName) =>
        TryGetFields(_machineWidgetKeys, unitName, out var keys) && keys.Contains(fieldName);

    /// <summary>
    /// True for a top-level field of a widget settings unit that stays on this PC because its
    /// setting has type Secret.
    /// </summary>
    public bool IsSecretField(string unitName, string fieldName) =>
        TryGetFields(_secretWidgetKeys, unitName, out var keys) && keys.Contains(fieldName);

    /// <summary>The Machine and Secret fields of a widget settings unit, or null when it has none.</summary>
    internal Func<string, bool>? LocalFieldsOf(string unitName) =>
        TryGetFields(_localWidgetKeys, unitName, out var keys) ? keys.Contains : null;

    private static bool TryGetFields(Dictionary<string, HashSet<string>> byType, string unitName, [NotNullWhen(true)] out HashSet<string>? keys)
    {
        keys = null;
        return SyncUnitNames.TryGetInstanceId(unitName, out var instanceId)
            && byType.TryGetValue(SyncUnitNames.TypeIdOf(instanceId), out keys);
    }

    private static void AddField(Dictionary<string, HashSet<string>> byType, string typeId, string key)
    {
        if (!byType.TryGetValue(typeId, out var keys))
            byType[typeId] = keys = new HashSet<string>(StringComparer.Ordinal);
        keys.Add(key);
    }
}
