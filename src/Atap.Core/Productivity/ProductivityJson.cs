using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity;

/// <summary>Writes enum values in camelCase, as layout.json does. Reading also accepts numbers.</summary>
public sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;

/// <summary>
/// Reads and writes <see cref="TimeOnly"/> as "HH:mm", the format of Time settings. Seconds are
/// written only when set. Reading accepts "HH:mm", "H:mm" and "HH:mm:ss".
/// </summary>
public sealed class HourMinuteConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && TimeOnly.TryParseExact(reader.GetString(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;
        throw new JsonException("Expected a time written as HH:mm.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
    {
        var format = value.Ticks % TimeSpan.TicksPerMinute == 0 ? "HH:mm" : "HH:mm:ss";
        writer.WriteStringValue(value.ToString(format, CultureInfo.InvariantCulture));
    }
}
