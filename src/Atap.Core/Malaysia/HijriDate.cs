using System.Globalization;

namespace Atap.Core.Malaysia;

/// <summary>A Hijri date. Malaysia's official date comes from e-Solat, so this is mostly parsed, not computed.</summary>
public readonly record struct HijriDate(int Year, int Month, int Day) : IComparable<HijriDate>
{
    /// <summary>The user offset is limited to this many days either way.</summary>
    public const int MaxOffsetDays = 2;

    // Set to 0 so the result never depends on the Windows "AddHijriDate" registry value.
    private static readonly HijriCalendar Tabular = new() { HijriAdjustment = 0 };

    public bool IsValid => Year is >= 1 and <= 9999 && Month is >= 1 and <= 12 && Day is >= 1 and <= 30;

    public bool IsRamadan => Month == 9;

    /// <summary>Parses the e-Solat form "1448-04-19".</summary>
    public static bool TryParse(string? text, out HijriDate date)
    {
        date = default;
        var parts = text?.Trim().Split('-');
        if (parts is not { Length: 3 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
            return false;

        var parsed = new HijriDate(year, month, day);
        if (!parsed.IsValid)
            return false;
        date = parsed;
        return true;
    }

    /// <summary>
    /// The tabular Hijri date for a Gregorian date. It can differ from JAKIM's date by a day or
    /// more (two days at the start of Ramadan 1447), so it is only a fallback, shifted by the
    /// user's offset (clamped to -2 to +2 days).
    /// </summary>
    public static HijriDate FromGregorian(DateOnly date, int offsetDays = 0)
    {
        var shifted = date.AddDays(Math.Clamp(offsetDays, -MaxOffsetDays, MaxOffsetDays)).ToDateTime(TimeOnly.MinValue);
        return new HijriDate(Tabular.GetYear(shifted), Tabular.GetMonth(shifted), Tabular.GetDayOfMonth(shifted));
    }

    /// <summary>"19 Rabiulakhir 1448".</summary>
    public string Format(string language) =>
        string.Create(CultureInfo.InvariantCulture, $"{Day} {HijriMonths.Name(Month, language)} {Year}");

    public int CompareTo(HijriDate other) =>
        Year != other.Year ? Year.CompareTo(other.Year)
        : Month != other.Month ? Month.CompareTo(other.Month)
        : Day.CompareTo(other.Day);

    /// <summary>The e-Solat form "1448-04-19".</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}-{Day:D2}");
}

/// <summary>
/// Hijri month names. Both languages use the Malaysian forms (Rabiulakhir, not Rabi' al-Thani),
/// as the maintainer decided. The tables are kept apart so a language can change on its own.
/// </summary>
public static class HijriMonths
{
    public static IReadOnlyList<string> English { get; } =
    [
        "Muharam", "Safar", "Rabiulawal", "Rabiulakhir", "Jamadilawal", "Jamadilakhir",
        "Rejab", "Syaaban", "Ramadan", "Syawal", "Zulkaedah", "Zulhijah",
    ];

    public static IReadOnlyList<string> Malay { get; } =
    [
        "Muharam", "Safar", "Rabiulawal", "Rabiulakhir", "Jamadilawal", "Jamadilakhir",
        "Rejab", "Syaaban", "Ramadan", "Syawal", "Zulkaedah", "Zulhijah",
    ];

    /// <summary>The name of month 1 to 12 for "en" or "ms". Anything else gets English.</summary>
    public static string Name(int month, string language)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        var table = string.Equals(language, "ms", StringComparison.OrdinalIgnoreCase) ? Malay : English;
        return table[month - 1];
    }
}
