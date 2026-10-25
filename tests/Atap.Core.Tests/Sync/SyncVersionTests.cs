using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncVersionTests
{
    [Fact]
    public void Compare_NewerStampWins()
    {
        Assert.True(V(T0.AddMilliseconds(1), DeviceA) > V(T0, DeviceB));
    }

    [Fact]
    public void Compare_TieGoesToTheHigherDevice()
    {
        Assert.True(string.CompareOrdinal(DeviceB, DeviceA) > 0);
        Assert.True(V(T0, DeviceB) > V(T0, DeviceA));
        Assert.Equal(0, V(T0, DeviceA).CompareTo(V(T0.ToOffset(TimeSpan.FromHours(8)), DeviceA)));
    }

    [Fact]
    public void NextStamp_IsNowToTheMillisecond()
    {
        var now = T0.AddTicks(12_345);

        Assert.Equal(T0.AddMilliseconds(1), SyncVersion.NextStamp(now, null));
        Assert.Equal(T0.AddMilliseconds(1), SyncVersion.NextStamp(now, T0));
    }

    [Fact]
    public void NextStamp_IsAlwaysLaterThanThePrevious()
    {
        Assert.Equal(T0.AddMilliseconds(1), SyncVersion.NextStamp(T0, T0));
        Assert.Equal(T0.AddDays(1).AddMilliseconds(1), SyncVersion.NextStamp(T0, T0.AddDays(1)));
    }

    [Fact]
    public void NextStamp_AtTheEndOfTime_StaysAtTheLatestStampThatSurvivesARoundTrip()
    {
        Assert.Equal(SyncVersion.LatestStamp, SyncVersion.NextStamp(T0, DateTimeOffset.MaxValue));
        Assert.Equal(SyncVersion.LatestStamp, SyncVersion.NextStamp(T0, SyncVersion.LatestStamp));
        Assert.Equal(SyncVersion.LatestStamp, SyncVersion.NextStamp(T0, SyncVersion.LatestStamp.AddMilliseconds(-1)));
        Assert.Equal(SyncVersion.LatestStamp, SyncVersion.NextStamp(DateTimeOffset.MaxValue, null));

        Assert.Equal("9999-12-31T23:59:59.999Z", SyncVersion.FormatStamp(SyncVersion.LatestStamp));
        Assert.True(SyncVersion.TryParseStamp(SyncVersion.FormatStamp(SyncVersion.LatestStamp), out var back));
        Assert.Equal(SyncVersion.LatestStamp, back);
        Assert.Equal(SyncVersion.LatestStamp.UtcTicks, back.UtcTicks);
    }

    [Fact]
    public void NextStamp_LocalTime_IsStoredAsUtc()
    {
        var local = new DateTimeOffset(2026, 10, 25, 16, 0, 0, TimeSpan.FromHours(8));

        var stamp = SyncVersion.NextStamp(local, null);

        Assert.Equal(TimeSpan.Zero, stamp.Offset);
        Assert.Equal("2026-10-25T08:00:00.000Z", SyncVersion.FormatStamp(stamp));
    }

    [Theory]
    [InlineData("1B4E28BA-2FA1-11D2-883F-0016D3CCA427")]
    [InlineData("{1b4e28ba-2fa1-11d2-883f-0016d3cca427}")]
    [InlineData("1b4e28ba2fa111d2883f0016d3cca427")]
    public void TryNormalizeDevice_GivesTheLowercaseGuid(string text)
    {
        Assert.True(SyncVersion.TryNormalizeDevice(text, out var device));
        Assert.Equal(DeviceA, device);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DESKTOP-1234")]
    public void TryNormalizeDevice_RejectsOtherText(string? text)
    {
        Assert.False(SyncVersion.TryNormalizeDevice(text, out _));
    }
}
