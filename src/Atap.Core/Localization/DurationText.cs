namespace Atap.Core.Localization;

/// <summary>
/// Short time spans for the notch, in English or Bahasa Melayu: "in 23 min", "dalam 1 j 12 min",
/// "3 days left", "3 hari lagi". Countdowns round up to the next minute, so "in 1 min" shows until the moment arrives.
/// </summary>
public static class DurationText
{
    /// <summary>"23 min", "1 h 12 min", "2 d 3 h". In Malay "1 j 12 min" and "2 hari 3 j". Under a minute is "1 min".</summary>
    public static string Short(TimeSpan span, string language)
    {
        var ms = language == Localizer.Malay;
        var totalMinutes = (long)Math.Ceiling(Math.Max(0, span.TotalMinutes));
        if (totalMinutes < 1)
            totalMinutes = 1;

        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes / 60 % 24;
        var minutes = totalMinutes % 60;

        if (days > 0)
        {
            var dayText = ms ? $"{days} hari" : $"{days} d";
            return hours > 0 ? $"{dayText} {hours} {(ms ? "j" : "h")}" : dayText;
        }
        if (hours > 0)
        {
            var hourText = $"{hours} {(ms ? "j" : "h")}";
            return minutes > 0 ? $"{hourText} {minutes} min" : hourText;
        }
        return $"{minutes} min";
    }

    /// <summary>"in 23 min" or "dalam 23 min". Zero or less is "now" or "sekarang".</summary>
    public static string Until(TimeSpan span, string language)
    {
        var ms = language == Localizer.Malay;
        if (span <= TimeSpan.Zero)
            return ms ? "sekarang" : "now";
        return (ms ? "dalam " : "in ") + Short(span, language);
    }

    /// <summary>"3 days left" or "3 hari lagi". One day is "1 day left". Zero is "today" or "hari ini". Past dates are "overdue" or "lewat".</summary>
    public static string DaysLeft(int days, string language)
    {
        var ms = language == Localizer.Malay;
        return days switch
        {
            < 0 => ms ? "lewat" : "overdue",
            0 => ms ? "hari ini" : "today",
            1 => ms ? "1 hari lagi" : "1 day left",
            _ => ms ? $"{days} hari lagi" : $"{days} days left",
        };
    }
}
