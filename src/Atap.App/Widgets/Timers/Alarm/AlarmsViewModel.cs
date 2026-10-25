using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Productivity.Timers;
using Atap.Notch.Automation;
using AlarmItem = Atap.Core.Productivity.Timers.Alarm;

namespace Atap.Widgets.Timers.Alarm;

/// <summary>One alarm in the list: "7:00 AM", "Mon Tue Wed Thu Fri", and its switch.</summary>
public sealed partial class AlarmRow(string id, string time, string detail, bool isOn, Action<string, bool> setEnabled, Action<string> edit)
    : ObservableObject
{
    private bool _isOn = isOn;

    public string Id => id;

    public string Time => time;

    public string Detail => detail;

    /// <summary>The switch. Turning it on arms the alarm from now.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetProperty(ref _isOn, value))
                setEnabled(id, value);
        }
    }

    public string AccessibleName => $"{time}, {detail}";

    [RelayCommand]
    private void Edit() => edit(id);
}

/// <summary>A day in the editor. Shown as a round chip with the short day name.</summary>
public sealed class DayChip(DayOfWeek day, string label, string name, bool isOn) : ObservableObject
{
    private bool _isOn = isOn;

    public DayOfWeek Day => day;

    public string Label => label;

    /// <summary>The full day name, for screen readers.</summary>
    public string Name => name;

    public bool IsOn
    {
        get => _isOn;
        set => SetProperty(ref _isOn, value);
    }
}

/// <summary>
/// The alarm list and its editor. The notch cannot take typed text, so the time is set with steppers and the
/// days with chips. Malaysia's weekend differs by state, so there is no fixed "weekdays" button.
/// </summary>
public sealed partial class AlarmsViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private readonly ILocalizer _text;
    private readonly AlarmActions _actions;
    private AlarmDocument _document = new();
    private AlarmDue? _next;
    private DateTimeOffset _now;
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;
    private bool _canSnooze;
    private string? _editingId;
    private TimeOnly _editTime = new(7, 0);
    private bool _isEditing;

    public AlarmsViewModel(ILocalizer text, AlarmActions actions)
    {
        _text = text;
        _actions = actions;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<AlarmRow> Rows { get; private set; } = [];

    public bool HasAlarms => Rows.Count > 0;

    public bool HasNoAlarms => Rows.Count == 0;

    public bool CanAdd => !_document.IsFull;

    /// <summary>"7:00 AM", the next alarm, for the small card.</summary>
    public string NextTime => _next is { } next ? TimeOf(next.Due) : string.Empty;

    /// <summary>"in 9 h 12 min", or "No alarms on".</summary>
    public string NextText => _next is { } next
        ? DurationText.Until(next.Due - _now, _text.Language)
        : _text["alarm.noneOn"];

    public bool CanSnooze => _canSnooze;

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
                OnPropertyChanged(nameof(IsListing));
        }
    }

    public bool IsListing => !IsEditing;

    public bool CanDelete => _editingId is not null;

    /// <summary>"7:05 AM" while editing.</summary>
    public string EditTimeText => _editTime.ToString("t", CultureInfo.CurrentCulture);

    public IReadOnlyList<DayChip> Days { get; private set; } = [];

    public string AccessibleSummary => _next is { } next
        ? _text.Format("alarm.accessible", ("time", TimeOf(next.Due)), ("until", NextText))
        : _text["alarm.accessibleNone"];

    public void Show(AlarmDocument document, AlarmDue? next, DateTimeOffset now, TimeZoneInfo zone, bool canSnooze)
    {
        _document = document;
        _next = next;
        _now = now;
        _zone = zone;
        _canSnooze = canSnooze;
        Rows = document.Alarms
            .Where(a => a is not null)
            .OrderBy(a => a.Time)
            .Select(a => new AlarmRow(a.Id, a.Time.ToString("t", CultureInfo.CurrentCulture), DetailOf(a), a.Enabled, _actions.SetEnabled, BeginEdit))
            .ToList();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasAlarms));
        OnPropertyChanged(nameof(HasNoAlarms));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(NextTime));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(CanSnooze));
        OnPropertyChanged(nameof(AccessibleSummary));
        AddCommand.NotifyCanExecuteChanged();
    }

    public Alert RingAlert(AlarmItem alarm, DateTimeOffset occurrence) =>
        new(AlarmWidget.AlertKey, AlertPriority.Critical, alarm.Label.Length > 0 ? alarm.Label : _text["alarm.ringing"])
        {
            Subtitle = TimeOf(occurrence),
            IconKey = "alarm",
            SoundKey = AlertSounds.Default,
        };

    public Alert MissedAlert(AlarmItem alarm, DateTimeOffset occurrence) =>
        new(AlarmWidget.AlertKey + ".missed." + alarm.Id, AlertPriority.Normal, _text.Format("alarm.missed", ("time", TimeOf(occurrence))))
        {
            Subtitle = alarm.Label.Length > 0 ? alarm.Label : null,
            IconKey = "alarm",
            Tone = AlertTone.Warning,
        };

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        _editingId = null;
        _editTime = new TimeOnly(7, 0);
        Days = BuildDays(AlarmDays.None);
        OpenEditor();
    }

    [RelayCommand]
    private void Snooze() => _actions.Snooze();

    [RelayCommand]
    private void HourUp() => SetEditTime(_editTime.AddHours(1));

    [RelayCommand]
    private void HourDown() => SetEditTime(_editTime.AddHours(-1));

    [RelayCommand]
    private void MinuteUp() => SetEditTime(new TimeOnly(_editTime.Hour, ((_editTime.Minute / 5) + 1) * 5 % 60));

    // 7:03 goes down to 7:00, then 6:55 stays in the same hour as 7:55.
    [RelayCommand]
    private void MinuteDown() => SetEditTime(new TimeOnly(
        _editTime.Hour,
        _editTime.Minute % 5 != 0 ? _editTime.Minute / 5 * 5 : (_editTime.Minute + 55) % 60));

    [RelayCommand]
    private void SaveEdit()
    {
        var days = Days.Where(d => d.IsOn).Aggregate(AlarmDays.None, (all, d) => all | d.Day.ToAlarmDay());
        IsEditing = false;
        _actions.Save(_editingId, _editTime, days);
    }

    [RelayCommand]
    private void DeleteEdit()
    {
        IsEditing = false;
        if (_editingId is { } id)
            _actions.Delete(id);
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    private void BeginEdit(string id)
    {
        if (_document.Find(id) is not { } alarm)
            return;
        _editingId = id;
        _editTime = alarm.Time;
        Days = BuildDays(alarm.Days);
        OpenEditor();
    }

    private void OpenEditor()
    {
        OnPropertyChanged(nameof(Days));
        OnPropertyChanged(nameof(EditTimeText));
        OnPropertyChanged(nameof(CanDelete));
        IsEditing = true;
    }

    /// <summary>Minutes move in fives and wrap within the hour, as on a phone. Hours wrap around the day.</summary>
    private void SetEditTime(TimeOnly time)
    {
        _editTime = time;
        OnPropertyChanged(nameof(EditTimeText));
    }

    private IReadOnlyList<DayChip> BuildDays(AlarmDays days)
    {
        var names = _text.Culture.DateTimeFormat;
        return WeekOrder
            .Select(d => new DayChip(d, names.GetShortestDayName(d), names.GetDayName(d), days.Includes(d)))
            .ToList();
    }

    /// <summary>"Every day", "Once", or "Mon Tue Wed", in week order from Monday.</summary>
    private string DetailOf(AlarmItem alarm)
    {
        var days = (alarm.Days & AlarmDays.Everyday) switch
        {
            AlarmDays.Everyday => _text["alarm.everyDay"],
            AlarmDays.None => _text["alarm.once"],
            var some => string.Join(" ", WeekOrder.Where(d => some.Includes(d)).Select(d => _text.Culture.DateTimeFormat.GetAbbreviatedDayName(d))),
        };
        return alarm.Label.Length > 0 ? $"{alarm.Label} · {days}" : days;
    }

    private string TimeOf(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _zone).ToString("t", CultureInfo.CurrentCulture);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Show(_document, _next, _now, _zone, _canSnooze);
        if (IsEditing)
        {
            var on = Days.Where(d => d.IsOn).Aggregate(AlarmDays.None, (all, d) => all | d.Day.ToAlarmDay());
            Days = BuildDays(on);
            OnPropertyChanged(nameof(Days));
        }
    }
}
