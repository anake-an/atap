using System.Globalization;
using Atap.Core.Commands.Query;

namespace Atap.Core.Tests.Commands.Query;

public class UnitConverterTests
{
    // Queries as typed in the command bar, in English and Bahasa Melayu, and the line shown (en-MY).
    private const string Conversions = """
        5 km to mi              => 5 km = 3.10685596118667 mi
        5km in mi               => 5 km = 3.10685596118667 mi
        5 KM TO MI              => 5 km = 3.10685596118667 mi
        1 mi to km              => 1 mi = 1.609344 km
        100 °C to °F            => 100 °C = 212 °F
        -40 c to f              => -40 °C = -40 °F
        0 C to K                => 0 °C = 273.15 K
        98.6 F ke C             => 98.6 °F = 37 °C
        1 kati to kg            => 1 kati = 0.604789826666667 kg
        16 tahil to kati        => 16 tahil = 1 kati
        1,000 sq ft dalam m²    => 1,000 ft² = 92.90304 m²
        1 ha to acres           => 1 ha = 2.47105381467165 ac
        60 km/h to mph          => 60 km/h = 37.28227153424 mph
        110 km/j ke mph         => 110 km/h = 68.3508311461067 mph
        32 psi to bar           => 32 psi = 2.20632233381388 bar
        1 GiB to MB             => 1 GiB = 1,073.741824 MB
        1 kWh to kJ             => 1 kWh = 3,600 kJ
        5 in in cm              => 5 in = 12.7 cm
        2 kaki ke cm            => 2 ft = 60.96 cm
        1 jam ke minit          => 1 h = 60 min
        150 cc to L             => 150 mL = 0.15 L
        1.5 L -> mL             => 1.5 L = 1,500 mL
        3 ft=in                 => 3 ft = 36 in
        1 m^2 to cm2            => 1 m² = 10,000 cm²
        5 lbs into kg           => 5 lb = 2.26796185 kg
        1 week to h             => 1 wk = 168 h
        8 bit to B              => 8 bit = 1 B
        1 atm to kPa            => 1 atm = 101.325 kPa
        2 batu ke km            => 2 mi = 3.218688 km
        1 km to km              => 1 km = 1 km
        """;

    // Queries that are not conversions, or that name units Atap cannot convert.
    private const string Failures = """
        5 km to kg              => DifferentCategories
        5 km to xyz             => UnknownUnit xyz
        100 usd to myr          => UnknownUnit usd
        5 km                    => NotAConversion
        km to mi                => NotAConversion
        12*8                    => NotAConversion
        spotify                 => NotAConversion
        5 to                    => NotAConversion
        5 km mi                 => NotAConversion
        1,23 km to mi           => NotAConversion
        """;

    public static TheoryData<string, string> ConversionCases => Table(Conversions);

    public static TheoryData<string, string> FailureCases => Table(Failures);

    private static readonly decimal[] Samples = [0m, 1m, -40m, 2.5m, 1234.5678m, 0.001m, 1_000_000m];

    // Below this a decimal with 28 decimal places holds fewer than 15 significant digits.
    private const decimal ShownPrecisionFloor = 0.000_000_000_001m;

    [Theory]
    [MemberData(nameof(ConversionCases))]
    public void Parse_ConvertsTheQuery(string query, string expected)
    {
        var result = UnitConverter.Parse(query);

        Assert.Equal(ConversionError.None, result.Error);
        Assert.NotNull(result.Conversion);
        Assert.Equal(expected, result.Conversion.Format(CultureInfo.GetCultureInfo("en-MY")));
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public void Parse_ReportsWhyItCannotConvert(string query, string expected)
    {
        var parts = expected.Split(' ');

        var result = UnitConverter.Parse(query);

        Assert.False(result.Success);
        Assert.Null(result.Conversion);
        Assert.Equal(Enum.Parse<ConversionError>(parts[0]), result.Error);
        Assert.Equal(parts.Length > 1 ? parts[1] : null, result.UnknownUnit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankQueryIsNotAConversion(string? query)
    {
        Assert.Equal(ConversionError.NotAConversion, UnitConverter.Parse(query).Error);
    }

    [Fact]
    public void Parse_RejectsQueriesOverTheLengthLimit()
    {
        var query = "5 km to mi" + new string(' ', UnitConverter.MaxLength);

        Assert.Equal(ConversionError.NotAConversion, UnitConverter.Parse(query).Error);
    }

    [Fact]
    public void Parse_KeepsTheExactResult()
    {
        var conversion = UnitConverter.Parse("100 km to m").Conversion;

        Assert.NotNull(conversion);
        Assert.Equal(100m, conversion.Amount);
        Assert.Equal(100_000m, conversion.Result);
        Assert.Equal("km", conversion.From.Id);
        Assert.Equal("m", conversion.To.Id);
    }

    [Fact]
    public void Parse_ReportsOverflow()
    {
        var result = UnitConverter.Parse("79228162514264337593543950335 TiB to bit");

        Assert.Equal(ConversionError.Overflow, result.Error);
    }

    [Fact]
    public void Units_RoundTripWithinEveryCategory()
    {
        foreach (var group in UnitTable.All.GroupBy(u => u.Category))
        {
            foreach (var from in group)
            {
                foreach (var to in group)
                {
                    foreach (var amount in Samples)
                    {
                        Assert.True(UnitConverter.TryConvert(amount, from, to, out var there), $"{amount} {from} to {to}");
                        Assert.True(UnitConverter.TryConvert(there, to, from, out var back), $"{there} {to} to {from}");

                        var label = $"{amount} {from} to {to} gave {there} and back {back}";
                        if (Math.Abs(there) >= ShownPrecisionFloor || amount == 0)
                        {
                            // The value in between keeps 15 significant digits, so the shown amount comes back unchanged.
                            var tolerance = 0.000_000_000_000_001m * Math.Max(1m, Math.Abs(amount));
                            Assert.True(Math.Abs(back - amount) <= tolerance, label);
                            Assert.True(QueryNumber.Format(amount) == QueryNumber.Format(back), label);
                        }
                        else
                        {
                            // Decimal has 28 decimal places, so a tiny value in between keeps fewer digits.
                            // The error is then at most the last place, scaled back up.
                            var scale = Math.Abs(amount) / Math.Abs(there);
                            Assert.True(Math.Abs(back - amount) <= scale * 0.000_000_000_000_000_000_000_000_01m, label);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void TryConvert_RefusesDifferentCategories()
    {
        var km = UnitTable.Find("km")!;
        var kg = UnitTable.Find("kg")!;

        Assert.False(UnitConverter.TryConvert(1m, km, kg, out var result));
        Assert.Equal(0m, result);
    }

    [Fact]
    public void TryConvert_KnownFactorsAreExact()
    {
        Assert.Equal(0.0254m, Convert(1m, "in", "m"));
        Assert.Equal(0.45359237m, Convert(1m, "lb", "kg"));
        Assert.Equal(1609.344m, Convert(1m, "mi", "m"));
        Assert.Equal(3.785411784m, Convert(1m, "gal", "l"));
        Assert.Equal(1024m, Convert(1m, "kib", "b"));
        Assert.Equal(212m, Convert(100m, "c", "f"));
        Assert.Equal(100m, Convert(212m, "f", "c"));
        Assert.Equal(-273.15m, Convert(0m, "k", "c"));
    }

    private static decimal Convert(decimal amount, string from, string to)
    {
        Assert.True(UnitConverter.TryConvert(amount, UnitTable.Find(from)!, UnitTable.Find(to)!, out var result));
        return result;
    }

    private static TheoryData<string, string> Table(string fixture)
    {
        var data = new TheoryData<string, string>();
        foreach (var line in fixture.Split('\n'))
        {
            var arrow = line.IndexOf(" => ", StringComparison.Ordinal);
            data.Add(line[..arrow].Trim(), line[(arrow + 4)..].Trim());
        }

        return data;
    }
}
