using Atap.Core.Calendar;

namespace Atap.Core.Tests.Calendar;

public class MeetingScheduleTests
{
    private static readonly TimeZoneInfo KualaLumpur = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
    private static readonly TimeSpan Myt = TimeSpan.FromHours(8);

    private static string Feed(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("", events.Select(e => "BEGIN:VEVENT\r\n" + e.Trim().Replace("\n", "\r\n") + "\r\nEND:VEVENT\r\n")) + "END:VCALENDAR\r\n";

    private static IReadOnlyList<Meeting> Week(string feed, DateTimeOffset from) =>
        MeetingSchedule.Between(IcsParser.Parse(feed, KualaLumpur), from, from.AddDays(7));

    [Fact]
    public void Weekly_SkipsExdates_CancelledAndMovedInstances()
    {
        var feed = Feed(
            """
            UID:standup
            SUMMARY:Standup
            DTSTART;TZID=Asia/Kuala_Lumpur:20261019T093000
            DTEND;TZID=Asia/Kuala_Lumpur:20261019T094500
            RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR
            EXDATE;TZID=Asia/Kuala_Lumpur:20261028T093000
            """,
            """
            UID:standup
            RECURRENCE-ID;TZID=Asia/Kuala_Lumpur:20261026T093000
            SUMMARY:Standup
            STATUS:CANCELLED
            DTSTART;TZID=Asia/Kuala_Lumpur:20261026T093000
            DTEND;TZID=Asia/Kuala_Lumpur:20261026T094500
            """,
            """
            UID:standup
            RECURRENCE-ID;TZID=Asia/Kuala_Lumpur:20261030T093000
            SUMMARY:Standup (moved)
            DTSTART;TZID=Asia/Kuala_Lumpur:20261030T140000
            DTEND;TZID=Asia/Kuala_Lumpur:20261030T141500
            """);

        var meetings = Week(feed, new DateTimeOffset(2026, 10, 25, 0, 0, 0, Myt));

        Assert.Equal(
            ["2026-10-30 14:00 Standup (moved)"],
            meetings.Select(m => $"{m.Start.ToOffset(Myt):yyyy-MM-dd HH:mm} {m.Title}"));
        Assert.Equal(TimeSpan.FromMinutes(15), meetings[0].End - meetings[0].Start);
    }

    [Fact]
    public void AllDayAndCancelledEvents_AreSkipped()
    {
        var feed = Feed(
            "UID:a\nSUMMARY:Deepavali\nDTSTART;VALUE=DATE:20261108\nDTEND;VALUE=DATE:20261109",
            "UID:b\nSUMMARY:Gone\nSTATUS:CANCELLED\nDTSTART:20261026T020000Z\nDTEND:20261026T030000Z",
            "UID:c\nSUMMARY:Review\nDTSTART:20261026T020000Z\nDTEND:20261026T030000Z");

        var meetings = Week(feed, new DateTimeOffset(2026, 10, 25, 0, 0, 0, Myt));

        Assert.Equal(["Review"], meetings.Select(m => m.Title));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 10, 0, 0, Myt), meetings[0].Start);
    }

    [Fact]
    public void UtcAndKualaLumpur_AreTheSameInstant()
    {
        var feed = Feed(
            "UID:utc\nSUMMARY:Utc\nDTSTART:20261026T010000Z\nDURATION:PT30M",
            "UID:myt\nSUMMARY:Myt\nDTSTART;TZID=Asia/Kuala_Lumpur:20261026T090000\nDURATION:PT30M",
            "UID:win\nSUMMARY:Win\nDTSTART;TZID=Singapore Standard Time:20261026T090000\nDURATION:PT30M");

        var meetings = Week(feed, new DateTimeOffset(2026, 10, 25, 0, 0, 0, Myt));

        Assert.Equal(3, meetings.Count);
        Assert.All(meetings, m => Assert.Equal(new DateTimeOffset(2026, 10, 26, 1, 0, 0, TimeSpan.Zero), m.Start));
    }

    [Fact]
    public void OutlookCustomZone_UsesItsStandardOffset()
    {
        var feed =
            "BEGIN:VCALENDAR\r\nBEGIN:VTIMEZONE\r\nTZID:(UTC+08:00) Kuala Lumpur\\, Singapore\r\nBEGIN:STANDARD\r\nDTSTART:16010101T000000\r\n" +
            "TZOFFSETFROM:+0800\r\nTZOFFSETTO:+0800\r\nEND:STANDARD\r\nEND:VTIMEZONE\r\nBEGIN:VEVENT\r\nUID:x\r\nSUMMARY:Sync\r\n" +
            "DTSTART;TZID=\"(UTC+08:00) Kuala Lumpur\\, Singapore\":20261026T150000\r\nDURATION:PT1H\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var meetings = MeetingSchedule.Between(IcsParser.Parse(feed, TimeZoneInfo.Utc), DateTimeOffset.Parse("2026-10-25T00:00:00Z"), DateTimeOffset.Parse("2026-10-28T00:00:00Z"));

        Assert.Equal(new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.Zero), Assert.Single(meetings).Start);
    }

    [Fact]
    public void MonthlyAndCountAndUntil()
    {
        var feed = Feed(
            "UID:m\nSUMMARY:Second Tuesday\nDTSTART;TZID=Asia/Kuala_Lumpur:20260113T100000\nDURATION:PT1H\nRRULE:FREQ=MONTHLY;BYDAY=2TU",
            "UID:c\nSUMMARY:Three days\nDTSTART;TZID=Asia/Kuala_Lumpur:20261101T080000\nDURATION:PT1H\nRRULE:FREQ=DAILY;COUNT=3",
            "UID:u\nSUMMARY:Until\nDTSTART;TZID=Asia/Kuala_Lumpur:20261102T120000\nDURATION:PT1H\nRRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20261106T040000Z");

        var meetings = MeetingSchedule.Between(
            IcsParser.Parse(feed, KualaLumpur), new DateTimeOffset(2026, 11, 1, 0, 0, 0, Myt), new DateTimeOffset(2026, 11, 30, 0, 0, 0, Myt));

        Assert.Equal(
            ["11-01 Three days", "11-02 Three days", "11-02 Until", "11-03 Three days", "11-04 Until", "11-06 Until", "11-10 Second Tuesday"],
            meetings.Select(m => $"{m.Start.ToOffset(Myt):MM-dd} {m.Title}"));
    }

    [Fact]
    public void FoldedLinesAndEscapes_AreRead()
    {
        var feed = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:f\r\nSUMMARY:Plan\\, budget and\r\n  hiring\r\nLOCATION:Room 3\\nLevel 2\r\nDTSTART:20261026T010000Z\r\nEND:VEVENT\r\nEND:VCALENDAR";

        var e = Assert.Single(IcsParser.Parse(feed, KualaLumpur));

        Assert.Equal("Plan, budget and hiring", e.Summary);
        Assert.Equal("Room 3\nLevel 2", e.Location);
    }

    [Fact]
    public void JoinLinks_AreOnlyHttpsMsteamsAndZoommtg()
    {
        CalendarEvent Event(string? location = null, string? description = null, string? url = null) => new()
        {
            Uid = "x",
            Start = new DateTime(2026, 10, 26, 9, 0, 0),
            Zone = KualaLumpur,
            Location = location,
            Description = description,
            Url = url,
        };

        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", MeetingSchedule.JoinLink(Event(description: "Join: <https://teams.microsoft.com/l/meetup-join/abc>"))?.AbsoluteUri);
        Assert.Equal("zoommtg", MeetingSchedule.JoinLink(Event(location: "zoommtg://zoom.us/join?confno=1"))?.Scheme);
        Assert.Equal("https://us02web.zoom.us/j/123", MeetingSchedule.JoinLink(Event(location: "https://us02web.zoom.us/j/123"))?.AbsoluteUri);
        Assert.Null(MeetingSchedule.JoinLink(Event(description: "Agenda at https://example.com/doc")));
        Assert.Null(MeetingSchedule.JoinLink(Event(location: "http://meet.google.com/abc")));
        Assert.Null(MeetingSchedule.JoinLink(Event(url: "javascript://alert(1)")));
        Assert.Equal("https://example.com/room", MeetingSchedule.JoinLink(Event(url: "https://example.com/room"))?.AbsoluteUri);
    }

    [Fact]
    public void Live_FromFiveMinutesBefore_UntilTheEnd()
    {
        var start = new DateTimeOffset(2026, 10, 26, 9, 0, 0, Myt);
        var meeting = new Meeting("x", start, start.AddMinutes(30), null, null);

        Assert.False(MeetingSchedule.IsLive(meeting, start.AddMinutes(-6)));
        Assert.True(MeetingSchedule.IsLive(meeting, start.AddMinutes(-5)));
        Assert.True(MeetingSchedule.IsLive(meeting, start.AddMinutes(29)));
        Assert.False(MeetingSchedule.IsLive(meeting, start.AddMinutes(30)));
    }
}
