using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Malaysia;
using Atap.Notch.Automation;

namespace Atap.Widgets.WaktuSolat;

/// <summary>Why there are no times to show.</summary>
public enum SolatStatus
{
    Ready,

    /// <summary>No zone picked yet.</summary>
    NeedsArea,

    Loading,

    /// <summary>Nothing for today: no countdown and no reminders.</summary>
    Stale,
}

/// <summary>One time in the list: "Asar", "4:19 PM".</summary>
public sealed record PrayerRow(string Name, string Time, bool IsNext, bool IsCurrent);

public sealed class WaktuSolatViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private SolatNow _state = new(null, null, null, false, false, null);
    private DateTimeOffset _now;
    private PrayerZone? _zone;
    private SolatStatus _status = SolatStatus.NeedsArea;

    public WaktuSolatViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public bool HasTimes => _status == SolatStatus.Ready && _state.Next is not null;

    public bool HasMessage => !HasTimes;

    /// <summary>"Pick your area in Settings", "Getting prayer times" or "Prayer times are out of date".</summary>
    public string Message => _text[_status switch
    {
        SolatStatus.NeedsArea => "solat.needsArea",
        SolatStatus.Loading => "solat.loading",
        _ => "solat.stale",
    }];

    /// <summary>A prayer began in the last five minutes.</summary>
    public bool IsStarted => _state.Current is { } current && _now - current.Time < SolatClock.JustStarted;

    /// <summary>"Asar", or "Asar now" in its first five minutes.</summary>
    public string Headline => IsStarted && _state.Current is { } current
        ? _text.Format("solat.started", ("prayer", NameOf(current.Prayer)))
        : _state.Next is { } next ? NameOf(next.Prayer) : string.Empty;

    /// <summary>The prayer the compact notch names: the one just begun, else the next.</summary>
    public string ShortName => IsStarted && _state.Current is { } current ? NameOf(current.Prayer)
        : _state.Next is { } next ? NameOf(next.Prayer) : string.Empty;

    public string NextTime => _state.Next is { } next ? TimeText(next.Time) : string.Empty;

    /// <summary>"in 23 min" or "dalam 23 min".</summary>
    public string Countdown => _state.Next is { } next ? DurationText.Until(next.Time - _now, _text.Language) : string.Empty;

    /// <summary>"now" while a prayer has just begun, else "23 min". For the compact notch.</summary>
    public string Short => IsStarted ? _text["solat.now"]
        : _state.Next is { } next ? DurationText.Short(next.Time - _now, _text.Language) : string.Empty;

    /// <summary>The line under the headline: "Asar 4:19 PM, in 23 min" or the time of the next one.</summary>
    public string Detail => IsStarted && _state.Next is { } next
        ? _text.Format("solat.then", ("prayer", NameOf(next.Prayer)), ("time", TimeText(next.Time)))
        : _text.Format("solat.at", ("time", NextTime), ("until", Countdown));

    public IReadOnlyList<PrayerRow> Rows { get; private set; } = [];

    /// <summary>"Gombak, Petaling, Sepang".</summary>
    public string Areas => _zone?.AreasText ?? string.Empty;

    public bool HasFast => _state.Fast is not null && HasTimes;

    /// <summary>"Imsak in 5 h 2 min" or "Berbuka in 2 h". Only during Ramadan.</summary>
    public string FastText => _state.Fast is { } fast
        ? _text.Format(fast.Prayer == Prayer.Imsak ? "solat.imsakIn" : "solat.berbukaIn", ("until", DurationText.Short(fast.Time - _now, _text.Language)))
        : string.Empty;

    /// <summary>For example "Waktu solat. Asar 4:19 PM, in 23 min".</summary>
    public string AccessibleSummary => HasTimes
        ? _text.Format("solat.accessible", ("prayer", ShortName), ("detail", Detail))
        : _text.Format("solat.accessibleMessage", ("message", Message));

    public void Show(SolatNow state, DateTimeOffset now, PrayerZone? zone, SolatStatus status)
    {
        _state = state;
        _now = now;
        _zone = zone;
        _status = status;
        Rows = BuildRows();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The alert at a prayer's time: "Time for Asar", or "Time to break the fast" at Maghrib in Ramadan.</summary>
    public Alert ReminderFor(PrayerTime prayer, bool isRamadan, PrayerZone? zone)
    {
        var title = prayer.Prayer switch
        {
            Prayer.Imsak => _text["solat.alert.imsak"],
            Prayer.Maghrib when isRamadan => _text["solat.alert.berbuka"],
            _ => _text.Format("solat.alert.prayer", ("prayer", NameOf(prayer.Prayer))),
        };
        var subtitle = zone is null ? TimeText(prayer.Time) : TimeText(prayer.Time) + " · " + zone.AreasText;
        return new Alert("solat", AlertPriority.High, title) { Subtitle = subtitle, IconKey = "moon" };
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private IReadOnlyList<PrayerRow> BuildRows()
    {
        if (_state.Today is not { } today)
            return [];
        var next = _state.Next;
        var current = IsStarted ? _state.Current : null;
        return Prayers.Shown
            .Select(p => new PrayerRow(
                NameOf(p),
                TimeText(today.TimeOf(p)),
                next is { } n && n.Prayer == p && n.Date == today.Date,
                current is { } c && c.Prayer == p && c.Date == today.Date))
            .ToList();
    }

    private string NameOf(Prayer prayer) => _text["solat.prayer." + prayer.ToString().ToLowerInvariant()];

    /// <summary>In Malaysian time, in the PC's own short time format, such as "4:19 PM" or "16:19".</summary>
    private static string TimeText(DateTimeOffset time) =>
        time.ToOffset(MalaysiaTime.Offset).ToString("t", CultureInfo.CurrentCulture);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Rows = BuildRows();
        OnPropertyChanged(string.Empty);
    }
}
