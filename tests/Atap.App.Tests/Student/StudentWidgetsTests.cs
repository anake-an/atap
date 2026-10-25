using Atap.Composition;
using Atap.Core.Localization;
using Atap.Core.Productivity.Student;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Settings.Cards.Student;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Student.Assignments;
using Atap.Widgets.Student.NextClass;

namespace Atap.Tests.Student;

/// <summary>Next class and assignments on fake time: Sunday 25 October 2026 at 09:00, UTC as the local zone.</summary>
public sealed class StudentWidgetsTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly List<OwnerLifetime> _lifetimes = [];
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);

    public StudentWidgetsTests()
    {
        _services.Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _services.Layout.Load();
    }

    public void Dispose()
    {
        foreach (var lifetime in _lifetimes)
            lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task NextClass_GoesLive15MinutesBefore_AndStaysLiveDuringTheClass()
    {
        SaveTimetable(new ClassSession { Id = "a", Code = "BIT2033", Name = "Data Structures", Room = "BK3", Day = DayOfWeek.Sunday, Start = new TimeOnly(10, 0), End = new TimeOnly(11, 0) });
        var (widget, view) = await StartAsync<NextClassViewModel>(new NextClassDescriptor());

        Assert.False(widget.IsLive);
        Assert.Equal("BIT2033", view.Short);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        Advance(TimeSpan.FromMinutes(45));
        Assert.True(widget.IsLive);
        Advance(TimeSpan.FromMinutes(30));
        Assert.True(widget.IsLive);
        Assert.True(view.IsNow);
        Advance(TimeSpan.FromMinutes(45));
        Assert.False(widget.IsLive);
    }

    [Fact]
    public async Task NextClass_RollsOverToTheNextDay()
    {
        SaveTimetable(new ClassSession { Id = "m", Code = "BIT2044", Name = "Networks", Day = DayOfWeek.Monday, Start = new TimeOnly(8, 0), End = new TimeOnly(10, 0) });
        var (_, view) = await StartAsync<NextClassViewModel>(new NextClassDescriptor());

        Assert.StartsWith("Tomorrow, ", view.Time, StringComparison.Ordinal);
        Advance(TimeSpan.FromHours(15));
        Assert.DoesNotContain("Tomorrow", view.Time, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextClass_WithoutATimetable_SaysWhereToAddIt()
    {
        var (widget, view) = await StartAsync<NextClassViewModel>(new NextClassDescriptor());

        Assert.True(view.HasMessage);
        Assert.Equal("Add your timetable in Settings, on the What it shows page.", view.Message);
        Assert.False(widget.IsLive);
    }

    [Fact]
    public async Task Assignments_CountDaysLeft_InEnglishAndMalay()
    {
        var now = _services.Time.GetUtcNow();
        new WidgetStorage(AssignmentsDescriptor.TypeIdValue, _services.Paths).Save(AssignmentsDescriptor.DataFile, new AssignmentDocument
        {
            Assignments = [Assignment.Create("1", "Laporan FYP Bab 3", "BIT3999", new DateOnly(2026, 10, 28), null, now)],
        }, StudentJsonContext.Default.AssignmentDocument);
        var (_, view) = await StartAsync<AssignmentsViewModel>(new AssignmentsDescriptor());

        Assert.Equal("3 days left", view.First?.Countdown);
        _services.Text.SetLanguage(Localizer.Malay);
        Assert.Equal("3 hari lagi", view.First?.Countdown);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Assignments_DueToday_CountsDownEachMinute_WhileShown()
    {
        var now = _services.Time.GetUtcNow();
        new WidgetStorage(AssignmentsDescriptor.TypeIdValue, _services.Paths).Save(AssignmentsDescriptor.DataFile, new AssignmentDocument
        {
            Assignments = [Assignment.Create("1", "Quiz 2", "", new DateOnly(2026, 10, 25), new TimeOnly(12, 0), now)],
        }, StudentJsonContext.Default.AssignmentDocument);
        var (widget, view) = await StartAsync<AssignmentsViewModel>(new AssignmentsDescriptor());
        Assert.Equal("Due today, 3 h left", view.First?.Countdown);

        _visibility.Value = WidgetVisibility.Panel;
        Advance(TimeSpan.FromMinutes(10));
        _services.Time.Advance(TimeSpan.FromSeconds(1)); // Past the 09:10 tick.

        Assert.Equal("Due today, 2 h 50 min left", view.First?.Countdown);
        await widget.StopAsync();
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Assignments_TickingDone_RemovesItFromTheList()
    {
        var now = _services.Time.GetUtcNow();
        new WidgetStorage(AssignmentsDescriptor.TypeIdValue, _services.Paths).Save(AssignmentsDescriptor.DataFile, new AssignmentDocument
        {
            Assignments = [Assignment.Create("1", "Quiz 2", "", new DateOnly(2026, 10, 26), null, now)],
        }, StudentJsonContext.Default.AssignmentDocument);
        var (_, view) = await StartAsync<AssignmentsViewModel>(new AssignmentsDescriptor());

        view.First!.Done.Execute(null);

        Assert.True(view.HasNone);
    }

    [Fact]
    public void Card_SavesToTheWidget_AndBumpsItsSettings()
    {
        using var card = new StudentCardViewModel(
            new WidgetStorage(NextClassDescriptor.TypeIdValue, _services.Paths),
            new WidgetStorage(AssignmentsDescriptor.TypeIdValue, _services.Paths),
            _services.Layout,
            _services.Text,
            _services.Time);

        card.Code = "BIT2033";
        card.AddClassCommand.Execute(null);
        card.Title = "Quiz 2";
        card.AddAssignmentCommand.Execute(null);

        Assert.Single(card.Classes);
        Assert.Single(card.Assignments);
        Assert.True(_services.Layout.Document.GetWidgetSettings(NextClassDescriptor.TypeIdValue).ContainsKey(StudentCardViewModel.RevisionKey));
        var stored = new WidgetStorage(NextClassDescriptor.TypeIdValue, _services.Paths)
            .Load(NextClassDescriptor.DataFile, StudentJsonContext.Default.TimetableDocument, new TimetableDocument());
        Assert.Equal("BIT2033", Assert.Single(stored.Classes).Code);
    }

    [Fact]
    public void Card_KeepsAnAssignmentTheWidgetTickedDone_WhenItAddsAnother()
    {
        var now = _services.Time.GetUtcNow();
        var storage = new WidgetStorage(AssignmentsDescriptor.TypeIdValue, _services.Paths);
        var quiz = Assignment.Create("1", "Quiz 2", "", new DateOnly(2026, 10, 26), null, now);
        storage.Save(AssignmentsDescriptor.DataFile, new AssignmentDocument { Assignments = [quiz] }, StudentJsonContext.Default.AssignmentDocument);
        using var card = new StudentCardViewModel(
            new WidgetStorage(NextClassDescriptor.TypeIdValue, _services.Paths),
            storage,
            _services.Layout,
            _services.Text,
            _services.Time);

        // The widget ticks it done while the card is open.
        storage.Save(AssignmentsDescriptor.DataFile, new AssignmentDocument { Assignments = [quiz.Complete(now)] }, StudentJsonContext.Default.AssignmentDocument);
        card.Title = "Lab 3";
        card.AddAssignmentCommand.Execute(null);

        var stored = storage.Load(AssignmentsDescriptor.DataFile, StudentJsonContext.Default.AssignmentDocument, new AssignmentDocument());
        Assert.True(stored.Find("1")?.Done);
        Assert.Equal("Lab 3", Assert.Single(card.Assignments).Title);
    }

    [Fact]
    public void Card_RefusesAClassThatEndsBeforeItStarts()
    {
        using var card = new StudentCardViewModel(
            new WidgetStorage(NextClassDescriptor.TypeIdValue, _services.Paths),
            new WidgetStorage(AssignmentsDescriptor.TypeIdValue, _services.Paths),
            _services.Layout,
            _services.Text,
            _services.Time);
        card.Code = "BIT2033";
        card.End = card.Times[0];

        card.AddClassCommand.Execute(null);

        Assert.Empty(card.Classes);
        Assert.NotNull(card.Message);
    }

    private void SaveTimetable(params ClassSession[] classes) =>
        new WidgetStorage(NextClassDescriptor.TypeIdValue, _services.Paths).Save(NextClassDescriptor.DataFile,
            new TimetableDocument { Classes = classes }, StudentJsonContext.Default.TimetableDocument);

    private async Task<(IWidget Widget, TViewModel ViewModel)> StartAsync<TViewModel>(IWidgetDescriptor descriptor)
    {
        var lifetime = new OwnerLifetime(descriptor.TypeId);
        _lifetimes.Add(lifetime);
        var widget = descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, lifetime, _visibility));
        await widget.StartAsync(SettingValues.Empty);
        return (widget, Assert.IsType<TViewModel>(widget.ViewModel));
    }

    private void Advance(TimeSpan span)
    {
        for (var left = span; left > TimeSpan.Zero; left -= TimeSpan.FromMinutes(1))
            _services.Time.Advance(TimeSpan.FromMinutes(1));
    }
}
