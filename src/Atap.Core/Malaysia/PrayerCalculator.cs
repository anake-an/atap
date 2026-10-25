namespace Atap.Core.Malaysia;

/// <summary>
/// Prayer times computed on the PC with JAKIM's parameters, for use outside Malaysia. Results
/// are approximate and the UI says so. Inside Malaysia the official e-Solat times are used.
/// </summary>
/// <remarks>
/// The tests check every time against 67 recorded e-Solat days, 66 for SGR01 between August and
/// December 2026 and one for WLY01. All of them match to within 2 minutes. Other zones and
/// months are not checked. Subuh and Isyak are at 18 degrees below the horizon. Asar is when
/// a shadow is its object's length plus the noon shadow. Sunrise and sunset are at 0.833 degrees
/// below the horizon. A precaution margin (ihtiyati) is added and times are rounded up to the
/// minute, except Syuruk, which is rounded down. Imsak is 10 minutes before Subuh and Dhuha
/// 25 minutes after Syuruk, as in every recorded e-Solat day. The sun's position uses the
/// low-precision formulas from the Astronomical Almanac, good to about a minute between 1950 and 2050.
/// </remarks>
public static class PrayerCalculator
{
    public const double SubuhAngle = 18;
    public const double IsyakAngle = 18;
    public static readonly TimeSpan ImsakBeforeSubuh = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DhuhaAfterSyuruk = TimeSpan.FromMinutes(25);

    private const double HorizonAngle = 0.833;

    // Ihtiyati in minutes, added before rounding.
    private const double SubuhMargin = 2;
    private const double ZohorMargin = 2;
    private const double AsarMargin = 1;
    private const double MaghribMargin = 1;
    private const double IsyakMargin = 1;

    /// <summary>
    /// One day of times, or null where the sun does not rise or set that day. Where Subuh or
    /// Isyak never reach their angle (high latitudes in summer), they fall back to a share of
    /// the night in proportion to the angle.
    /// </summary>
    /// <param name="utcOffset">The local UTC offset on <paramref name="date"/>.</param>
    public static PrayerDay? Calculate(DateOnly date, GeoPoint location, TimeSpan utcOffset)
    {
        if (!location.IsValid)
            return null;

        var zoneHours = utcOffset.TotalHours;
        var midnightUt = date.DayNumber + 1721425.5 - zoneHours / 24;

        // Start from rough guesses and refine twice with the sun's position at each time.
        double subuh = 5, syuruk = 6, zohor = 12, asar = 15.5, maghrib = 18, isyak = 19;
        for (var pass = 0; pass < 2; pass++)
        {
            var subuhNext = Event(midnightUt, subuh, location, zoneHours, -SubuhAngle, before: true);
            var syurukNext = Event(midnightUt, syuruk, location, zoneHours, -HorizonAngle, before: true);
            var maghribNext = Event(midnightUt, maghrib, location, zoneHours, -HorizonAngle, before: false);
            var isyakNext = Event(midnightUt, isyak, location, zoneHours, -IsyakAngle, before: false);
            var asarNext = AsarTime(midnightUt, asar, location, zoneHours);
            if (syurukNext is null || maghribNext is null || asarNext is null)
                return null;

            zohor = Noon(midnightUt, zohor, location, zoneHours);
            syuruk = syurukNext.Value;
            maghrib = maghribNext.Value;
            asar = asarNext.Value;

            // A share of the night where the angle is never reached or the twilight runs too long.
            var night = 24 - (maghrib - syuruk);
            var subuhLimit = syuruk - night * SubuhAngle / 60;
            var isyakLimit = maghrib + night * IsyakAngle / 60;
            subuh = subuhNext is { } s && s >= subuhLimit ? s : subuhLimit;
            isyak = isyakNext is { } i && i <= isyakLimit ? i : isyakLimit;
        }

        var subuhTime = Clock(date, utcOffset, subuh, SubuhMargin, roundUp: true);
        var syurukTime = Clock(date, utcOffset, syuruk, 0, roundUp: false);
        return new PrayerDay(
            date,
            null,
            subuhTime - ImsakBeforeSubuh,
            subuhTime,
            syurukTime,
            syurukTime + DhuhaAfterSyuruk,
            Clock(date, utcOffset, zohor, ZohorMargin, roundUp: true),
            Clock(date, utcOffset, asar, AsarMargin, roundUp: true),
            Clock(date, utcOffset, maghrib, MaghribMargin, roundUp: true),
            Clock(date, utcOffset, isyak, IsyakMargin, roundUp: true));
    }

    /// <summary>
    /// A calculated schedule for <paramref name="dayCount"/> days from <paramref name="first"/>.
    /// Days where the sun does not rise or set are left out, so the schedule reads as stale there.
    /// </summary>
    public static PrayerSchedule Schedule(GeoPoint location, DateOnly first, int dayCount, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentOutOfRangeException.ThrowIfNegative(dayCount);

        var days = new List<PrayerDay>(dayCount);
        for (var i = 0; i < dayCount; i++)
        {
            var date = first.AddDays(i);
            var offset = timeZone.GetUtcOffset(date.ToDateTime(new TimeOnly(12, 0)));
            if (Calculate(date, location, offset) is { } day)
                days.Add(day);
        }
        return new PrayerSchedule(null, PrayerSource.Calculated, days);
    }

    private static double Noon(double midnightUt, double guess, GeoPoint location, double zoneHours)
    {
        var (_, equation) = Sun(midnightUt + guess / 24);
        return 12 + zoneHours - location.Longitude / 15 - equation;
    }

    /// <summary>Local hours when the sun is at <paramref name="altitude"/> before or after noon.</summary>
    private static double? Event(double midnightUt, double guess, GeoPoint location, double zoneHours, double altitude, bool before)
    {
        var (declination, equation) = Sun(midnightUt + guess / 24);
        var noon = 12 + zoneHours - location.Longitude / 15 - equation;
        var hours = HourAngle(altitude, location.Latitude, declination);
        if (hours is null)
            return null;
        return before ? noon - hours.Value : noon + hours.Value;
    }

    private static double? AsarTime(double midnightUt, double guess, GeoPoint location, double zoneHours)
    {
        var (declination, _) = Sun(midnightUt + guess / 24);
        var altitude = Degrees(Math.Atan(1 / (1 + Math.Tan(Radians(Math.Abs(location.Latitude - declination))))));
        return Event(midnightUt, guess, location, zoneHours, altitude, before: false);
    }

    private static double? HourAngle(double altitude, double latitude, double declination)
    {
        var lat = Radians(latitude);
        var dec = Radians(declination);
        var cos = (Math.Sin(Radians(altitude)) - Math.Sin(lat) * Math.Sin(dec)) / (Math.Cos(lat) * Math.Cos(dec));
        return cos is < -1 or > 1 ? null : Degrees(Math.Acos(cos)) / 15;
    }

    /// <summary>The sun's declination in degrees and the equation of time in hours.</summary>
    private static (double Declination, double Equation) Sun(double julianDay)
    {
        var d = julianDay - 2451545.0;
        var meanAnomaly = Radians(Normalize(357.529 + 0.98560028 * d, 360));
        var meanLongitude = Normalize(280.459 + 0.98564736 * d, 360);
        var longitude = Radians(Normalize(meanLongitude + 1.915 * Math.Sin(meanAnomaly) + 0.020 * Math.Sin(2 * meanAnomaly), 360));
        var obliquity = Radians(23.439 - 0.00000036 * d);

        var rightAscension = Normalize(Degrees(Math.Atan2(Math.Cos(obliquity) * Math.Sin(longitude), Math.Cos(longitude))) / 15, 24);
        var declination = Degrees(Math.Asin(Math.Sin(obliquity) * Math.Sin(longitude)));
        var equation = meanLongitude / 15 - rightAscension;
        equation -= 24 * Math.Round(equation / 24);
        return (declination, equation);
    }

    private static DateTimeOffset Clock(DateOnly date, TimeSpan utcOffset, double hours, double marginMinutes, bool roundUp)
    {
        var minutes = hours * 60 + marginMinutes;
        // Strip float noise so an exact minute does not round up to the next one.
        minutes = Math.Round(minutes, 6);
        var whole = roundUp ? Math.Ceiling(minutes) : Math.Floor(minutes);
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), utcOffset).AddMinutes(whole);
    }

    private static double Normalize(double value, double range)
    {
        var result = value % range;
        return result < 0 ? result + range : result;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Degrees(double radians) => radians * 180 / Math.PI;
}
