using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Atap.Core.Sync;

/// <summary>
/// Reads and writes sync-state.json:
/// <c>{ "format": 1, "deviceId": "&lt;guid&gt;", "units": { "&lt;name&gt;": { "stamp": "&lt;utc&gt;", "hash": "&lt;sha256&gt;" } } }</c>.
/// A unit changed on another PC also has <c>"device"</c>. A deleted unit has <c>"deleted": true</c> and no hash.
/// </summary>
public static class SyncStateFile
{
    /// <summary>
    /// Parses the file. Returns null when it is missing, broken or from a newer Atap. The caller then
    /// starts again with <see cref="SyncStamper.Initialize"/> and <see cref="SyncStartChoice.UseOneDrive"/>,
    /// so a lost state never lets this PC overwrite the other PCs.
    /// </summary>
    public static SyncState? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (text[0] == '\uFEFF')
            text = text[1..];
        if (text.Length > SyncLimits.MaxBundleBytes || Encoding.UTF8.GetByteCount(text) > SyncLimits.MaxBundleBytes)
            return null;

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false });
            return ReadRoot(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Write(SyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", SyncLimits.CurrentFormat);
            writer.WriteString("deviceId", state.DeviceId);
            writer.WriteStartObject("units");
            foreach (var name in state.Units.Keys.Order(StringComparer.Ordinal))
            {
                var unit = state.Units[name];
                writer.WriteStartObject(name);
                writer.WriteString("stamp", SyncVersion.FormatStamp(unit.Version.Stamp));
                if (!string.Equals(unit.Version.Device, state.DeviceId, StringComparison.Ordinal))
                    writer.WriteString("device", unit.Version.Device);
                if (unit.Hash is { } hash)
                    writer.WriteString("hash", hash);
                else
                    writer.WriteBoolean("deleted", true);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static SyncState? ReadRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("format", out var format)
            || format.ValueKind != JsonValueKind.Number
            || !format.TryGetInt32(out var formatValue)
            || formatValue != SyncLimits.CurrentFormat)
            return null;

        if (!root.TryGetProperty("deviceId", out var deviceElement)
            || deviceElement.ValueKind != JsonValueKind.String
            || !SyncVersion.TryNormalizeDevice(deviceElement.GetString(), out var deviceId))
            return null;

        var units = new Dictionary<string, SyncStateUnit>(StringComparer.Ordinal);
        if (root.TryGetProperty("units", out var unitsElement))
        {
            if (unitsElement.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var property in unitsElement.EnumerateObject())
            {
                // A bad entry is dropped. The next stamp treats that unit as new.
                if (SyncUnitNames.IsWellFormed(property.Name) && TryReadUnit(property.Value, deviceId, out var unit))
                    units[property.Name] = unit;
            }
        }
        return new SyncState(deviceId, units);
    }

    private static bool TryReadUnit(JsonElement element, string ownDevice, out SyncStateUnit unit)
    {
        unit = null!;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("stamp", out var stampElement)
            || stampElement.ValueKind != JsonValueKind.String
            || !SyncVersion.TryParseStamp(stampElement.GetString(), out var stamp))
            return false;

        var device = ownDevice;
        if (element.TryGetProperty("device", out var deviceElement)
            && (deviceElement.ValueKind != JsonValueKind.String || !SyncVersion.TryNormalizeDevice(deviceElement.GetString(), out device)))
            return false;

        var version = new SyncVersion(stamp, device);
        if (element.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True)
        {
            unit = SyncStateUnit.Deleted(version);
            return true;
        }

        if (!element.TryGetProperty("hash", out var hashElement)
            || hashElement.ValueKind != JsonValueKind.String
            || hashElement.GetString() is not { Length: 64 } hash)
            return false;
        unit = new SyncStateUnit(version, hash);
        return true;
    }
}
