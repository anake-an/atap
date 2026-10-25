using System.Text;

namespace Atap.Core.Commands.Query;

/// <summary>
/// The units the command bar converts. Built on first use, so it costs nothing until a query needs it.
/// There is no currency, because rates change and would need the network.
/// </summary>
/// <remarks>
/// Names are matched without case, spaces are collapsed, ² and ³ read as 2 and 3, and ° and ^ are ignored.
/// So "°C", "c" and "celsius" all find Celsius, and "m²", "m^2" and "sq m" all find square metres.
/// <para>
/// Choices where a name has more than one meaning:
/// KB, MB, GB, TB and PB are powers of 1000. KiB, MiB, GiB and TiB are powers of 1024. "b" is a byte.
/// "gal" and "fl oz" are US measures. The imperial gallon is "imp gal".
/// "t" is the metric tonne. "cal" is the small calorie and "kcal" the food calorie.
/// "batu" is the mile. The kati is 1⅓ lb and the tahil a sixteenth of a kati, as defined in Malaysia.
/// </para>
/// </remarks>
public static class UnitTable
{
    private static readonly UnitDefinition[] Units =
    [
        // Length, in metres.
        Unit("mm", "mm", UnitCategory.Length, 0.001m, "millimetre", "millimetres", "millimeter", "millimeters", "milimeter"),
        Unit("cm", "cm", UnitCategory.Length, 0.01m, "centimetre", "centimetres", "centimeter", "centimeters", "sentimeter"),
        Unit("m", "m", UnitCategory.Length, 1m, "metre", "metres", "meter", "meters"),
        Unit("km", "km", UnitCategory.Length, 1000m, "kilometre", "kilometres", "kilometer", "kilometers", "kms"),
        Unit("in", "in", UnitCategory.Length, 0.0254m, "inch", "inches", "inci"),
        Unit("ft", "ft", UnitCategory.Length, 0.3048m, "foot", "feet", "kaki"),
        Unit("yd", "yd", UnitCategory.Length, 0.9144m, "yard", "yards", "ela"),
        Unit("mi", "mi", UnitCategory.Length, 1609.344m, "mile", "miles", "batu"),
        Unit("nmi", "nmi", UnitCategory.Length, 1852m, "nautical mile", "nautical miles", "batu nautika"),

        // Mass, in kilograms.
        Unit("mg", "mg", UnitCategory.Mass, 0.000001m, "milligram", "milligrams", "miligram"),
        Unit("g", "g", UnitCategory.Mass, 0.001m, "gram", "grams", "gramme", "grammes"),
        Unit("kg", "kg", UnitCategory.Mass, 1m, "kilogram", "kilograms", "kilo", "kilos", "kgs"),
        Unit("t", "t", UnitCategory.Mass, 1000m, "tonne", "tonnes", "tan"),
        Unit("oz", "oz", UnitCategory.Mass, 0.028349523125m, "ounce", "ounces", "auns"),
        Unit("lb", "lb", UnitCategory.Mass, 0.45359237m, "lbs", "pound", "pounds", "paun"),
        Unit("st", "st", UnitCategory.Mass, 6.35029318m, "stone", "stones"),
        Ratio("kati", "kati", UnitCategory.Mass, 1.81436948m, 3m),
        Ratio("tahil", "tahil", UnitCategory.Mass, 1.81436948m, 48m),

        // Temperature, in kelvin.
        new("c", "°C", UnitCategory.Temperature, 1m, 1m, 273.15m, ["celsius"]),
        new("f", "°F", UnitCategory.Temperature, 5m, 9m, 459.67m, ["fahrenheit"]),
        new("k", "K", UnitCategory.Temperature, 1m, 1m, 0m, ["kelvin"]),

        // Volume, in litres.
        Unit("ml", "mL", UnitCategory.Volume, 0.001m, "millilitre", "millilitres", "milliliter", "milliliters", "mililiter", "cc"),
        Unit("l", "L", UnitCategory.Volume, 1m, "litre", "litres", "liter", "liters", "ltr"),
        Unit("m3", "m³", UnitCategory.Volume, 1000m, "cubic metre", "cubic metres", "cubic meter", "cubic meters", "meter padu"),
        Unit("gal", "gal", UnitCategory.Volume, 3.785411784m, "gallon", "gallons", "us gal"),
        Unit("impgal", "imp gal", UnitCategory.Volume, 4.54609m, "imperial gallon", "imperial gallons"),
        Unit("floz", "fl oz", UnitCategory.Volume, 0.0295735295625m, "fluid ounce", "fluid ounces"),

        // Area, in square metres.
        Unit("cm2", "cm²", UnitCategory.Area, 0.0001m, "sq cm", "square centimetre", "square centimetres", "square centimeter", "square centimeters"),
        Unit("m2", "m²", UnitCategory.Area, 1m, "sq m", "sqm", "square metre", "square metres", "square meter", "square meters", "meter persegi"),
        Unit("km2", "km²", UnitCategory.Area, 1_000_000m, "sq km", "square kilometre", "square kilometres", "square kilometer", "square kilometers"),
        Unit("ha", "ha", UnitCategory.Area, 10_000m, "hectare", "hectares", "hektar"),
        Unit("ac", "ac", UnitCategory.Area, 4046.8564224m, "acre", "acres", "ekar"),
        Unit("ft2", "ft²", UnitCategory.Area, 0.09290304m, "sq ft", "sqft", "square foot", "square feet", "kaki persegi"),
        Unit("mi2", "mi²", UnitCategory.Area, 2_589_988.110336m, "sq mi", "square mile", "square miles"),

        // Speed, in metres per second.
        Unit("m/s", "m/s", UnitCategory.Speed, 1m, "metres per second", "meters per second"),
        Ratio("km/h", "km/h", UnitCategory.Speed, 1000m, 3600m, "kmh", "kph", "kmph", "km/j", "kilometres per hour", "kilometers per hour"),
        Ratio("mph", "mph", UnitCategory.Speed, 1609.344m, 3600m, "mi/h", "miles per hour"),
        Ratio("kn", "kn", UnitCategory.Speed, 1852m, 3600m, "kt", "knot", "knots"),

        // Time, in seconds.
        Unit("ms", "ms", UnitCategory.Time, 0.001m, "millisecond", "milliseconds", "milisaat"),
        Unit("s", "s", UnitCategory.Time, 1m, "sec", "secs", "second", "seconds", "saat"),
        Unit("min", "min", UnitCategory.Time, 60m, "mins", "minute", "minutes", "minit"),
        Unit("h", "h", UnitCategory.Time, 3600m, "hr", "hrs", "hour", "hours", "jam"),
        Unit("d", "d", UnitCategory.Time, 86_400m, "day", "days", "hari"),
        Unit("wk", "wk", UnitCategory.Time, 604_800m, "week", "weeks", "minggu"),

        // Data, in bytes.
        Ratio("bit", "bit", UnitCategory.Data, 1m, 8m, "bits"),
        Unit("b", "B", UnitCategory.Data, 1m, "byte", "bytes"),
        Unit("kb", "KB", UnitCategory.Data, 1_000m, "kilobyte", "kilobytes"),
        Unit("mb", "MB", UnitCategory.Data, 1_000_000m, "megabyte", "megabytes"),
        Unit("gb", "GB", UnitCategory.Data, 1_000_000_000m, "gigabyte", "gigabytes"),
        Unit("tb", "TB", UnitCategory.Data, 1_000_000_000_000m, "terabyte", "terabytes"),
        Unit("pb", "PB", UnitCategory.Data, 1_000_000_000_000_000m, "petabyte", "petabytes"),
        Unit("kib", "KiB", UnitCategory.Data, 1_024m, "kibibyte", "kibibytes"),
        Unit("mib", "MiB", UnitCategory.Data, 1_048_576m, "mebibyte", "mebibytes"),
        Unit("gib", "GiB", UnitCategory.Data, 1_073_741_824m, "gibibyte", "gibibytes"),
        Unit("tib", "TiB", UnitCategory.Data, 1_099_511_627_776m, "tebibyte", "tebibytes"),

        // Pressure, in pascals. A pound-force (0.45359237 kg × 9.80665 m/s²) per square inch is exact as a fraction.
        Unit("pa", "Pa", UnitCategory.Pressure, 1m, "pascal", "pascals"),
        Unit("kpa", "kPa", UnitCategory.Pressure, 1_000m, "kilopascal", "kilopascals"),
        Unit("bar", "bar", UnitCategory.Pressure, 100_000m, "bars"),
        Ratio("psi", "psi", UnitCategory.Pressure, 4.4482216152605m, 0.00064516m),
        Unit("atm", "atm", UnitCategory.Pressure, 101_325m, "atmosphere", "atmospheres"),

        // Energy, in joules.
        Unit("j", "J", UnitCategory.Energy, 1m, "joule", "joules"),
        Unit("kj", "kJ", UnitCategory.Energy, 1_000m, "kilojoule", "kilojoules"),
        Unit("cal", "cal", UnitCategory.Energy, 4.184m, "calorie", "calories", "kalori"),
        Unit("kcal", "kcal", UnitCategory.Energy, 4_184m, "kilocalorie", "kilocalories", "kilokalori"),
        Unit("wh", "Wh", UnitCategory.Energy, 3_600m, "watt hour", "watt hours"),
        Unit("kwh", "kWh", UnitCategory.Energy, 3_600_000m, "kilowatt hour", "kilowatt hours"),
    ];

    private static readonly Dictionary<string, UnitDefinition> ByName = BuildIndex();

    /// <summary>Every unit, grouped by category.</summary>
    public static IReadOnlyList<UnitDefinition> All => Units;

    /// <summary>Finds a unit by its id, symbol or any alias, or returns null.</summary>
    public static UnitDefinition? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return ByName.GetValueOrDefault(Normalize(name));
    }

    /// <summary>Lowercase, single spaces, ² and ³ as digits, and no ° or ^.</summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var raw in text)
        {
            var c = raw switch
            {
                '²' => '2',
                '³' => '3',
                _ => char.ToLowerInvariant(raw),
            };
            if (c is '°' or 'º' or '^')
                continue;
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static Dictionary<string, UnitDefinition> BuildIndex()
    {
        // First one wins. UnitTableTests checks that no two units share a name.
        var index = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal);
        foreach (var unit in Units)
        {
            index.TryAdd(Normalize(unit.Id), unit);
            index.TryAdd(Normalize(unit.Symbol), unit);
            foreach (var alias in unit.Aliases)
                index.TryAdd(Normalize(alias), unit);
        }

        return index;
    }

    private static UnitDefinition Unit(string id, string symbol, UnitCategory category, decimal factor, params string[] aliases) =>
        new(id, symbol, category, factor, 1m, 0m, aliases);

    private static UnitDefinition Ratio(
        string id, string symbol, UnitCategory category, decimal numerator, decimal denominator, params string[] aliases) =>
        new(id, symbol, category, numerator, denominator, 0m, aliases);
}
