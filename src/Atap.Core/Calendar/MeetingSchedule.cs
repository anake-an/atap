using System.Text.RegularExpressions;

namespace Atap.Core.Calendar;

/// <summary>One instance of a meeting. <see cref="JoinUrl"/> is https to a meeting site, msteams or zoommtg.</summary>
public sealed record Meeting(string Title, DateTimeOffset Start, DateTimeOffset End, string? Location, Uri? JoinUrl);

/// <summary>Turns parsed events into the meetings in a window of time. Pure.</summary>
public static partial class MeetingSchedule
{
    /// <summary>A meeting goes live on the notch this long before it starts.</summary>
    public static readonly TimeSpan LiveBefore = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Timed meetings that overlap <paramref name="from"/> to <paramref name="to"/>, by start time. All-day events,
    /// cancelled events, EXDATEs and cancelled instances are left out. An edited instance replaces the one it moves.
    /// </summary>
    public static IReadOnlyList<Meeting> Between(IReadOnlyList<CalendarEvent> events, DateTimeOffset from, DateTimeOffset to)
    {
        var overrides = events
            .Where(e => e.RecurrenceId is not null)
            .GroupBy(e => e.Uid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.RecurrenceId!.Value.UtcDateTime).ToHashSet(), StringComparer.Ordinal);
        var meetings = new List<Meeting>();

        foreach (var e in events)
        {
            if (e.AllDay || e.Cancelled)
                continue;
            if (e.RecurrenceId is not null || e.Rule is null)
            {
                Add(e, e.StartOffset);
                continue;
            }
            overrides.TryGetValue(e.Uid, out var moved);
            var excluded = e.ExDates.Select(d => d.UtcDateTime).ToHashSet();
            foreach (var wall in e.Rule.Expand(e.Start, e.Zone, to))
            {
                var start = CalendarTime.ToOffset(wall, e.Zone);
                if (excluded.Contains(start.UtcDateTime) || moved?.Contains(start.UtcDateTime) == true)
                    continue;
                Add(e, start);
            }
        }
        return meetings.OrderBy(m => m.Start).ThenBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

        void Add(CalendarEvent e, DateTimeOffset start)
        {
            var end = start + e.Duration;
            if (end <= from || start >= to)
                return;
            meetings.Add(new Meeting(e.Summary, start, end, string.IsNullOrWhiteSpace(e.Location) ? null : e.Location.Trim(), JoinLink(e)));
        }
    }

    /// <summary>The first meeting that has not ended.</summary>
    public static Meeting? Next(IReadOnlyList<Meeting> meetings, DateTimeOffset now) => meetings.FirstOrDefault(m => m.End > now);

    /// <summary>From <see cref="LiveBefore"/> before the start until the end.</summary>
    public static bool IsLive(Meeting meeting, DateTimeOffset now) => now >= meeting.Start - LiveBefore && now < meeting.End;

    /// <summary>
    /// msteams: and zoommtg: links anywhere. https links from the conference properties and URL, and from the
    /// location or notes only when they go to a known meeting site, so a random link in the notes is not a Join button.
    /// </summary>
    public static Uri? JoinLink(CalendarEvent e)
    {
        foreach (var text in e.ConferenceUrls.Append(e.Url))
        {
            if (FirstLink(text, anyHttpsHost: true) is { } link)
                return link;
        }
        foreach (var text in new[] { e.Location, e.Description })
        {
            if (FirstLink(text, anyHttpsHost: false) is { } link)
                return link;
        }
        return null;
    }

    private static readonly string[] MeetingHosts =
    [
        "teams.microsoft.com", "teams.live.com", "zoom.us", "meet.google.com", "webex.com", "whereby.com", "meet.jit.si", "gotomeeting.com", "chime.aws",
    ];

    private static Uri? FirstLink(string? text, bool anyHttpsHost)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        foreach (Match match in LinkPattern().Matches(text))
        {
            var candidate = match.Value.TrimEnd('.', ',', ')', ']', '>', ';');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                continue;
            if (uri.Scheme is "msteams" or "zoommtg")
                return uri;
            if (uri.Scheme == Uri.UriSchemeHttps && (anyHttpsHost || IsMeetingHost(uri.Host)))
                return uri;
        }
        return null;
    }

    private static bool IsMeetingHost(string host) =>
        MeetingHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"(?:https|msteams|zoommtg)://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
