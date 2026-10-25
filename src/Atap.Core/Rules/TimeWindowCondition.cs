namespace Atap.Core.Rules;

/// <summary>
/// Matches from <see cref="From"/> until <see cref="To"/> local time, on windows that start on one of <see cref="Days"/>.
/// The start is included and the end is not.
/// <list type="bullet">
/// <item>An end earlier than the start crosses midnight, and the window belongs to the day it starts:
/// Friday 22:00 to 07:00 runs until Saturday 07:00.</item>
/// <item>Equal times mean the whole 24 hours from <see cref="From"/>.</item>
/// </list>
/// </summary>
public sealed class TimeWindowCondition : RuleCondition
{
    private static readonly TimeSpan FullDay = TimeSpan.FromDays(1);

    public override ConditionKind Kind => ConditionKind.TimeWindow;

    /// <summary>Days a window may start on. Empty means every day.</summary>
    public List<DayOfWeek> Days { get; set; } = [];

    public TimeOnly From { get; set; }

    public TimeOnly To { get; set; }

    public override bool IsValid => true;

    /// <summary>How long one window lasts, more than zero and at most a day.</summary>
    public TimeSpan Length
    {
        get
        {
            // TimeOnly subtraction wraps around midnight, so 22:00 to 07:00 is 9 hours.
            var length = To - From;
            return length == TimeSpan.Zero ? FullDay : length;
        }
    }

    public override bool Matches(RuleContext context) => IsOpenAt(ToLocal(context.Now, context.TimeZone));

    /// <summary>Whether a window is open at a local wall-clock time.</summary>
    public bool IsOpenAt(DateTime localTime)
    {
        var length = Length;
        // A window lasts at most a day, so only one starting today or yesterday can be open.
        for (var daysBack = 0; daysBack <= 1; daysBack++)
        {
            var day = localTime.Date.AddDays(-daysBack);
            if (!StartsOn(day.DayOfWeek))
                continue;

            var start = day + From.ToTimeSpan();
            if (start <= localTime && localTime < start + length)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The first instant after <paramref name="now"/> at which the condition changes from open to closed
    /// or back. Touching windows, such as Saturday and Sunday all day, count as one. Null when it never
    /// changes, for example open all day on every day.
    /// </summary>
    public DateTimeOffset? NextChange(DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var local = ToLocal(now, timeZone);
        var openNow = IsOpenAt(local);
        var length = Length;

        // Every start and end from yesterday to a week ahead. That covers the next change of any day set.
        var edges = new List<DateTime>(20);
        for (var dayOffset = -1; dayOffset <= 8; dayOffset++)
        {
            var day = local.Date.AddDays(dayOffset);
            if (!StartsOn(day.DayOfWeek))
                continue;

            var start = day + From.ToTimeSpan();
            edges.Add(start);
            edges.Add(start + length);
        }
        edges.Sort();

        // The state only changes at an edge, so the first later edge with the other state is the change.
        foreach (var edge in edges)
        {
            if (edge <= local || IsOpenAt(edge) == openNow)
                continue;
            if (ToInstant(edge, timeZone, now) is { } instant)
                return instant;
        }
        return null;
    }

    private bool StartsOn(DayOfWeek day) => Days is not { Count: > 0 } || Days.Contains(day);

    private static DateTime ToLocal(DateTimeOffset now, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(now, timeZone).DateTime;

    /// <summary>
    /// The instant of a wall-clock time. A time skipped by a daylight saving jump happens when the clock
    /// jumps. A time that occurs twice gives the first occurrence after <paramref name="after"/>.
    /// </summary>
    private static DateTimeOffset? ToInstant(DateTime localTime, TimeZoneInfo timeZone, DateTimeOffset after)
    {
        for (var minutes = 0; minutes < 24 * 60 && timeZone.IsInvalidTime(localTime); minutes++)
            localTime = localTime.AddMinutes(1);

        if (timeZone.IsAmbiguousTime(localTime))
        {
            DateTimeOffset? first = null;
            foreach (var offset in timeZone.GetAmbiguousTimeOffsets(localTime))
            {
                var candidate = new DateTimeOffset(localTime, offset);
                if (candidate > after && (first is null || candidate < first))
                    first = candidate;
            }
            return first;
        }

        var instant = new DateTimeOffset(localTime, timeZone.GetUtcOffset(localTime));
        return instant > after ? instant : null;
    }
}
