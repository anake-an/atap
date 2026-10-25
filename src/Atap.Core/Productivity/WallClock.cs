namespace Atap.Core.Productivity;

/// <summary>
/// Converts between instants and wall-clock times in a time zone. Every productivity model goes
/// through here, so alarms, reminders and classes agree on daylight saving edges.
/// </summary>
public static class WallClock
{
    /// <summary>Earliest date the models accept. Older dates in a file are treated as unset.</summary>
    public static readonly DateOnly MinDate = new(1900, 1, 1);

    /// <summary>Latest date the models accept. Later dates in a file are treated as unset.</summary>
    public static readonly DateOnly MaxDate = new(2999, 12, 31);

    private static readonly DateTime MinWall = MinDate.ToDateTime(TimeOnly.MinValue);
    private static readonly DateTime MaxWall = MaxDate.ToDateTime(TimeOnly.MaxValue);
    private static readonly DateTimeOffset MinInstant = new(MinWall, TimeSpan.Zero);
    private static readonly DateTimeOffset MaxInstant = new(MaxWall, TimeSpan.Zero);

    public static bool IsSupported(DateOnly date) => date >= MinDate && date <= MaxDate;

    public static bool IsSupported(DateTimeOffset instant) => IsSupported(DateOnly.FromDateTime(instant.UtcDateTime));

    /// <summary>
    /// The wall-clock time at <paramref name="instant"/> in <paramref name="zone"/>. Instants
    /// outside the supported dates are clamped, so a broken file never throws here.
    /// </summary>
    public static DateTime ToLocal(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant < MinInstant ? MinInstant : instant > MaxInstant ? MaxInstant : instant, zone).DateTime;

    /// <summary>The local date at <paramref name="instant"/> in <paramref name="zone"/>.</summary>
    public static DateOnly DateOf(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(ToLocal(instant, zone));

    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone) =>
        ToInstant(date.ToDateTime(time), zone);

    /// <summary>
    /// The instant a wall-clock time happens in <paramref name="zone"/>. A time skipped by a forward
    /// jump moves forward by the length of the jump. A time that happens twice resolves to the first.
    /// Times outside the supported dates are clamped, so a broken file never throws here.
    /// </summary>
    public static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        var clamped = local < MinWall ? MinWall : local > MaxWall ? MaxWall : local;
        var wall = DateTime.SpecifyKind(clamped, DateTimeKind.Unspecified);
        var probe = DateTime.SpecifyKind(wall, DateTimeKind.Utc);
        var withOffsetBefore = new DateTimeOffset(wall, zone.GetUtcOffset(probe.AddDays(-1)));
        var withOffsetAfter = new DateTimeOffset(wall, zone.GetUtcOffset(probe.AddDays(1)));

        var first = withOffsetBefore <= withOffsetAfter ? withOffsetBefore : withOffsetAfter;
        var second = withOffsetBefore <= withOffsetAfter ? withOffsetAfter : withOffsetBefore;
        if (Happens(first, wall, zone))
            return first;
        if (Happens(second, wall, zone))
            return second;

        // Skipped by a forward jump. The offset in force before the jump lands just after it.
        return withOffsetBefore;
    }

    /// <summary>The next local midnight after <paramref name="now"/>, when the date changes.</summary>
    public static DateTimeOffset StartOfNextDay(DateTimeOffset now, TimeZoneInfo zone) =>
        ToInstant(DateOf(now, zone).AddDays(1), TimeOnly.MinValue, zone);

    private static bool Happens(DateTimeOffset candidate, DateTime wall, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(candidate, zone).DateTime == wall;
}
