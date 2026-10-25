using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Productivity.Student;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Student.Assignments;

[assembly: AtapWidget(typeof(AssignmentsDescriptor))]

namespace Atap.Widgets.Student.Assignments;

/// <summary>Assignment deadlines from the list set in Settings. Constants only.</summary>
public sealed class AssignmentsDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "assignments";

    /// <summary>The assignment file in this widget's user data. The Settings card writes it.</summary>
    public const string DataFile = "assignments.json";

    public string TypeId => TypeIdValue;

    public string NameKey => "assignments.name";

    public string DescriptionKey => "assignments.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Student/Assignments/AssignmentsViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new AssignmentsWidget(context);
}

/// <summary>One wake at the next change: a deadline passing, or midnight when every day count drops by one.</summary>
public sealed class AssignmentsWidget : WidgetBase
{
    private static readonly ScheduleOptions AtChange = new(TimeSpan.FromMinutes(1), false, MissedRun.RunOnce);

    private readonly AssignmentsViewModel _viewModel;
    private AssignmentDocument _document = new();
    private IDisposable? _wake;

    public AssignmentsWidget(WidgetContext context)
        : base(context)
    {
        _viewModel = new AssignmentsViewModel(context.Text, Toggle);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _document = Context.Storage.Load(AssignmentsDescriptor.DataFile, StudentJsonContext.Default.AssignmentDocument, new AssignmentDocument()).Normalize();
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        _wake?.Dispose();
        _wake = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void Toggle(string id)
    {
        if (_document.Find(id) is not { } assignment)
            return;
        var now = Context.Time.GetUtcNow();
        _document = _document.Upsert(assignment.Done ? assignment.Reopen() : assignment.Complete(now));
        Context.Storage.Save(AssignmentsDescriptor.DataFile, _document, StudentJsonContext.Default.AssignmentDocument);
        Refresh();
    }

    private void Refresh()
    {
        var now = Context.Time.GetUtcNow();
        var zone = Context.Time.LocalTimeZone;
        _viewModel.Show(_document.Upcoming(now, zone));
        _wake?.Dispose();
        _wake = Context.Scheduler.At(_document.NextChange(now, zone), Refresh, AtChange);
    }
}

/// <summary>"Laporan FYP Bab 3", "BIT2033", "3 days left", and its tick.</summary>
public sealed record AssignmentRow(string Title, string Course, string Countdown, bool Urgent, IRelayCommand Done)
{
    public bool HasCourse => Course.Length > 0;
}

public sealed class AssignmentsViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    public const int Shown = 4;

    private readonly ILocalizer _text;
    private readonly Action<string> _toggle;
    private IReadOnlyList<AssignmentCountdown> _upcoming = [];

    public AssignmentsViewModel(ILocalizer text, Action<string> toggle)
    {
        _text = text;
        _toggle = toggle;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<AssignmentRow> Rows { get; private set; } = [];

    public AssignmentRow? First => Rows.Count > 0 ? Rows[0] : null;

    public bool HasRows => Rows.Count > 0;

    public bool HasNone => Rows.Count == 0;

    public string AccessibleSummary => First is { } first
        ? _text.Format("assignments.accessible", ("count", Rows.Count.ToString(CultureInfo.CurrentCulture)), ("first", first.Title), ("when", first.Countdown))
        : _text["assignments.accessibleNone"];

    public void Show(IReadOnlyList<AssignmentCountdown> upcoming)
    {
        _upcoming = upcoming;
        Build();
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void Build()
    {
        Rows = _upcoming.Take(Shown).Select(a => new AssignmentRow(
            a.Assignment.Title,
            a.Assignment.Course,
            CountdownText(a.Countdown),
            a.Countdown is { State: DueState.Overdue or DueState.Today or DueState.Tomorrow },
            new RelayCommand(() => _toggle(a.Assignment.Id)))).ToList();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>"Overdue", "Due today 11:59 PM", "Due tomorrow", or "3 days left" and "3 hari lagi".</summary>
    private string CountdownText(DueCountdown? countdown) => countdown switch
    {
        null => _text["assignments.noDate"],
        { State: DueState.Overdue } => _text["assignments.overdue"],
        { State: DueState.Today } c => _text.Format("assignments.today", ("left", DurationText.Short(c.Remaining, _text.Language))),
        { State: DueState.Tomorrow } => _text["assignments.tomorrow"],
        { Days: var days } => DurationText.DaysLeft(days, _text.Language),
    };

    private void OnLanguageChanged(object? sender, EventArgs e) => Build();
}
