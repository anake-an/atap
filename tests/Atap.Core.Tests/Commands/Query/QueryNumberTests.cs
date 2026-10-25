using System.Globalization;
using Atap.Core.Commands.Query;

namespace Atap.Core.Tests.Commands.Query;

public class QueryNumberTests
{
    [Theory]
    [InlineData("en-MY")]
    [InlineData("ms-MY")]
    public void Format_UsesMalaysianSeparators(string culture)
    {
        var provider = CultureInfo.GetCultureInfo(culture);

        Assert.Equal("1,234,567.891", QueryNumber.Format(1_234_567.891m, provider));
        Assert.Equal("-0.5", QueryNumber.Format(-0.5m, provider));
    }

    [Fact]
    public void Format_WithoutGroupingPastesCleanly()
    {
        Assert.Equal("1234567.891", QueryNumber.Format(1_234_567.891m, grouping: false));
    }

    [Fact]
    public void Format_DropsTrailingZerosAndNegativeZero()
    {
        Assert.Equal("2.5", QueryNumber.Format(2.500m));
        Assert.Equal("0", QueryNumber.Format(-0.0m));
        Assert.Equal("0", QueryNumber.Format(0m * -1m));
    }

    [Fact]
    public void Round_HidesDivisionNoise()
    {
        var third = 1m / 3m;

        Assert.Equal(1m, QueryNumber.Round(third * 3m));
        Assert.Equal(0.333333333333333m, QueryNumber.Round(third));
    }

    [Fact]
    public void Round_KeepsWholeNumbersExact()
    {
        Assert.Equal("12,345,678,901,234,567,890", QueryNumber.Format(12_345_678_901_234_567_890m));
    }

    [Fact]
    public void Round_NeverDropsTheWholePart()
    {
        Assert.Equal("123,456,789,012,346", QueryNumber.Format(123_456_789_012_345.678m));
        Assert.Equal("12,345.6789012346", QueryNumber.Format(12_345.678901234567m));
    }

    [Fact]
    public void Round_KeepsSignificantDigitsOfSmallValues()
    {
        Assert.Equal("0.000000000001", QueryNumber.Format(0.000000000001m));
        Assert.Equal("0.000123456789012346", QueryNumber.Format(0.00012345678901234567m));
    }
}
