using Atap.Core;
using Atap.Core.Alerts;
using Atap.Core.Productivity;
using Atap.Core.Productivity.Timers;
using Atap.Core.Scheduling;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Widgets.Timers.Alarm;
using AlarmItem = Atap.Core.Productivity.Timers.Alarm;

[assembly: AtapWidget(typeof(AlarmDescriptor))]

namespace Atap.Widgets.Timers.Alarm;

/// <summary>Alarms. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class AlarmDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "alarm";

    public string TypeId => TypeIdValue;

    public string NameKey => "alarm.name";

    public string DescriptionKey => "alarm.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    // Alarms ring even when the card is not on any page.
    public bool WorksWithoutPlacement => true;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Timers/Alarm/AlarmViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new AlarmWidget(context);
}

/// <summary>
/// Keeps the alarms in user data and one exact wake for the alarm due first. On that wake, and when Atap starts,
/// each alarm either rings (a Critical alert with sound) or, when its time passed while Atap was closed or the
/// PC slept, gives one "Missed alarm". Either way it is marked handled, so it never alerts twice.
/// </summary>
public sealed class AlarmWidget : WidgetBase
{
    public const string DataFile = "alarms.json";
    public const string AlertKey = "alarm";

    /// <summary>The card offers Snooze this long after an alarm rings.</summary>
    public static readonly TimeSpan SnoozeOffer = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(5);

    private static readonly ScheduleOptions Exact = new(TimeSpan.Zero, false, MissedRun.RunOnce);

    private readonly AlarmsViewModel _viewModel;
    private AlarmDocument _document = new();
    private IDisposable? _wake;
    private IDisposable? _tick;
    private string? _ringingId;
    private DateTimeOffset _snoozeOfferEnds;

    public AlarmWidget(WidgetContext context)
        : base(context)
    {
        _viewModel = new AlarmsViewModel(context.Text, new AlarmActions(SetEnabled, Save, Delete, Snooze));
    }

    public override object ViewModel => _viewModel;

    private DateTimeOffset Now => Context.Time.GetUtcNow();

    private TimeZoneInfo Zone => Context.Time.LocalTimeZone;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _document = Context.Storage.Load(DataFile, TimersJsonContext.Default.AlarmDocument, new AlarmDocument()).Normalize(Now);
        Context.Visibility.Changed += OnVisibilityChanged;
        Check();
        FollowVisibility();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _wake?.Dispose();
        _wake = null;
        _tick?.Dispose();
        _tick = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void SetEnabled(string id, bool enabled)
    {
        if (_document.Find(id) is not { } alarm || alarm.Enabled == enabled)
            return;
        Commit(_document.Upsert(enabled ? alarm.TurnOn(Now) : alarm.TurnOff()));
    }

    private void Save(string? id, TimeOnly time, AlarmDays days)
    {
        var alarm = id is not null && _document.Find(id) is { } existing
            ? existing.Reschedule(time, days, Now)
            : AlarmItem.Create(ItemIds.New(), time, days, null, Now);
        Commit(_document.Upsert(alarm));
    }

    private void Delete(string id) => Commit(_document.Remove(id));

    private void Snooze()
    {
        if (_ringingId is null || _document.Find(_ringingId) is not { } alarm)
            return;
        _ringingId = null;
        Context.Alerts.Dismiss(AlertKey);
        Commit(_document.Upsert(alarm.Snooze(Now, SnoozeLength)));
    }

    private void Commit(AlarmDocument document)
    {
        _document = document;
        Context.Storage.Save(DataFile, _document, TimersJsonContext.Default.AlarmDocument);
        Check();
    }

    private void Check()
    {
        var now = Now;
        var changed = false;
        foreach (var alarm in _document.Alarms.ToList())
        {
            var status = alarm.Check(now, Zone);
            if (status is not { State: AlarmState.Ringing or AlarmState.Missed, Occurrence: { } occurrence })
                continue;
            _document = _document.Upsert(alarm.MarkHandled(occurrence));
            changed = true;
            if (status.State == AlarmState.Ringing)
            {
                _ringingId = alarm.Id;
                _snoozeOfferEnds = occurrence + SnoozeOffer;
                Context.Alerts.Show(_viewModel.RingAlert(alarm, occurrence));
            }
            else
            {
                Context.Alerts.Show(_viewModel.MissedAlert(alarm, occurrence));
            }
        }
        if (changed)
            Context.Storage.Save(DataFile, _document, TimersJsonContext.Default.AlarmDocument);
        if (_ringingId is not null && now >= _snoozeOfferEnds)
            _ringingId = null;

        var next = _document.NextDue(now, Zone);
        _viewModel.Show(_document, next, now, Zone, _ringingId is not null);

        _wake?.Dispose();
        var due = next?.Due;
        if (_ringingId is not null && (due is null || _snoozeOfferEnds < due))
            due = _snoozeOfferEnds;
        _wake = due is { } at ? Context.Scheduler.At(at, OnWake, Exact) : null;
    }

    private void OnWake()
    {
        _wake = null;
        Check();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        _viewModel.Tick(Now);
        FollowVisibility();
    }

    /// <summary>"in 9 h 12 min" counts down each minute while the card is on screen, and nothing ticks while it is not.</summary>
    private void FollowVisibility()
    {
        var shown = Context.Visibility.Value is WidgetVisibility.Compact or WidgetVisibility.Expanded or WidgetVisibility.Panel;
        if (shown && _tick is null)
            _tick = Context.Scheduler.EveryMinute(() => _viewModel.Tick(Now));
        else if (!shown && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }
}

/// <summary>What the card can ask the widget to do.</summary>
public sealed record AlarmActions(
    Action<string, bool> SetEnabled,
    Action<string?, TimeOnly, AlarmDays> Save,
    Action<string> Delete,
    Action Snooze);
