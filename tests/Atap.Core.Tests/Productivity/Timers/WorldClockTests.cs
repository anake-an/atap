using Atap.Core.Productivity.Timers;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Timers;

public class WorldClockTests
{
    [Fact]
    public void Read_ZoneStillInYesterday()
    {
        var reading = WorldClock.Read(Seasonal, MalaysiaZone, My(2026, 10, 26, 5, 0));

        Assert.Equal(new DateTime(2026, 10, 25, 21, 0, 0), reading.LocalTime);
        Assert.Equal(-1, reading.DayOffset);
        Assert.Equal(TimeSpan.FromHours(-8), reading.OffsetFromHome);
    }

    [Fact]
    public void Read_ZoneAlreadyInTomorrow()
    {
        var reading = WorldClock.Read(PlusTwelve, MalaysiaZone, My(2026, 10, 25, 23, 0));

        Assert.Equal(new DateTime(2026, 10, 26, 3, 0, 0), reading.LocalTime);
        Assert.Equal(1, reading.DayOffset);
        Assert.Equal(TimeSpan.FromHours(4), reading.OffsetFromHome);
    }

    [Fact]
    public void Read_FollowsDaylightSavingInTheOtherZone()
    {
        var summer = WorldClock.Read(Seasonal, MalaysiaZone, My(2026, 7, 1, 12, 0));
        var winter = WorldClock.Read(Seasonal, MalaysiaZone, My(2026, 12, 1, 12, 0));

        Assert.Equal(TimeSpan.FromHours(-7), summer.OffsetFromHome);
        Assert.Equal(TimeSpan.FromHours(-8), winter.OffsetFromHome);
        Assert.Equal(0, summer.DayOffset);
    }

    [Fact]
    public void TryFindZone_FindsKnownIdsAndRejectsOthers()
    {
        Assert.True(WorldClock.TryFindZone("UTC", out var utc));
        Assert.Equal(TimeSpan.Zero, utc.BaseUtcOffset);
        Assert.True(WorldClock.TryFindZone(" Asia/Kuala_Lumpur ", out var kualaLumpur));
        Assert.Equal(TimeSpan.FromHours(8), kualaLumpur.BaseUtcOffset);
        Assert.False(WorldClock.TryFindZone("Tiada/Zon", out _));
        Assert.False(WorldClock.TryFindZone("", out _));
        Assert.False(WorldClock.TryFindZone(null, out _));
    }

    [Theory]
    [InlineData("Asia/Kuala_Lumpur", "Kuala Lumpur")]
    [InlineData("America/Argentina/Buenos_Aires", "Buenos Aires")]
    [InlineData("Asia/Tokyo", "Tokyo")]
    [InlineData("Bukan zon", "Bukan zon")]
    public void CityOf_GivesAShortPlaceName(string id, string expected)
    {
        Assert.Equal(expected, WorldClock.CityOf(id));
    }

    [Theory]
    [InlineData("Singapore Standard Time", "Kuala Lumpur")]
    [InlineData(" Singapore Standard Time ", "Kuala Lumpur")]
    [InlineData("Tokyo Standard Time", "Tokyo")]
    [InlineData("GMT Standard Time", "London")]
    public void CityOf_WindowsId_UsesTheMalaysianMappingFirst(string id, string expected)
    {
        Assert.Equal(expected, WorldClock.CityOf(id));
    }
}
