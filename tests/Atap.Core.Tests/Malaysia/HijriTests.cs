using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class HijriTests
{
    [Fact]
    public void TryParse_ReadsTheESolatForm()
    {
        Assert.True(HijriDate.TryParse("1448-04-19", out var date));
        Assert.Equal(new HijriDate(1448, 4, 19), date);
        Assert.Equal("1448-04-19", date.ToString());
    }

    [Theory]
    [InlineData("1448-13-01")]
    [InlineData("1448-04-31")]
    [InlineData("1448-00-10")]
    [InlineData("1448-04")]
    [InlineData("1448/04/19")]
    [InlineData("+1448-04-19")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsOtherShapes(string? text)
    {
        Assert.False(HijriDate.TryParse(text, out _));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ms")]
    public void Format_UsesMalaysianMonthNamesInBothLanguages(string language)
    {
        Assert.Equal("19 Rabiulakhir 1448", new HijriDate(1448, 4, 19).Format(language));
        Assert.Equal("1 Syawal 1447", new HijriDate(1447, 10, 1).Format(language));
    }

    [Fact]
    public void Months_TablesHaveTwelveMalaysianNames()
    {
        string[] expected =
        [
            "Muharam", "Safar", "Rabiulawal", "Rabiulakhir", "Jamadilawal", "Jamadilakhir",
            "Rejab", "Syaaban", "Ramadan", "Syawal", "Zulkaedah", "Zulhijah",
        ];

        Assert.Equal(expected, HijriMonths.Malay);
        Assert.Equal(expected, HijriMonths.English);
        Assert.Equal("Ramadan", HijriMonths.Name(9, "ms"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Months_NameOutOfRange_Throws(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HijriMonths.Name(month, "en"));
    }

    [Fact]
    public void IsRamadan_OnlyForTheNinthMonth()
    {
        Assert.True(new HijriDate(1447, 9, 1).IsRamadan);
        Assert.False(new HijriDate(1447, 10, 1).IsRamadan);
    }

    [Fact]
    public void CompareTo_OrdersByYearMonthDay()
    {
        Assert.True(new HijriDate(1447, 12, 29).CompareTo(new HijriDate(1448, 1, 1)) < 0);
        Assert.True(new HijriDate(1448, 5, 1).CompareTo(new HijriDate(1448, 4, 30)) > 0);
        Assert.Equal(0, new HijriDate(1448, 4, 19).CompareTo(new HijriDate(1448, 4, 19)));
    }

    [Fact]
    public void RecordedDays_CarryTheOfficialDate()
    {
        var october = EsolatParser.Parse(EsolatFixtures.Sgr01October2026).Value!;

        Assert.Equal(new HijriDate(1448, 4, 19), october.Days[0].Hijri);
        Assert.Equal(new HijriDate(1448, 5, 20), october.Days[^1].Hijri);
        Assert.All(october.Days, d => Assert.NotNull(d.Hijri));
    }

    [Fact]
    public void FromGregorian_AgreesWithESolatForTheRecordedMonth()
    {
        var october = EsolatParser.Parse(EsolatFixtures.Sgr01October2026).Value!;

        Assert.All(october.Days, d => Assert.Equal(d.Hijri, HijriDate.FromGregorian(d.Date)));
    }

    [Fact]
    public void FromGregorian_OffsetCorrectsADifferentMonthStart()
    {
        // e-Solat gives 1 Ramadan 1447 on 19 February 2026 (recorded SGR01 year response).
        // The tabular calendar is two days ahead there, which the user offset corrects.
        var date = new DateOnly(2026, 2, 19);

        Assert.Equal(new HijriDate(1447, 9, 3), HijriDate.FromGregorian(date));
        Assert.Equal(new HijriDate(1447, 9, 1), HijriDate.FromGregorian(date, -2));
    }

    [Fact]
    public void FromGregorian_ClampsTheOffset()
    {
        var date = new DateOnly(2026, 10, 1);

        Assert.Equal(HijriDate.FromGregorian(date, 2), HijriDate.FromGregorian(date, 7));
        Assert.Equal(HijriDate.FromGregorian(date, -2), HijriDate.FromGregorian(date, -9));
        Assert.Equal(HijriDate.FromGregorian(date.AddDays(1)), HijriDate.FromGregorian(date, 1));
    }
}
