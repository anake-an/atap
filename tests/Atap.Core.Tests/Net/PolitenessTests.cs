using System.Net.Http.Headers;
using Atap.Core.Net;

namespace Atap.Core.Tests.Net;

public sealed class PolitenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(12, 2048)]
    [InlineData(13, 3600)]
    [InlineData(200, 3600)]
    public void Backoff_Doubles_UpTo60Minutes(int failures, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), Backoff.Delay(failures, 0.5));
    }

    [Fact]
    public void Backoff_Jitter_StaysWithin20Percent()
    {
        Assert.Equal(3.2, Backoff.Delay(3, 0).TotalSeconds, 3);
        Assert.Equal(4.8, Backoff.Delay(3, 1).TotalSeconds, 3);
        Assert.Equal(3.2, Backoff.Delay(3, -5).TotalSeconds, 3);
        Assert.Equal(72, Backoff.Delay(50, 1).TotalMinutes, 3);
    }

    [Fact]
    public void RetryAfter_ReadsSecondsAndDates_AndCutsLongWaits()
    {
        Assert.Null(Backoff.RetryAfter(null, Now));
        Assert.Equal(TimeSpan.FromSeconds(120), Backoff.RetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120)), Now));
        Assert.Equal(TimeSpan.FromSeconds(90), Backoff.RetryAfter(new RetryConditionHeaderValue(Now.AddSeconds(90)), Now));
        Assert.Equal(TimeSpan.Zero, Backoff.RetryAfter(new RetryConditionHeaderValue(Now.AddSeconds(-90)), Now));
        Assert.Equal(Backoff.LongestRetryAfter, Backoff.RetryAfter(new RetryConditionHeaderValue(TimeSpan.FromDays(30)), Now));
    }

    [Theory]
    [InlineData("www.e-solat.gov.my", 60)]
    [InlineData("api.data.gov.my", 15)]
    [InlineData("lrclib.net", 2)]
    [InlineData("LRCLIB.NET", 2)]
    [InlineData("notlrclib.net", 1)]
    [InlineData("example.com", 1)]
    public void Hosts_HaveTheirOwnGap(string host, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), HostSpacing.IntervalFor(host));
    }

    [Fact]
    public void Spacing_GivesEachRequestTheNextFreeSlot()
    {
        var spacing = new HostSpacing();

        Assert.Equal(TimeSpan.Zero, spacing.Reserve("api.data.gov.my", Now));
        Assert.Equal(TimeSpan.FromSeconds(15), spacing.Reserve("api.data.gov.my", Now));
        Assert.Equal(TimeSpan.FromSeconds(25), spacing.Reserve("api.data.gov.my", Now.AddSeconds(5)));
        Assert.Equal(TimeSpan.Zero, spacing.Reserve("lrclib.net", Now));
        Assert.Equal(TimeSpan.Zero, spacing.Reserve("api.data.gov.my", Now.AddMinutes(5)));
    }
}
