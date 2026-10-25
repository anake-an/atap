using System.Text;
using Atap.Core.Net;

namespace Atap.Core.Malaysia;

/// <summary>
/// Loads a zone's prayer times: this month, plus next month from the 25th. e-Solat first, the mirror when
/// e-Solat fails or answers for the wrong period. Each month is fetched once and kept on disk, so a month
/// costs one e-Solat request, or two in the first month the widget runs.
/// </summary>
public sealed class PrayerTimesLoader(IHttpDataService http)
{
    /// <summary>Published times never change, so a month's copy is used for as long as it is needed.</summary>
    public static readonly TimeSpan MonthMaxAge = TimeSpan.FromDays(62);

    /// <summary>Every day loaded from yesterday on, or null when this month and next could not be loaded.</summary>
    /// <remarks>Yesterday stays so that just after midnight Isyak is still the current prayer.</remarks>
    public async Task<PrayerSchedule?> LoadAsync(string zone, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var today = MalaysiaTime.DateOf(now);
        var schedule = await LoadMonthAsync(zone, today.Year, today.Month, today.Year, cancellationToken).ConfigureAwait(false);
        if (today.Day >= MalaysiaKeys.PrefetchFromDay)
        {
            var next = today.AddMonths(1);
            var upcoming = await LoadMonthAsync(zone, next.Year, next.Month, today.Year, cancellationToken).ConfigureAwait(false);
            if (upcoming is not null)
                schedule = schedule is null ? upcoming : schedule.Merge(upcoming);
        }
        return schedule?.DropBefore(today.AddDays(-1));
    }

    private async Task<PrayerSchedule?> LoadMonthAsync(string zone, int year, int month, int currentYear, CancellationToken cancellationToken)
    {
        // e-Solat always answers for Malaysia's current year, so January from December comes from the mirror.
        if (year == currentYear)
        {
            var esolat = await http.GetAsync(new FetchRequest(MalaysiaUrls.EsolatMonth(zone, month), MonthMaxAge), cancellationToken).ConfigureAwait(false);
            if (Read(esolat, EsolatParser.Parse, zone, year, month) is { } official)
                return official;
        }
        var mirror = await http.GetAsync(new FetchRequest(MalaysiaUrls.MirrorMonth(zone, year, month), MonthMaxAge), cancellationToken).ConfigureAwait(false);
        return Read(mirror, MirrorParser.Parse, zone, year, month);
    }

    private static PrayerSchedule? Read(
        FetchResult result,
        Func<string?, string?, int?, int?, ParseResult<PrayerSchedule>> parse,
        string zone,
        int year,
        int month)
    {
        if (result.Body.IsEmpty)
            return null;
        var parsed = parse(Encoding.UTF8.GetString(result.Body.Span), zone, year, month);
        return parsed.Succeeded && parsed.Value!.Covers(year, month) ? parsed.Value : null;
    }
}
