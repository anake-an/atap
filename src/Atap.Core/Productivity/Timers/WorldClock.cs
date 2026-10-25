using System.Diagnostics.CodeAnalysis;

namespace Atap.Core.Productivity.Timers;

/// <param name="LocalTime">Wall-clock time in the other zone.</param>
/// <param name="DayOffset">-1 when it is still yesterday there, 1 when it is already tomorrow, otherwise 0.</param>
/// <param name="OffsetFromHome">How far the other zone is ahead of home. Negative when behind.</param>
public readonly record struct WorldClockReading(DateTime LocalTime, int DayOffset, TimeSpan OffsetFromHome);

/// <summary>Time in other zones, compared with the home zone.</summary>
public static class WorldClock
{
    public const int MaxClocks = 6;

    /// <summary>Region code for Windows to IANA zone mapping. Zones with no Malaysian entry use their default.</summary>
    private const string MalaysiaRegion = "MY";

    public static WorldClockReading Read(TimeZoneInfo zone, TimeZoneInfo home, DateTimeOffset now)
    {
        var there = TimeZoneInfo.ConvertTime(now, zone);
        var here = TimeZoneInfo.ConvertTime(now, home);
        var dayOffset = DateOnly.FromDateTime(there.DateTime).DayNumber - DateOnly.FromDateTime(here.DateTime).DayNumber;
        return new WorldClockReading(there.DateTime, dayOffset, there.Offset - here.Offset);
    }

    /// <summary>Finds a zone by IANA id ("Asia/Tokyo") or Windows id ("Tokyo Standard Time").</summary>
    public static bool TryFindZone(string? id, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        zone = null;
        return !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out zone);
    }

    /// <summary>
    /// A short place name for a zone id: "Asia/Kuala_Lumpur" gives "Kuala Lumpur". A Windows id is
    /// mapped to its IANA id first when Windows knows it. The Malaysian mapping comes first, so
    /// "Singapore Standard Time", the zone of every Malaysian PC, gives "Kuala Lumpur". Anything
    /// else comes back as it is.
    /// </summary>
    public static string CityOf(string id)
    {
        var name = id.Trim();
        if (!name.Contains('/') && TimeZoneInfo.TryConvertWindowsIdToIanaId(name, MalaysiaRegion, out var iana))
            name = iana;
        var slash = name.LastIndexOf('/');
        if (slash >= 0 && slash < name.Length - 1)
            name = name[(slash + 1)..];
        return name.Replace('_', ' ');
    }
}
