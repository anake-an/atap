using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Productivity;
using Atap.Core.Productivity.Student;
using Atap.Core.Storage;
using Atap.Settings.Cards.Student;
using Atap.Widgets.Student.Assignments;
using Atap.Widgets.Student.NextClass;

[assembly: AtapSettingsCard(typeof(StudentCard))]

namespace Atap.Settings.Cards.Student;

/// <summary>The timetable for Next class and the list for Assignments. Typing happens here, not in the notch.</summary>
internal sealed class StudentCard : ISettingsCard
{
    public string Id => "student";

    public string SectionId => SettingsSections.Widgets;

    public int Order => 50;

    public string TitleKey => "studentcard.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Student/StudentViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) => new StudentCardViewModel(
        new WidgetStorage(NextClassDescriptor.TypeIdValue, context.Services.Paths),
        new WidgetStorage(AssignmentsDescriptor.TypeIdValue, context.Services.Paths),
        context.Services.Layout,
        context.Services.Text,
        context.Services.Time);
}

public sealed record ClassListRow(string Id, string Day, string Time, string Name, string Room);

public sealed record AssignmentListRow(string Id, string Title, string Course, string Due);

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Saves straight into each widget's user data, then bumps a "revision" value in its layout settings. A settings
/// change restarts that widget, so it loads the new list at once.
/// </summary>
internal sealed partial class StudentCardViewModel : ObservableObject, IDisposable
{
    public const string RevisionKey = "revision";

    private static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private readonly IWidgetStorage _timetableStorage;
    private readonly IWidgetStorage _assignmentStorage;
    private readonly ILayoutService _layout;
    private readonly ILocalizer _text;
    private readonly TimeProvider _time;
    private TimetableDocument _timetable;
    private AssignmentDocument _assignments;

    public StudentCardViewModel(IWidgetStorage timetable, IWidgetStorage assignments, ILayoutService layout, ILocalizer text, TimeProvider time)
    {
        _timetableStorage = timetable;
        _assignmentStorage = assignments;
        _layout = layout;
        _text = text;
        _time = time;
        _timetable = timetable.Load(NextClassDescriptor.DataFile, StudentJsonContext.Default.TimetableDocument, new TimetableDocument()).Normalize();
        _assignments = assignments.Load(AssignmentsDescriptor.DataFile, StudentJsonContext.Default.AssignmentDocument, new AssignmentDocument()).Normalize();
        Build();
        _text.LanguageChanged += OnLanguageChanged;
    }

    // Timetable entry.
    public IReadOnlyList<Option<DayOfWeek>> Days { get; private set; } = [];

    public IReadOnlyList<Option<TimeOnly>> Times { get; private set; } = [];

    [ObservableProperty]
    private Option<DayOfWeek>? _day;

    [ObservableProperty]
    private Option<TimeOnly>? _start;

    [ObservableProperty]
    private Option<TimeOnly>? _end;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _className = string.Empty;

    [ObservableProperty]
    private string _room = string.Empty;

    public IReadOnlyList<ClassListRow> Classes { get; private set; } = [];

    // Assignment entry.
    public IReadOnlyList<Option<DateOnly>> Dates { get; private set; } = [];

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _course = string.Empty;

    [ObservableProperty]
    private Option<DateOnly>? _due;

    public IReadOnlyList<AssignmentListRow> Assignments { get; private set; } = [];

    [ObservableProperty]
    private string? _message;

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    [RelayCommand]
    private void AddClass()
    {
        if (Day is null || Start is null || End is null || End.Value <= Start.Value || (Code.Trim().Length == 0 && ClassName.Trim().Length == 0))
        {
            Message = _text["studentcard.classInvalid"];
            return;
        }
        var session = new ClassSession
        {
            Id = ItemIds.New(),
            Code = Code.Trim(),
            Name = ClassName.Trim(),
            Room = Room.Trim(),
            Day = Day.Value,
            Start = Start.Value,
            End = End.Value,
        };
        _timetable = _timetable.Upsert(session).Normalize();
        SaveTimetable();
        Code = ClassName = Room = string.Empty;
        Message = null;
        Build();
    }

    [RelayCommand]
    private void RemoveClass(string id)
    {
        _timetable = _timetable.Remove(id);
        SaveTimetable();
        Build();
    }

    [RelayCommand]
    private void AddAssignment()
    {
        if (Due is null || Title.Trim().Length == 0)
        {
            Message = _text["studentcard.assignmentInvalid"];
            return;
        }
        _assignments = _assignments.Upsert(Assignment.Create(ItemIds.New(), Title, Course, Due.Value, null, _time.GetUtcNow())).Normalize();
        SaveAssignments();
        Title = Course = string.Empty;
        Message = null;
        Build();
    }

    [RelayCommand]
    private void RemoveAssignment(string id)
    {
        _assignments = _assignments.Remove(id);
        SaveAssignments();
        Build();
    }

    private void SaveTimetable()
    {
        _timetableStorage.Save(NextClassDescriptor.DataFile, _timetable, StudentJsonContext.Default.TimetableDocument);
        Bump(NextClassDescriptor.TypeIdValue);
    }

    private void SaveAssignments()
    {
        _assignmentStorage.Save(AssignmentsDescriptor.DataFile, _assignments, StudentJsonContext.Default.AssignmentDocument);
        Bump(AssignmentsDescriptor.TypeIdValue);
    }

    private void Bump(string instanceId) =>
        _layout.Update(d => d.SetWidgetSetting(instanceId, RevisionKey, _time.GetUtcNow().ToUnixTimeMilliseconds()));

    private void Build()
    {
        var names = _text.Culture.DateTimeFormat;
        Days = WeekOrder.Select(d => new Option<DayOfWeek>(d, names.GetDayName(d))).ToList();
        Times = Enumerable.Range(14, 31).Select(i => new TimeOnly(i / 2, i % 2 * 30))
            .Select(t => new Option<TimeOnly>(t, t.ToString("t", CultureInfo.CurrentCulture))).ToList();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime);
        Dates = Enumerable.Range(0, 120).Select(i => today.AddDays(i))
            .Select(d => new Option<DateOnly>(d, d.ToString("ddd, d MMM yyyy", _text.Culture))).ToList();
        Day ??= Days[0];
        Start ??= Times.First(t => t.Value == new TimeOnly(8, 0));
        End ??= Times.First(t => t.Value == new TimeOnly(10, 0));
        Due ??= Dates[7];

        Classes = WeekOrder.SelectMany(_timetable.On).Select(c => new ClassListRow(
            c.Id,
            names.GetAbbreviatedDayName(c.Day),
            $"{c.Start.ToString("t", CultureInfo.CurrentCulture)} - {c.End.ToString("t", CultureInfo.CurrentCulture)}",
            string.Join(" ", new[] { c.Code, c.Name }.Where(s => s.Length > 0)),
            c.Room)).ToList();
        Assignments = _assignments.Assignments.Where(a => a is not null && !a.Done).OrderBy(a => a.DueDate)
            .Select(a => new AssignmentListRow(a.Id, a.Title, a.Course, a.DueDate?.ToString("ddd, d MMM", _text.Culture) ?? string.Empty)).ToList();
        OnPropertyChanged(string.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Day = null;
        Start = null;
        End = null;
        Due = null;
        Build();
    }
}
