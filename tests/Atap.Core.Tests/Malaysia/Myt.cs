namespace Atap.Core.Tests.Malaysia;

/// <summary>Builds Malaysian (UTC+8) instants for the tests.</summary>
internal static class Myt
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    public static DateTimeOffset At(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(year, month, day, hour, minute, second, Offset);
}
