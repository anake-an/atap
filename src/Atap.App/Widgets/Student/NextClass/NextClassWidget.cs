using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Productivity.Student;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Student.NextClass;

[assembly: AtapWidget(typeof(NextClassDescriptor))]

namespace Atap.Widgets.Student.NextClass;

/// <summary>The next class from the timetable set in Settings. Constants only.</summary>
public sealed class NextClassDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "next-class";

    /// <summary>The timetable file in this widget's user data. The Settings card writes it.</summary>
    public const string DataFile = "timetable.json";

    public string TypeId => TypeIdValue;

    public string NameKey => "nextclass.name";

    public string DescriptionKey => "nextclass.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Student/NextClass/NextClassViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new NextClassWidget(context);
}

/// <summary>
/// Live during a class and from 15 minutes before the next, so the compact notch names it. One wake for the next
/// change (<see cref="ClassOutlook.NextChange"/>), which includes midnight for the day rollover, plus the minute
/// tick only while shown.
/// </summary>
public sealed class NextClassWidget(WidgetContext context) : WidgetBase(context)
{
    private static readonly ScheduleOptions Exact = new(TimeSpan.Zero, false, MissedRun.RunOnce);

    private readonly NextClassViewModel _viewModel = new(context.Text);
    private TimetableDocument _timetable = new();
    private IDisposable? _wake;
    private IDisposable? _tick;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _timetable = Context.Storage.Load(NextClassDescriptor.DataFile, StudentJsonContext.Default.TimetableDocument, new TimetableDocument()).Normalize();
        Context.Visibility.Changed += OnVisibilityChanged;
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _wake?.Dispose();
        _wake = null;
        _tick?.Dispose();
        _tick = null;
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var now = Context.Time.GetUtcNow();
        var zone = Context.Time.LocalTimeZone;
        var outlook = _timetable.OutlookAt(now, zone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var later = _timetable.On(today.DayOfWeek)
            .Select(c => (Class: c, Start: TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(c.Start), zone)))
            .Where(c => c.Start > now.UtcDateTime)
            .Select(c => c.Class)
            .ToList();
        _viewModel.Show(outlook, later, now, zone, _timetable.Classes.Count > 0);
        SetIsLive(outlook.IsLiveAt(now));

        _wake?.Dispose();
        _wake = Context.Scheduler.At(outlook.NextChange(now, zone), Refresh, Exact);

        var shown = Context.Visibility.Value is WidgetVisibility.Compact or WidgetVisibility.Expanded or WidgetVisibility.Panel;
        if (shown && _tick is null)
            _tick = Context.Scheduler.EveryMinute(Refresh);
        else if (!shown && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }
}

/// <summary>A class later today: "10:00 to 12:00", "BIT2033 Data Structures", "BK3".</summary>
public sealed record ClassRow(string Time, string Name, string Room);

public sealed class NextClassViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private ClassOutlook _outlook = ClassOutlook.None;
    private IReadOnlyList<ClassSession> _later = [];
    private DateTimeOffset _now;
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;
    private bool _hasTimetable;

    public NextClassViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    private ClassOccurrence? Shown => _outlook.Current ?? _outlook.Next;

    public bool HasClass => Shown is not null;

    public bool HasMessage => !HasClass;

    public string Message => _text[_hasTimetable ? "nextclass.noneSoon" : "nextclass.noTimetable"];

    public bool IsNow => _outlook.Current is not null;

    /// <summary>"BIT2033", or the name when there is no code. For the compact notch.</summary>
    public string Short => Shown is { } c ? (c.Session.Code.Length > 0 ? c.Session.Code : c.Session.Name) : string.Empty;

    /// <summary>"BIT2033 Data Structures".</summary>
    public string Name => Shown is { } c ? NameOf(c.Session) : string.Empty;

    public string Room => Shown?.Session.Room ?? string.Empty;

    public bool HasRoom => Room.Length > 0;

    /// <summary>"10:00 AM to 12:00 PM", with the day when it is not today.</summary>
    public string Time => Shown is { } c ? TimeText(c) : string.Empty;

    /// <summary>"in 12 min", "now", or "ends in 40 min" during the class.</summary>
    public string Countdown => _outlook.Current is { } current
        ? _text.Format("nextclass.endsIn", ("time", DurationText.Short(current.End - _now, _text.Language)))
        : _outlook.Next is { } next ? DurationText.Until(next.Start - _now, _text.Language)
        : string.Empty;

    /// <summary>For the compact notch: the room, or the countdown when there is no room.</summary>
    public string CompactRight => _outlook.Current is null && _outlook.Next is { } next && next.Start - _now <= ClassOutlook.LiveLead
        ? DurationText.Short(next.Start - _now, _text.Language)
        : HasRoom ? Room : Countdown;

    public IReadOnlyList<ClassRow> Later { get; private set; } = [];

    public bool HasLater => Later.Count > 0;

    public string AccessibleSummary => HasClass
        ? _text.Format(IsNow ? "nextclass.accessibleNow" : "nextclass.accessible", ("name", Name), ("room", Room), ("when", Countdown))
        : _text.Format("nextclass.accessibleMessage", ("message", Message));

    public void Show(ClassOutlook outlook, IReadOnlyList<ClassSession> later, DateTimeOffset now, TimeZoneInfo zone, bool hasTimetable)
    {
        _outlook = outlook;
        _later = later;
        _now = now;
        _zone = zone;
        _hasTimetable = hasTimetable;
        Build();
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void Build()
    {
        Later = _later
            .Where(c => Shown is null || c.Id != Shown.Session.Id)
            .Select(c => new ClassRow(Range(c.Start, c.End), NameOf(c), c.Room))
            .ToList();
        OnPropertyChanged(string.Empty);
    }

    private string TimeText(ClassOccurrence c)
    {
        var range = Range(TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.Start, _zone).DateTime), TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.End, _zone).DateTime));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_now, _zone).DateTime);
        return c.Date == today ? range
            : c.Date == today.AddDays(1) ? _text.Format("nextclass.tomorrowAt", ("time", range))
            : $"{c.Date.ToString("ddd", _text.Culture)}, {range}";
    }

    private string Range(TimeOnly start, TimeOnly end) =>
        _text.Format("nextclass.range", ("start", start.ToString("t", CultureInfo.CurrentCulture)), ("end", end.ToString("t", CultureInfo.CurrentCulture)));

    private static string NameOf(ClassSession session) =>
        session.Code.Length > 0 && session.Name.Length > 0 ? $"{session.Code} {session.Name}"
        : session.Code.Length > 0 ? session.Code
        : session.Name;

    private void OnLanguageChanged(object? sender, EventArgs e) => Build();
}
