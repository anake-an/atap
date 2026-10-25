using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Productivity;
using Atap.Core.Productivity.Lists;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Lists.Todo;

[assembly: AtapWidget(typeof(TodoDescriptor))]

namespace Atap.Widgets.Lists.Todo;

/// <summary>A to-do list with due dates and reminders. Constants only.</summary>
public sealed class TodoDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "todo";

    public string TypeId => TypeIdValue;

    public string NameKey => "todo.name";

    public string DescriptionKey => "todo.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    // Reminders come even when the card is not on any page.
    public bool WorksWithoutPlacement => true;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Lists/Todo/TodoViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new TodoWidget(context);
}

/// <summary>
/// Keeps the list in user data. Only the next reminder has a wake. Reminders that came due while Atap was closed
/// show once: recent ones each, older ones as one summary.
/// </summary>
public sealed class TodoWidget : WidgetBase
{
    public const string DataFile = "todo.json";

    // Due times come from local times, so a time zone change works them out again.
    private static readonly ScheduleOptions Exact = new(TimeSpan.Zero, false, MissedRun.RunOnce, WallClock: true);

    private readonly TodoViewModel _viewModel;
    private TodoDocument _document = new();
    private IDisposable? _wake;

    public TodoWidget(WidgetContext context)
        : base(context)
    {
        _viewModel = new TodoViewModel(context.Text, new TodoActions(Add, Toggle, SetDue, Delete, ClearDone));
    }

    public override object ViewModel => _viewModel;

    private DateTimeOffset Now => Context.Time.GetUtcNow();

    private TimeZoneInfo Zone => Context.Time.LocalTimeZone;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _document = Context.Storage.Load(DataFile, ListsJsonContext.Default.TodoDocument, new TodoDocument()).Normalize();
        Context.Visibility.Changed += OnVisibilityChanged;
        Check();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _wake?.Dispose();
        _wake = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void Add(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || _document.IsFull)
            return;
        Commit(_document.Upsert(TodoItem.Create(ItemIds.New(), title, Now)));
    }

    private void Toggle(string id)
    {
        if (_document.Find(id) is { } item)
            Commit(_document.Upsert(item.Done ? item.Reopen(Now) : item.Complete(Now)));
    }

    /// <summary>Due today or tomorrow, all day, so the reminder comes in the morning. Null clears it.</summary>
    private void SetDue(string id, int? daysFromToday)
    {
        if (_document.Find(id) is not { } item)
            return;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, Zone).DateTime);
        var date = daysFromToday is { } days ? today.AddDays(days) : (DateOnly?)null;
        // No lead: the reminder comes on the day itself, at 9:00.
        Commit(_document.Upsert(item.WithDue(date, null, date is null ? null : TimeSpan.Zero, Now)));
    }

    private void Delete(string id) => Commit(_document.Remove(id));

    private void ClearDone() => Commit(_document.RemoveDone());

    private void Commit(TodoDocument document)
    {
        _document = document;
        Save();
        Check();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Show();

    private void Check()
    {
        var now = Now;
        var due = _document.DueReminders(now, Zone, TodoDocument.DefaultAllDayReminder);
        foreach (var reminder in due.Recent)
            Context.Alerts.Show(_viewModel.ReminderFor(reminder));
        if (due.Older.Count > 0)
            Context.Alerts.Show(_viewModel.MissedSummary(due.Older.Count));
        if (due.Recent.Count > 0 || due.Older.Count > 0)
        {
            _document = _document.MarkReminded(due.Recent.Concat(due.Older));
            Save();
        }

        _wake?.Dispose();
        _wake = _document.NextReminder(now, Zone, TodoDocument.DefaultAllDayReminder) is { } next
            ? Context.Scheduler.At(next.At, OnWake, Exact)
            : null;
        Show();
    }

    private void OnWake()
    {
        _wake = null;
        Check();
    }

    private void Show() => _viewModel.Show(_document, Now, Zone);

    private void Save() => Context.Storage.Save(DataFile, _document, ListsJsonContext.Default.TodoDocument);
}

/// <summary>What the card can ask the widget to do.</summary>
public sealed record TodoActions(
    Action<string> Add,
    Action<string> Toggle,
    Action<string, int?> SetDue,
    Action<string> Delete,
    Action ClearDone);

/// <summary>One task: its title, when it is due, and its buttons.</summary>
public sealed record TodoRow(string Id, string Title, bool Done, string Due, bool Overdue, IRelayCommand Toggle,
    IRelayCommand DueToday, IRelayCommand DueTomorrow, IRelayCommand ClearDue, IRelayCommand Delete)
{
    public bool HasDue => Due.Length > 0;
}

public sealed partial class TodoViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    /// <summary>Tasks in the small card and the open notch.</summary>
    public const int Shown = 4;

    private readonly ILocalizer _text;
    private readonly TodoActions _actions;
    private TodoDocument _document = new();
    private DateTimeOffset _now;
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;
    private string _newTitle = string.Empty;

    public TodoViewModel(ILocalizer text, TodoActions actions)
    {
        _text = text;
        _actions = actions;
        _text.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>What is typed in the Add box.</summary>
    public string NewTitle
    {
        get => _newTitle;
        set => SetProperty(ref _newTitle, value ?? string.Empty);
    }

    public IReadOnlyList<TodoRow> Rows { get; private set; } = [];

    /// <summary>The first open tasks, for the open notch.</summary>
    public IReadOnlyList<TodoRow> Top { get; private set; } = [];

    public bool HasNone => Rows.Count == 0;

    public bool HasDone => Rows.Any(r => r.Done);

    /// <summary>"3 to do, 1 overdue".</summary>
    public string CountText
    {
        get
        {
            var open = _document.OpenCount();
            var overdue = _document.OverdueCount(_now, _zone);
            return open == 0 ? _text["todo.allDone"]
                : overdue > 0 ? _text.Format("todo.countOverdue", ("open", N(open)), ("overdue", N(overdue)))
                : _text.Format("todo.count", ("open", N(open)));
        }
    }

    public string AccessibleSummary => _text.Format("todo.accessible", ("count", CountText));

    public void Show(TodoDocument document, DateTimeOffset now, TimeZoneInfo zone)
    {
        _document = document;
        _now = now;
        _zone = zone;
        Rows = document.Ordered(zone).Select(RowFor).ToList();
        Top = Rows.Where(r => !r.Done).Take(Shown).ToList();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>"Due: Pay the bill".</summary>
    public Alert ReminderFor(TodoReminder reminder) =>
        new("todo." + reminder.Item.Id, AlertPriority.High, reminder.Item.Title)
        {
            Subtitle = _text["todo.alert.due"],
            IconKey = "bell",
        };

    /// <summary>One alert for reminders that came due long ago, while Atap was closed.</summary>
    public Alert MissedSummary(int count) =>
        new("todo.missed", AlertPriority.Normal, _text.Format("todo.alert.missed", ("count", N(count))))
        {
            IconKey = "bell",
            Tone = AlertTone.Warning,
        };

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    [RelayCommand]
    private void Add()
    {
        var title = NewTitle.Trim();
        if (title.Length == 0)
            return;
        NewTitle = string.Empty;
        _actions.Add(title);
    }

    [RelayCommand]
    private void ClearDone() => _actions.ClearDone();

    private TodoRow RowFor(TodoItem item) => new(
        item.Id,
        item.Title,
        item.Done,
        DueText(item),
        item.IsOverdueAt(_now, _zone),
        new RelayCommand(() => _actions.Toggle(item.Id)),
        new RelayCommand(() => _actions.SetDue(item.Id, 0)),
        new RelayCommand(() => _actions.SetDue(item.Id, 1)),
        new RelayCommand(() => _actions.SetDue(item.Id, null)),
        new RelayCommand(() => _actions.Delete(item.Id)));

    /// <summary>"Today", "Tomorrow", "Overdue" or "Fri, 30 Oct".</summary>
    private string DueText(TodoItem item)
    {
        if (item.DueDate is not { } date || item.Done)
            return string.Empty;
        if (item.IsOverdueAt(_now, _zone))
            return _text["todo.overdue"];
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_now, _zone).DateTime);
        var day = date == today ? _text["todo.today"]
            : date == today.AddDays(1) ? _text["todo.tomorrow"]
            : date.ToString("ddd, d MMM", _text.Culture);
        return item.DueTime is { } time ? $"{day} {time.ToString("t", CultureInfo.CurrentCulture)}" : day;
    }

    private static string N(int value) => value.ToString(CultureInfo.CurrentCulture);

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_document, _now, _zone);
}
