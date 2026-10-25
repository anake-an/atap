using Atap.Core.Custom;

namespace Atap.Core.Metrics;

/// <summary>Processor time since boot, in 100 ns units, as GetSystemTimes reports it. Kernel time includes idle time.</summary>
public readonly record struct CpuTimes(long Idle, long Kernel, long User);

/// <summary>Physical memory in bytes.</summary>
public readonly record struct MemoryReading(ulong Total, ulong Available)
{
    public ulong Used => Total > Available ? Total - Available : 0;

    /// <summary>0 to 100.</summary>
    public double Percent => Total == 0 ? 0 : Used * 100.0 / Total;
}

/// <summary>The system drive in bytes.</summary>
public readonly record struct DiskReading(string Name, ulong Total, ulong Free)
{
    public ulong Used => Total > Free ? Total - Free : 0;

    /// <summary>0 to 100.</summary>
    public double Percent => Total == 0 ? 0 : Used * 100.0 / Total;
}

/// <summary>One reading of the machine. Values Windows did not give are null.</summary>
public sealed record SystemReading(CpuTimes? Cpu, MemoryReading? Memory, DiskReading? Disk);

/// <summary>The maths for the system monitor, pure.</summary>
public static class SystemUsage
{
    public const double WarningPercent = 80;
    public const double CriticalPercent = 95;

    /// <summary>
    /// CPU use between two readings, 0 to 100. Busy time is kernel plus user minus idle, over kernel plus user.
    /// Null when there is no earlier reading or no time passed, so the first sample never shows a made-up value.
    /// </summary>
    public static double? CpuPercent(CpuTimes? before, CpuTimes? after)
    {
        if (before is not { } a || after is not { } b)
            return null;
        var idle = b.Idle - a.Idle;
        var total = (b.Kernel - a.Kernel) + (b.User - a.User);
        if (total <= 0 || idle < 0)
            return null;
        return Math.Clamp((total - idle) * 100.0 / total, 0, 100);
    }

    /// <summary>Amber from 80 %, rose from 95 %.</summary>
    public static ValueLevel LevelOf(double? percent) => percent switch
    {
        null => ValueLevel.Unknown,
        >= CriticalPercent => ValueLevel.Critical,
        >= WarningPercent => ValueLevel.Warning,
        _ => ValueLevel.Normal,
    };

    /// <summary>"9.8 GB", "512 MB": one decimal under 10, none above.</summary>
    public static string Size(ulong bytes, IFormatProvider culture)
    {
        const double Gb = 1024d * 1024 * 1024;
        const double Mb = 1024d * 1024;
        if (bytes >= Gb)
        {
            var gb = bytes / Gb;
            return gb < 10 ? gb.ToString("0.0", culture) + " GB" : gb.ToString("0", culture) + " GB";
        }
        return (bytes / Mb).ToString("0", culture) + " MB";
    }
}
