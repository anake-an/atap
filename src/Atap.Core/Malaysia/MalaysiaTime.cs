namespace Atap.Core.Malaysia;

/// <summary>Malaysia time, UTC+8 all year with no daylight saving.</summary>
public static class MalaysiaTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    /// <summary>The calendar date in Malaysia at <paramref name="instant"/>.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    /// <summary>A Malaysian wall-clock time on <paramref name="date"/> as an instant.</summary>
    public static DateTimeOffset At(DateOnly date, TimeOnly time) =>
        new(date.ToDateTime(time), Offset);
}
