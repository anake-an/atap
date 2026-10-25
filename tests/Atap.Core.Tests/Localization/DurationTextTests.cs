using System.Globalization;
using Atap.Core.Localization;
using Xunit;

namespace Atap.Core.Tests.Localization;

public class DurationTextTests
{
    private const string En = Localizer.English;
    private const string Ms = Localizer.Malay;

    [Theory]
    [InlineData(23, "in 23 min", "dalam 23 min")]
    [InlineData(72, "in 1 h 12 min", "dalam 1 j 12 min")]
    [InlineData(60, "in 1 h", "dalam 1 j")]
    [InlineData(1, "in 1 min", "dalam 1 min")]
    public void Until_ReadsNaturallyInBothLanguages(int minutes, string english, string malay)
    {
        Assert.Equal(english, DurationText.Until(TimeSpan.FromMinutes(minutes), En));
        Assert.Equal(malay, DurationText.Until(TimeSpan.FromMinutes(minutes), Ms));
    }

    [Fact]
    public void Until_RoundsUpToTheNextMinute()
    {
        Assert.Equal("in 23 min", DurationText.Until(new TimeSpan(0, 22, 10), En));
        Assert.Equal("in 1 min", DurationText.Until(TimeSpan.FromSeconds(5), En));
    }

    [Fact]
    public void Until_ZeroOrPast_IsNow()
    {
        Assert.Equal("now", DurationText.Until(TimeSpan.Zero, En));
        Assert.Equal("sekarang", DurationText.Until(TimeSpan.FromMinutes(-3), Ms));
    }

    [Fact]
    public void Short_ShowsDaysAndHoursForLongSpans()
    {
        var span = new TimeSpan(2, 3, 0, 0);
        Assert.Equal("2 d 3 h", DurationText.Short(span, En));
        Assert.Equal("2 hari 3 j", DurationText.Short(span, Ms));
        Assert.Equal("1 d", DurationText.Short(TimeSpan.FromDays(1), En));
    }

    [Theory]
    [InlineData(3, "3 days left", "3 hari lagi")]
    [InlineData(1, "1 day left", "1 hari lagi")]
    [InlineData(0, "today", "hari ini")]
    [InlineData(-2, "overdue", "lewat")]
    public void DaysLeft_ReadsNaturallyInBothLanguages(int days, string english, string malay)
    {
        Assert.Equal(english, DurationText.DaysLeft(days, En));
        Assert.Equal(malay, DurationText.DaysLeft(days, Ms));
    }

    [Fact]
    public void Output_FollowsTheCopyRules()
    {
        foreach (var language in new[] { En, Ms })
        {
            foreach (var minutes in new[] { -5, 0, 1, 59, 60, 61, 1439, 1440, 3000 })
            {
                var until = DurationText.Until(TimeSpan.FromMinutes(minutes), language);
                Assert.DoesNotContain('—', until);
                Assert.DoesNotContain(';', until);
            }
        }
    }

    [Theory]
    [InlineData("system", "ms-MY", "ms")]
    [InlineData("system", "ms-BN", "ms")]
    [InlineData("system", "en-MY", "en")]
    [InlineData("system", "zh-CN", "en")]
    [InlineData("en", "ms-MY", "en")]
    [InlineData("ms", "en-GB", "ms")]
    [InlineData(null, "ms-SG", "ms")]
    public void ResolveChoice_FollowsWindowsOnlyForSameAsWindows(string? choice, string windows, string expected) =>
        Assert.Equal(expected, Localizer.ResolveChoice(choice, CultureInfo.GetCultureInfo(windows)));
}
