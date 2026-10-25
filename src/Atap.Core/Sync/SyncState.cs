namespace Atap.Core.Sync;

/// <summary>
/// What this PC knows about each synced unit: the hash of its value and its version. Kept in
/// %LOCALAPPDATA%\AtapData\sync-state.json, which never leaves the PC.
/// </summary>
/// <param name="DeviceId">This PC's device id, a lowercase GUID made on the first enable.</param>
/// <param name="Units">Units by name.</param>
public sealed record SyncState(string DeviceId, IReadOnlyDictionary<string, SyncStateUnit> Units)
{
    /// <summary>A state with no units, for a PC turning sync on.</summary>
    public static SyncState Create(Guid deviceId) =>
        new(deviceId.ToString("D"), new Dictionary<string, SyncStateUnit>(StringComparer.Ordinal));

    /// <summary>Same device and the same units with the same hashes and versions.</summary>
    public bool SameAs(SyncState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
            return true;
        if (DeviceId != other.DeviceId || Units.Count != other.Units.Count)
            return false;
        foreach (var (name, unit) in Units)
        {
            if (!other.Units.TryGetValue(name, out var otherUnit) || unit != otherUnit)
                return false;
        }
        return true;
    }
}

/// <param name="Version">When and where the unit last changed.</param>
/// <param name="Hash">
/// <see cref="SyncValues.Hash"/> of the value with its local-only parts stripped, or null when
/// the unit was deleted.
/// </param>
public sealed record SyncStateUnit(SyncVersion Version, string? Hash)
{
    public bool IsDeleted => Hash is null;

    public static SyncStateUnit Deleted(SyncVersion version) => new(version, null);
}
