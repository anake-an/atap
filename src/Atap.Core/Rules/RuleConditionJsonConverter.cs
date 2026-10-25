using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Rules;

/// <summary>
/// Reads and writes a <see cref="RuleCondition"/> as a JSON object with a <c>kind</c> field, in any
/// property order:
/// <code>
/// { "kind": "foregroundApp", "apps": ["valorant.exe"] }
/// { "kind": "timeWindow", "days": ["mon", "fri"], "from": "22:00", "to": "07:00" }
/// { "kind": "wifi", "ssids": ["Rumah_unifi"] }
/// { "kind": "power", "state": "batteryBelow", "percent": 20 }
/// </code>
/// Kind, day and state values are matched ignoring case. Unknown fields are kept. Any value it cannot
/// read becomes an <see cref="UnsupportedCondition"/> holding the original JSON, so one bad condition
/// never fails the whole settings file. It uses no reflection and works with source-generated contexts.
/// </summary>
public sealed class RuleConditionJsonConverter : JsonConverter<RuleCondition>
{
    private const string KindProperty = "kind";
    private const string AppsProperty = "apps";
    private const string DaysProperty = "days";
    private const string FromProperty = "from";
    private const string ToProperty = "to";
    private const string SsidsProperty = "ssids";
    private const string StateProperty = "state";
    private const string PercentProperty = "percent";

    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    // Indexed by DayOfWeek, which starts at Sunday.
    private static readonly string[] DayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    private static readonly (ConditionKind Kind, string Name, string[] Properties)[] Kinds =
    [
        (ConditionKind.ForegroundApp, "foregroundApp", [KindProperty, AppsProperty]),
        (ConditionKind.TimeWindow, "timeWindow", [KindProperty, DaysProperty, FromProperty, ToProperty]),
        (ConditionKind.Wifi, "wifi", [KindProperty, SsidsProperty]),
        (ConditionKind.Power, "power", [KindProperty, StateProperty, PercentProperty]),
    ];

    private static readonly (PowerConditionState State, string Name)[] PowerStates =
    [
        (PowerConditionState.OnBattery, "onBattery"),
        (PowerConditionState.PluggedIn, "pluggedIn"),
        (PowerConditionState.EnergySaver, "energySaver"),
        (PowerConditionState.BatteryBelow, "batteryBelow"),
    ];

    public override RuleCondition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FromElement(JsonElement.ParseValue(ref reader));

    public override void Write(Utf8JsonWriter writer, RuleCondition value, JsonSerializerOptions options)
    {
        if (value is UnsupportedCondition unsupported)
        {
            unsupported.Raw.WriteTo(writer);
            return;
        }

        var kind = Array.Find(Kinds, entry => entry.Kind == value.Kind);
        writer.WriteStartObject();
        writer.WriteString(KindProperty, kind.Name);

        switch (value)
        {
            case ForegroundAppCondition app:
                WriteStrings(writer, AppsProperty, app.Apps);
                break;

            case TimeWindowCondition window:
                if (window.Days is { Count: > 0 })
                {
                    writer.WriteStartArray(DaysProperty);
                    foreach (var day in window.Days.Where(candidate => Enum.IsDefined(candidate)).Distinct())
                        writer.WriteStringValue(DayNames[(int)day]);
                    writer.WriteEndArray();
                }
                writer.WriteString(FromProperty, window.From.ToString("HH:mm", CultureInfo.InvariantCulture));
                writer.WriteString(ToProperty, window.To.ToString("HH:mm", CultureInfo.InvariantCulture));
                break;

            case WifiCondition wifi:
                WriteStrings(writer, SsidsProperty, wifi.Ssids);
                break;

            case PowerCondition power:
                writer.WriteString(StateProperty, Array.Find(PowerStates, entry => entry.State == power.State).Name ?? power.State.ToString());
                if (power.Percent is { } percent)
                    writer.WriteNumber(PercentProperty, percent);
                break;
        }

        if (value.ExtensionData is { } extra)
        {
            foreach (var (name, extraValue) in extra)
            {
                // Never write a field twice, even if code put a known name here.
                if (Array.IndexOf(kind.Properties ?? [], name) >= 0)
                    continue;
                writer.WritePropertyName(name);
                extraValue.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>Reads one condition from a JSON value. Never throws.</summary>
    internal static RuleCondition FromElement(JsonElement element)
    {
        string? kindName = null;
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(KindProperty, out var kindValue)
            && kindValue.ValueKind == JsonValueKind.String)
        {
            kindName = kindValue.GetString();
        }

        var known = Array.FindIndex(Kinds, entry => string.Equals(entry.Name, kindName, StringComparison.OrdinalIgnoreCase));
        if (known < 0)
            return new UnsupportedCondition(element, kindName, isKnownKind: false);

        var (kind, _, properties) = Kinds[known];
        RuleCondition? condition = kind switch
        {
            ConditionKind.ForegroundApp => TryReadStrings(element, AppsProperty, out var apps) ? new ForegroundAppCondition { Apps = apps } : null,
            ConditionKind.TimeWindow => ReadTimeWindow(element),
            ConditionKind.Wifi => TryReadStrings(element, SsidsProperty, out var ssids) ? new WifiCondition { Ssids = ssids } : null,
            ConditionKind.Power => ReadPower(element),
            _ => null,
        };
        if (condition is null)
            return new UnsupportedCondition(element, kindName, isKnownKind: true);

        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(properties, property.Name) < 0)
                (condition.ExtensionData ??= new(StringComparer.Ordinal))[property.Name] = property.Value;
        }
        return condition;
    }

    private static TimeWindowCondition? ReadTimeWindow(JsonElement element)
    {
        if (!TryReadTime(element, FromProperty, out var from) || !TryReadTime(element, ToProperty, out var to))
            return null;

        var days = new List<DayOfWeek>();
        if (element.TryGetProperty(DaysProperty, out var daysValue) && daysValue.ValueKind != JsonValueKind.Null)
        {
            if (daysValue.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in daysValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !TryParseDay(item.GetString(), out var day))
                    return null;
                if (!days.Contains(day))
                    days.Add(day);
            }
        }

        return new TimeWindowCondition { Days = days, From = from, To = to };
    }

    private static PowerCondition? ReadPower(JsonElement element)
    {
        if (!element.TryGetProperty(StateProperty, out var stateValue) || stateValue.ValueKind != JsonValueKind.String)
            return null;
        var stateName = stateValue.GetString();
        var state = Array.FindIndex(PowerStates, entry => string.Equals(entry.Name, stateName, StringComparison.OrdinalIgnoreCase));
        if (state < 0)
            return null;

        int? percent = null;
        if (element.TryGetProperty(PercentProperty, out var percentValue) && percentValue.ValueKind != JsonValueKind.Null)
        {
            if (percentValue.ValueKind != JsonValueKind.Number || !percentValue.TryGetInt32(out var number))
                return null;
            percent = number;
        }

        return new PowerCondition { State = PowerStates[state].State, Percent = percent };
    }

    /// <summary>A missing or null list reads as empty. Anything other than an array of strings fails.</summary>
    private static bool TryReadStrings(JsonElement element, string name, out List<string> values)
    {
        values = [];
        if (!element.TryGetProperty(name, out var array) || array.ValueKind == JsonValueKind.Null)
            return true;
        if (array.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
            values.Add(item.GetString()!);
        }
        return true;
    }

    /// <summary>HH:mm or H:mm. "24:00" is read as midnight, so 22:00 to 24:00 ends at midnight.</summary>
    private static bool TryReadTime(JsonElement element, string name, out TimeOnly time)
    {
        time = default;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return false;

        var text = value.GetString();
        if (text == "24:00")
            return true;
        return TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    /// <summary>Short names ("mon") or full English names ("Monday").</summary>
    private static bool TryParseDay(string? text, out DayOfWeek day)
    {
        for (var index = 0; index < DayNames.Length; index++)
        {
            day = (DayOfWeek)index;
            if (string.Equals(text, DayNames[index], StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, day.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        day = default;
        return false;
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, List<string>? values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values ?? [])
        {
            if (value is not null)
                writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }
}
