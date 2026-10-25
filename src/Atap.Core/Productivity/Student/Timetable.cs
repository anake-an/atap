using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Student;

/// <summary>One weekly class slot, for example CSC584 on Monday 10:00 to 12:00 in Bilik 2A.</summary>
public sealed record ClassSession
{
    public const int MaxTextLength = 80;

    public string Id { get; set; } = "";

    /// <summary>Course code, for example "CSC584".</summary>
    public string Code { get; set; } = "";

    /// <summary>Course name, for example "Enterprise Programming".</summary>
    public string Name { get; set; } = "";

    /// <summary>Room, for example "Bilik 2A".</summary>
    public string Room { get; set; } = "";

    public string Lecturer { get; set; } = "";

    [JsonConverter(typeof(CamelCaseEnumConverter<DayOfWeek>))]
    public DayOfWeek Day { get; set; }

    public TimeOnly Start { get; set; }

    public TimeOnly End { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Has a code or a name, a real day, and ends after it starts on the same day.</summary>
    [JsonIgnore]
    public bool IsValid =>
        End > Start
        && Day is >= DayOfWeek.Sunday and <= DayOfWeek.Saturday
        && (!string.IsNullOrWhiteSpace(Code) || !string.IsNullOrWhiteSpace(Name));
}

/// <summary>A class on a particular date.</summary>
public sealed record ClassOccurrence(ClassSession Session, DateOnly Date, DateTimeOffset Start, DateTimeOffset End);

/// <summary>The class going on now, if any, and the next one to start.</summary>
public sealed record ClassOutlook(ClassOccurrence? Current, ClassOccurrence? Next)
{
    /// <summary>How long before a class the widget goes live.</summary>
    public static readonly TimeSpan LiveLead = TimeSpan.FromMinutes(15);

    public static ClassOutlook None { get; } = new(null, null);

    /// <summary>True during a class and from 15 minutes before the next one.</summary>
    public bool IsLiveAt(DateTimeOffset now) =>
        Current is not null || (Next is { } next && next.Start - now <= LiveLead);

    /// <summary>
    /// The next time the outlook or its live state changes: the end of the current class,
    /// 15 minutes before the next one, its start, or local midnight for the day rollover. One
    /// scheduler item at this time is enough.
    /// </summary>
    public DateTimeOffset NextChange(DateTimeOffset now, TimeZoneInfo zone)
    {
        var change = WallClock.StartOfNextDay(now, zone);
        Consider(Current?.End);
        Consider(Next?.Start - LiveLead);
        Consider(Next?.Start);
        return change;

        void Consider(DateTimeOffset? at)
        {
            if (at is { } value && value > now && value < change)
                change = value;
        }
    }
}

/// <summary>A student's weekly timetable, stored as user data.</summary>
public sealed record TimetableDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxClasses = 80;
    public const int MaxNoClassDays = 400;

    /// <summary>How far ahead to look for the next class. Covers a semester break of several weeks.</summary>
    public const int LookAheadDays = 63;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public IReadOnlyList<ClassSession> Classes { get; set; } = [];

    /// <summary>First day of the semester. No classes before it.</summary>
    public DateOnly? FirstDay { get; set; }

    /// <summary>Last day of the semester. No classes after it.</summary>
    public DateOnly? LastDay { get; set; }

    /// <summary>Days with no classes, for example public holidays or study week.</summary>
    public IReadOnlyList<DateOnly> NoClassDays { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>
    /// Repairs the values of a document that was read from a file: drops nulls and invalid classes,
    /// fixes ids, trims text and clears unusable dates. A value the reader cannot parse at all, such
    /// as "isnin" for a day or "7am" for a time, fails the whole read before this runs.
    /// </summary>
    public TimetableDocument Normalize() => this with
    {
        Classes = ItemLists.Normalize(Classes, MaxClasses, c => c.Id, (session, id) =>
        {
            var cleaned = session with
            {
                Id = id,
                Code = ProductivityText.CleanLine(session.Code, ClassSession.MaxTextLength),
                Name = ProductivityText.CleanLine(session.Name, ClassSession.MaxTextLength),
                Room = ProductivityText.CleanLine(session.Room, ClassSession.MaxTextLength),
                Lecturer = ProductivityText.CleanLine(session.Lecturer, ClassSession.MaxTextLength),
            };
            return cleaned.IsValid ? cleaned : null;
        }),
        FirstDay = FirstDay is { } first && WallClock.IsSupported(first) ? first : null,
        LastDay = LastDay is { } last && WallClock.IsSupported(last) ? last : null,
        NoClassDays = (NoClassDays ?? [])
            .Where(WallClock.IsSupported)
            .Distinct()
            .Order()
            .Take(MaxNoClassDays)
            .ToList(),
    };

    public ClassSession? Find(string id) =>
        ItemLists.Present(Classes).FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    public TimetableDocument Upsert(ClassSession session) =>
        this with { Classes = ItemLists.Upsert(Classes, session, c => c.Id, MaxClasses) };

    public TimetableDocument Remove(string id) => this with { Classes = ItemLists.Remove(Classes, id, c => c.Id) };

    /// <summary>The classes of one weekday, by start time.</summary>
    public IReadOnlyList<ClassSession> On(DayOfWeek day) =>
        ItemLists.Present(Classes)
            .Where(c => c.IsValid && c.Day == day)
            .OrderBy(c => c.Start)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The class going on at <paramref name="now"/> and the next one to start. The search runs
    /// across the end of the week, skips no-class days and stays inside the semester dates.
    /// </summary>
    public ClassOutlook OutlookAt(DateTimeOffset now, TimeZoneInfo zone)
    {
        var sessions = ItemLists.Present(Classes).Where(c => c.IsValid).ToList();
        if (sessions.Count == 0)
            return ClassOutlook.None;

        var skip = (NoClassDays ?? []).ToHashSet();
        var today = WallClock.DateOf(now, zone);
        var from = FirstDay is { } first && first > today ? first : today;
        var until = from.AddDays(LookAheadDays);
        ClassOccurrence? current = null;
        ClassOccurrence? next = null;

        for (var date = from; date <= until; date = date.AddDays(1))
        {
            if (LastDay is { } last && date > last)
                break;
            if (skip.Contains(date))
                continue;

            foreach (var session in sessions)
            {
                if (session.Day != date.DayOfWeek)
                    continue;
                var occurrence = new ClassOccurrence(
                    session,
                    date,
                    WallClock.ToInstant(date, session.Start, zone),
                    WallClock.ToInstant(date, session.End, zone));

                if (occurrence.Start <= now && now < occurrence.End)
                {
                    // Overlapping classes: the one that started last is the one going on.
                    if (current is null
                        || occurrence.Start > current.Start
                        || (occurrence.Start == current.Start && IsBefore(occurrence, current)))
                        current = occurrence;
                }
                else if (occurrence.Start > now && (next is null || IsBefore(occurrence, next)))
                {
                    next = occurrence;
                }
            }

            if (next is not null)
                break;
        }

        return new ClassOutlook(current, next);
    }

    /// <summary>Earlier start first. Classes starting together are ordered by code, then id.</summary>
    private static bool IsBefore(ClassOccurrence a, ClassOccurrence b)
    {
        if (a.Start != b.Start)
            return a.Start < b.Start;
        var byCode = string.CompareOrdinal(a.Session.Code, b.Session.Code);
        return byCode != 0 ? byCode < 0 : string.CompareOrdinal(a.Session.Id, b.Session.Id) < 0;
    }
}
