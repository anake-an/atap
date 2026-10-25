using System.Text.Json;

namespace Atap.Core.Sync;

/// <summary>
/// The sync file in OneDrive, atap-sync.json:
/// <c>{ "format": 1, "deviceId": "&lt;guid&gt;", "units": { "&lt;name&gt;": { "stamp": "&lt;utc&gt;", "value": ... } } }</c>.
/// </summary>
/// <remarks>
/// A unit may also carry <c>"device"</c>, the PC that made the change, when that is not the PC that
/// wrote the file. A deleted unit has <c>"deleted": true</c> and no value.
/// </remarks>
/// <param name="DeviceId">The PC that wrote the file, a lowercase GUID.</param>
/// <param name="Units">Units by name.</param>
public sealed record SyncBundle(string DeviceId, IReadOnlyDictionary<string, SyncBundleUnit> Units)
{
    public int Format { get; init; } = SyncLimits.CurrentFormat;
}

/// <summary>One unit in a bundle.</summary>
/// <param name="Version">When and where the unit last changed.</param>
/// <param name="Value">The value, or null when the unit was deleted.</param>
public sealed record SyncBundleUnit(SyncVersion Version, JsonElement? Value)
{
    public bool IsDeleted => Value is null;

    public static SyncBundleUnit Deleted(SyncVersion version) => new(version, null);
}
