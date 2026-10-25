using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Atap.Core.Sync;

/// <summary>Writes a sync bundle as compact JSON, units in name order so the same bundle gives the same text.</summary>
public static class SyncBundleWriter
{
    public static string Write(SyncBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return Encoding.UTF8.GetString(WriteUtf8(bundle));
    }

    /// <summary>Writes the bundle unless it would be over <see cref="SyncLimits.MaxBundleBytes"/>.</summary>
    public static bool TryWrite(SyncBundle bundle, out string json)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var utf8 = WriteUtf8(bundle);
        if (utf8.Length > SyncLimits.MaxBundleBytes)
        {
            json = string.Empty;
            return false;
        }
        json = Encoding.UTF8.GetString(utf8);
        return true;
    }

    private static ReadOnlySpan<byte> WriteUtf8(SyncBundle bundle)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", bundle.Format);
            writer.WriteString("deviceId", bundle.DeviceId);
            writer.WriteStartObject("units");
            foreach (var name in bundle.Units.Keys.Order(StringComparer.Ordinal))
            {
                var unit = bundle.Units[name];
                writer.WriteStartObject(name);
                writer.WriteString("stamp", SyncVersion.FormatStamp(unit.Version.Stamp));
                if (!string.Equals(unit.Version.Device, bundle.DeviceId, StringComparison.Ordinal))
                    writer.WriteString("device", unit.Version.Device);
                if (unit.Value is { } value)
                {
                    writer.WritePropertyName("value");
                    value.WriteTo(writer);
                }
                else
                {
                    writer.WriteBoolean("deleted", true);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan;
    }
}
