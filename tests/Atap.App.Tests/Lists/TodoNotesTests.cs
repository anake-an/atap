using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Widgets;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Lists.Notes;
using Atap.Widgets.Lists.Todo;

namespace Atap.Tests.Lists;

/// <summary>To-do and notes on fake time, starting 25 October 2026 at 09:00 with UTC as the local zone.</summary>
public sealed class TodoNotesTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly List<OwnerLifetime> _lifetimes = [];
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Panel);

    public TodoNotesTests() => _services.Time.SetLocalTimeZone(TimeZoneInfo.Utc);

    public void Dispose()
    {
        foreach (var lifetime in _lifetimes)
            lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task Todo_AddTickAndClear_SurviveARestart()
    {
        var (first, todo) = await StartAsync<TodoViewModel>(new TodoDescriptor());
        todo.NewTitle = "Pay the bill";
        todo.AddCommand.Execute(null);
        todo.NewTitle = "Buy milk";
        todo.AddCommand.Execute(null);
        Assert.Equal(string.Empty, todo.NewTitle);
        Assert.Equal("2 to do", todo.CountText);

        todo.Rows.Single(r => r.Title == "Buy milk").Toggle.Execute(null);
        Assert.Equal("1 to do", todo.CountText);
        Assert.True(todo.HasDone);
        await first.StopAsync();

        var (_, again) = await StartAsync<TodoViewModel>(new TodoDescriptor());
        Assert.Equal(["Pay the bill", "Buy milk"], again.Rows.Select(r => r.Title));
        again.ClearDoneCommand.Execute(null);
        Assert.Single(again.Rows);
    }

    [Fact]
    public async Task Todo_OnlyTheNextReminderIsArmed_AndItAlertsOnTheDay()
    {
        var (_, todo) = await StartAsync<TodoViewModel>(new TodoDescriptor());
        todo.NewTitle = "Hand in the report";
        todo.AddCommand.Execute(null);
        todo.NewTitle = "Call the bank";
        todo.AddCommand.Execute(null);
        todo.Rows[0].DueTomorrow.Execute(null);
        todo.Rows[1].DueTomorrow.Execute(null);

        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
        Assert.Equal("Tomorrow", todo.Rows[0].Due);

        for (var i = 0; i < 24; i++)
            _services.Time.Advance(TimeSpan.FromHours(1));

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal(AlertPriority.High, alert.Alert.Priority);
        Assert.Equal("Due today", alert.Alert.Subtitle);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Notes_SaveOneSecondAfterTyping_AndKeepAcrossARestart()
    {
        var (first, notes) = await StartAsync<NotesViewModel>(new NotesDescriptor());
        notes.NewCommand.Execute(null);
        notes.EditText = "Shopping";
        notes.EditText = "Shopping\nmilk, eggs";

        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
        _services.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        await first.StopAsync();

        var (_, again) = await StartAsync<NotesViewModel>(new NotesDescriptor());
        Assert.Equal("Shopping\nmilk, eggs", again.EditText);
        Assert.Equal("Notes. Shopping", again.AccessibleSummary);
    }

    [Fact]
    public async Task Notes_StoppingSavesAtOnce()
    {
        var (first, notes) = await StartAsync<NotesViewModel>(new NotesDescriptor());
        notes.NewCommand.Execute(null);
        notes.EditText = "Quick thought";

        await first.StopAsync();

        var (_, again) = await StartAsync<NotesViewModel>(new NotesDescriptor());
        Assert.Equal("Quick thought", again.EditText);
    }

    [Fact]
    public async Task Notes_LeavingThePanel_DropsEmptyNotes()
    {
        var (_, notes) = await StartAsync<NotesViewModel>(new NotesDescriptor());
        notes.NewCommand.Execute(null);
        Assert.True(notes.HasNote);

        _visibility.Value = WidgetVisibility.Hidden;

        Assert.True(notes.HasNone);
    }

    private async Task<(IWidget Widget, TViewModel ViewModel)> StartAsync<TViewModel>(IWidgetDescriptor descriptor)
    {
        var lifetime = new OwnerLifetime(descriptor.TypeId);
        _lifetimes.Add(lifetime);
        var widget = descriptor.Create(_services.CreateContext(descriptor.TypeId, descriptor, lifetime, _visibility));
        await widget.StartAsync(SettingValues.Empty);
        return (widget, Assert.IsType<TViewModel>(widget.ViewModel));
    }
}
