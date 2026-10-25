using System.Globalization;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;

namespace Atap.Widgets.Timers;

/// <summary>Text for running times: "4:05" and "1:04:05" with seconds, "25 min" for the compact notch.</summary>
public static class TimerText
{
    /// <summary>"24:59", or "1:04:05" from an hour on. Rounds down, so the last second reads 0:00 only at the end.</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        var total = (long)span.TotalSeconds;
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var seconds = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:D2}:{seconds:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:D2}");
    }

    /// <summary>A countdown for the compact notch, which changes once a minute: rounds up, as "in 1 min" does.</summary>
    public static string Remaining(TimeSpan span, string language) => Core.Localization.DurationText.Short(span, language);

    /// <summary>Time counted up for the compact notch: rounds down, so "0 min" for the first minute.</summary>
    public static string Elapsed(TimeSpan span)
    {
        var minutes = (long)Math.Max(0, span.TotalMinutes);
        return minutes >= 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}:{minutes % 60:D2} h")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes} min");
    }
}

/// <summary>
/// The display tick of a running timer: every second while it is open (Expanded or Panel), every minute while it
/// is only in the compact notch, and none while hidden or stopped. Ends and alarms use their own exact wake.
/// </summary>
public sealed class TimerTicks(IScheduler scheduler, IObservableValue<WidgetVisibility> visibility, Action refresh) : IDisposable
{
    private IDisposable? _tick;
    private bool _perSecond;

    public void Update(bool running)
    {
        var view = visibility.Value;
        var perSecond = view is WidgetVisibility.Expanded or WidgetVisibility.Panel;
        var wanted = running && view != WidgetVisibility.Hidden && view != WidgetVisibility.Idle;
        if (!wanted)
        {
            Dispose();
            return;
        }
        if (_tick is not null && _perSecond == perSecond)
            return;
        _tick?.Dispose();
        _perSecond = perSecond;
        _tick = perSecond ? scheduler.EverySecond(refresh) : scheduler.EveryMinute(refresh);
    }

    public void Dispose()
    {
        _tick?.Dispose();
        _tick = null;
    }
}
