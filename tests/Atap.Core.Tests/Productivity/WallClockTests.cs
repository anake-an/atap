using Atap.Core.Productivity;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity;

public class WallClockTests
{
    [Fact]
    public void ToInstant_FixedZone_UsesItsOffset()
    {
        var instant = WallClock.ToInstant(new DateOnly(2026, 10, 25), new TimeOnly(7, 0), MalaysiaZone);

        Assert.Equal(Utc(2026, 10, 24, 23), instant);
    }

    [Fact]
    public void ToInstant_TimeSkippedByForwardJump_MovesForwardByTheJump()
    {
        // 01:30 does not exist on 2026-03-29. The clock reads 02:30 one real hour after 00:30.
        var instant = WallClock.ToInstant(new DateOnly(2026, 3, 29), new TimeOnly(1, 30), Seasonal);

        Assert.Equal(Utc(2026, 3, 29, 1, 30), instant);
        Assert.Equal(new DateTime(2026, 3, 29, 2, 30, 0), WallClock.ToLocal(instant, Seasonal));
    }

    [Fact]
    public void ToInstant_TimeThatHappensTwice_ResolvesToTheFirst()
    {
        var instant = WallClock.ToInstant(new DateOnly(2026, 10, 25), new TimeOnly(1, 30), Seasonal);

        Assert.Equal(Utc(2026, 10, 25, 0, 30), instant);
    }

    [Fact]
    public void ToInstant_OrdinarySummerTime_UsesSummerOffset()
    {
        var instant = WallClock.ToInstant(new DateOnly(2026, 7, 1), new TimeOnly(9, 0), Seasonal);

        Assert.Equal(Utc(2026, 7, 1, 8), instant);
    }

    [Fact]
    public void StartOfNextDay_IsLocalMidnight()
    {
        var next = WallClock.StartOfNextDay(My(2026, 10, 25, 23, 59, 30), MalaysiaZone);

        Assert.Equal(My(2026, 10, 26, 0), next);
    }

    [Fact]
    public void ExtremeValues_AreClampedInsteadOfThrowing()
    {
        var behind = TimeZoneInfo.CreateCustomTimeZone("Test/Minus5", TimeSpan.FromHours(-5), "Minus five (test)", "M5");

        Assert.Equal(WallClock.MinDate, DateOnly.FromDateTime(WallClock.ToInstant(DateTime.MinValue, MalaysiaZone).DateTime));
        Assert.Equal(WallClock.MaxDate, DateOnly.FromDateTime(WallClock.ToInstant(DateTime.MaxValue, behind).DateTime));
        Assert.Equal(new DateOnly(1899, 12, 31), WallClock.DateOf(DateTimeOffset.MinValue, behind));
        Assert.Equal(new DateOnly(3000, 1, 1), WallClock.DateOf(DateTimeOffset.MaxValue, MalaysiaZone));
    }

    [Fact]
    public void IsSupported_RejectsDatesFarOutsideUse()
    {
        Assert.True(WallClock.IsSupported(new DateOnly(2026, 10, 25)));
        Assert.False(WallClock.IsSupported(DateOnly.MinValue));
        Assert.False(WallClock.IsSupported(default(DateTimeOffset)));
    }
}
