using System.Text.Json;

namespace Atap.Core.Sync;

/// <summary>The choice on the first enable when a bundle already exists.</summary>
public enum SyncStartChoice
{
    /// <summary>
    /// "Keep this PC's settings". This PC's units win everywhere, and synced units that only the
    /// bundle has are deleted. A fixed unit is never deleted, so one that only the bundle has is
    /// taken from it.
    /// </summary>
    KeepThisPc,

    /// <summary>
    /// "Use the settings from OneDrive". Every unit in the bundle wins. Units only this PC has are
    /// added to the bundle.
    /// </summary>
    UseOneDrive,
}

/// <summary>
/// Gives units their stamps. While sync is on, the App calls <see cref="Stamp"/> after each local
/// write, so a unit's stamp is the time of the edit, not of the next export.
/// </summary>
/// <remarks>
/// Local units are passed by name with their current value, from <see cref="SyncUnits"/>. Pass every
/// unit of a document or none of it. A document is taken as read only when its sentinel unit is
/// present (<see cref="SyncUnitNames.SentinelFor"/>), so a document that failed to load never
/// turns into deletions on every PC. Leave out a document that only has defaults in memory, such as
/// one that is locked or unreadable. For a document whose file was reset to defaults, call
/// <see cref="YieldToOneDrive"/> first.
/// </remarks>
public static class SyncStamper
{
    /// <summary>
    /// Hashes each synced local unit. A new unit or a changed hash gets a new stamp at
    /// <paramref name="now"/>. A synced unit that is gone gets a deletion stamp, except fixed units
    /// and units of a document that was not read. Returns the same instance when nothing changed.
    /// </summary>
    public static SyncState Stamp(SyncState state, IReadOnlyDictionary<string, JsonElement> localUnits, SyncScope scope, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(localUnits);
        ArgumentNullException.ThrowIfNull(scope);

        Dictionary<string, SyncStateUnit>? units = null;
        Dictionary<string, SyncStateUnit> Edit() => units ??= new Dictionary<string, SyncStateUnit>(state.Units, StringComparer.Ordinal);

        foreach (var (name, value) in localUnits)
        {
            if (!scope.IsSynced(name))
                continue;
            state.Units.TryGetValue(name, out var previous);
            var hash = LocalHash(name, value, scope);
            if (hash is null)
            {
                // The value is a secret reference, so the unit stays on this PC and has no state.
                if (previous is not null)
                    Edit().Remove(name);
                continue;
            }
            if (previous is { Hash: { } previousHash } && previousHash == hash)
                continue;
            var stamp = SyncVersion.NextStamp(now, previous?.Version.Stamp);
            Edit()[name] = new SyncStateUnit(new SyncVersion(stamp, state.DeviceId), hash);
        }

        foreach (var (name, previous) in state.Units)
        {
            if (!scope.IsSynced(name))
            {
                // No longer synced here, for example a key that became Machine.
                Edit().Remove(name);
                continue;
            }
            if (previous.IsDeleted || localUnits.ContainsKey(name) || !CanDelete(name, localUnits))
                continue;
            var stamp = SyncVersion.NextStamp(now, previous.Version.Stamp);
            Edit()[name] = SyncStateUnit.Deleted(new SyncVersion(stamp, state.DeviceId));
        }

        return units is null ? state : state with { Units = units };
    }

    /// <summary>
    /// The state for a PC turning sync on. With <see cref="SyncStartChoice.KeepThisPc"/> every local
    /// unit is stamped later than its newest copy in <paramref name="existing"/> and
    /// <paramref name="conflictCopies"/>, and units only those bundles have are stamped as deleted.
    /// With <see cref="SyncStartChoice.UseOneDrive"/> local units get <see cref="SyncVersion.InitialStamp"/>,
    /// which any real stamp beats. A unit the bundle lacks is stamped by the merge that first writes
    /// it, at the time of that pass. Use UseOneDrive when no choice was asked, such as on the first
    /// PC or after sync-state.json was lost.
    /// </summary>
    /// <remarks>
    /// Pass the conflict copies the first merge will read. Otherwise a newer stamp in a copy still
    /// beats "Keep this PC's settings". A fixed unit is never deleted, so a fixed unit that only the
    /// bundles have gets no state here and the first merge takes it from them. That only happens
    /// when this PC's document lacks the unit.
    /// </remarks>
    public static SyncState Initialize(
        Guid deviceId,
        IReadOnlyDictionary<string, JsonElement> localUnits,
        SyncScope scope,
        SyncStartChoice choice,
        SyncBundle? existing,
        DateTimeOffset now,
        IReadOnlyList<SyncBundle>? conflictCopies = null)
    {
        ArgumentNullException.ThrowIfNull(localUnits);
        ArgumentNullException.ThrowIfNull(scope);

        var device = deviceId.ToString("D");
        var remote = choice == SyncStartChoice.KeepThisPc
            ? NewestRemoteUnits(existing, conflictCopies)
            : new Dictionary<string, SyncBundleUnit>(StringComparer.Ordinal);
        var units = new Dictionary<string, SyncStateUnit>(StringComparer.Ordinal);
        foreach (var (name, value) in localUnits)
        {
            if (!scope.IsSynced(name) || LocalHash(name, value, scope) is not { } hash)
                continue;
            var stamp = choice == SyncStartChoice.KeepThisPc
                ? SyncVersion.NextStamp(now, remote.TryGetValue(name, out var newest) ? newest.Version.Stamp : null)
                : SyncVersion.InitialStamp;
            units[name] = new SyncStateUnit(new SyncVersion(stamp, device), hash);
        }

        foreach (var (name, newest) in remote)
        {
            if (newest.IsDeleted || units.ContainsKey(name) || localUnits.ContainsKey(name)
                || !scope.IsSynced(name) || !CanDelete(name, localUnits))
                continue;
            units[name] = SyncStateUnit.Deleted(new SyncVersion(SyncVersion.NextStamp(now, newest.Version.Stamp), device));
        }

        return new SyncState(device, units);
    }

    /// <summary>
    /// Lets the bundle win every unit of one document, as <see cref="SyncStartChoice.UseOneDrive"/>
    /// does for both. Its units in <paramref name="localUnits"/> get <see cref="SyncVersion.InitialStamp"/>,
    /// and its other units leave the state, so they are taken from the bundle again. A unit the
    /// bundle lacks is stamped by the merge that first writes it. Units of the other document keep
    /// their state.
    /// </summary>
    /// <remarks>
    /// Call it instead of <see cref="Stamp"/> when a document was reset to defaults, for example
    /// after a corrupt layout.json was replaced. Stamped as a new change, the defaults would win on
    /// every PC. Save the state at once, because after a restart the reset file loads like any other.
    /// </remarks>
    /// <param name="family">The document that was reset: <see cref="SyncFamily.Settings"/> or <see cref="SyncFamily.Layout"/>.</param>
    public static SyncState YieldToOneDrive(SyncState state, SyncFamily family, IReadOnlyDictionary<string, JsonElement> localUnits, SyncScope scope)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(localUnits);
        ArgumentNullException.ThrowIfNull(scope);
        if (SyncUnitNames.SentinelFor(family) is null)
            throw new ArgumentOutOfRangeException(nameof(family), family, "Only settings and layout units belong to a document.");

        var units = new Dictionary<string, SyncStateUnit>(StringComparer.Ordinal);
        foreach (var (name, unit) in state.Units)
        {
            if (SyncUnitNames.FamilyOf(name) != family)
                units[name] = unit;
        }
        foreach (var (name, value) in localUnits)
        {
            if (SyncUnitNames.FamilyOf(name) != family || !scope.IsSynced(name) || LocalHash(name, value, scope) is not { } hash)
                continue;
            units[name] = new SyncStateUnit(new SyncVersion(SyncVersion.InitialStamp, state.DeviceId), hash);
        }
        return state with { Units = units };
    }

    /// <summary>The hash a local value is tracked by, or null when the unit stays on this PC.</summary>
    internal static string? LocalHash(string name, JsonElement value, SyncScope scope) =>
        SyncValues.Strip(value, scope.LocalFieldsOf(name)) is { } stripped ? SyncValues.Hash(stripped) : null;

    /// <summary>True when the document the unit belongs to was read on this PC.</summary>
    internal static bool HasDocument(string name, IReadOnlyDictionary<string, JsonElement> localUnits) =>
        SyncUnitNames.SentinelFor(SyncUnitNames.FamilyOf(name)) is { } sentinel && localUnits.ContainsKey(sentinel);

    private static bool CanDelete(string name, IReadOnlyDictionary<string, JsonElement> localUnits) =>
        !SyncUnitNames.IsFixed(name) && HasDocument(name, localUnits);

    /// <summary>The newest version of each unit across the bundle and its conflict copies, as the merge picks it.</summary>
    private static Dictionary<string, SyncBundleUnit> NewestRemoteUnits(SyncBundle? existing, IReadOnlyList<SyncBundle>? conflictCopies)
    {
        var newest = new Dictionary<string, SyncBundleUnit>(StringComparer.Ordinal);
        IEnumerable<SyncBundle> bundles = existing is null ? conflictCopies ?? [] : [existing, .. conflictCopies ?? []];
        foreach (var bundle in bundles)
        {
            foreach (var (name, unit) in bundle.Units)
            {
                if (!newest.TryGetValue(name, out var current) || unit.Version > current.Version)
                    newest[name] = unit;
            }
        }
        return newest;
    }
}
