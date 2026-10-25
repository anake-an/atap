using System.Globalization;
using System.Text.RegularExpressions;

namespace Atap.Core.Malaysia;

/// <summary>
/// Request URLs for the Malaysia sources. Every builder checks its input against the bundled
/// tables, so nothing a user types can reach a query string.
/// </summary>
public static partial class MalaysiaUrls
{
    /// <summary>
    /// One month of e-Solat times. The <c>month</c> parameter is undocumented and e-Solat
    /// ignores any year, always answering for the current year in Malaysia. A January
    /// prefetch made in December must use <see cref="MirrorMonth"/> instead. Pass the year
    /// and month asked for to <see cref="EsolatParser.Parse"/>, which then refuses another
    /// period as a mismatch, and check that the answer <see cref="PrayerSchedule.Covers"/> the month.
    /// </summary>
    public static Uri EsolatMonth(string zone, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        var code = RequireZone(zone);
        return new Uri(string.Create(CultureInfo.InvariantCulture,
            $"https://{MalaysiaKeys.EsolatHost}/index.php?r=esolatApi/TakwimSolat&period=month&zone={code}&month={month}"));
    }

    /// <summary>One month from the mirror. It answers 404 for months e-Solat has not published.</summary>
    public static Uri MirrorMonth(string zone, int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 2100);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        var code = RequireZone(zone);
        return new Uri(string.Create(CultureInfo.InvariantCulture,
            $"https://{MalaysiaKeys.MirrorHost}/v2/solat/{code}?year={year}&month={month}"));
    }

    /// <summary>The 7-day forecast for one data.gov.my location id, such as "St008" or "Ds055".</summary>
    public static Uri Forecast(string locationId)
    {
        if (locationId is null || !LocationIdPattern().IsMatch(locationId))
            throw new ArgumentException("Not a data.gov.my location id.", nameof(locationId));
        return new Uri($"https://{MalaysiaKeys.DataGovHost}/weather/forecast/?filter={locationId}@location__location_id");
    }

    /// <summary>Every current MET Malaysia warning. Filtering by state happens on the PC.</summary>
    public static Uri Warnings { get; } = new($"https://{MalaysiaKeys.DataGovHost}/weather/warning/");

    private static string RequireZone(string zone) =>
        PrayerZones.Find(zone)?.Code ?? throw new ArgumentException("Not a JAKIM zone code.", nameof(zone));

    [GeneratedRegex(@"^[A-Z][a-z]\d{3}$")]
    private static partial Regex LocationIdPattern();
}
