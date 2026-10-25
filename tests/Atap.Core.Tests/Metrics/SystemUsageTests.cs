using System.Globalization;
using Atap.Core.Custom;
using Atap.Core.Metrics;

namespace Atap.Core.Tests.Metrics;

public class SystemUsageTests
{
    [Fact]
    public void Cpu_IsBusyOverTotal_BetweenTwoReadings()
    {
        var before = new CpuTimes(Idle: 1000, Kernel: 1500, User: 500);
        // 400 more total: 100 more idle inside 300 more kernel, plus 100 user. Busy = 400 - 100.
        var after = new CpuTimes(Idle: 1100, Kernel: 1800, User: 600);

        Assert.Equal(75, SystemUsage.CpuPercent(before, after));
    }

    [Fact]
    public void Cpu_WithoutTwoReadings_OrNoTimePassing_IsUnknown()
    {
        var reading = new CpuTimes(1, 2, 3);

        Assert.Null(SystemUsage.CpuPercent(null, reading));
        Assert.Null(SystemUsage.CpuPercent(reading, null));
        Assert.Null(SystemUsage.CpuPercent(reading, reading));
    }

    [Fact]
    public void Cpu_StaysBetween0And100()
    {
        Assert.Equal(0, SystemUsage.CpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(100, 100, 0)));
        Assert.Equal(100, SystemUsage.CpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(0, 50, 50)));
    }

    [Fact]
    public void MemoryAndDisk_Percentages()
    {
        Assert.Equal(25, new MemoryReading(16, 12).Percent);
        Assert.Equal(0, new MemoryReading(0, 0).Percent);
        Assert.Equal(90, new DiskReading("C:", 100, 10).Percent);
    }

    [Theory]
    [InlineData(null, ValueLevel.Unknown)]
    [InlineData(50.0, ValueLevel.Normal)]
    [InlineData(80.0, ValueLevel.Warning)]
    [InlineData(95.0, ValueLevel.Critical)]
    public void Levels(double? percent, ValueLevel level) => Assert.Equal(level, SystemUsage.LevelOf(percent));

    [Fact]
    public void Sizes_ReadNaturally()
    {
        var culture = CultureInfo.InvariantCulture;
        Assert.Equal("9.8 GB", SystemUsage.Size((ulong)(9.8 * 1024 * 1024 * 1024), culture));
        Assert.Equal("16 GB", SystemUsage.Size(16UL * 1024 * 1024 * 1024, culture));
        Assert.Equal("512 MB", SystemUsage.Size(512UL * 1024 * 1024, culture));
    }
}
