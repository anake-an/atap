using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Productivity.Timers;
using Atap.Core.Scheduling;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Session;
using Atap.Widgets.Timers.Water;

[assembly: AtapWidget(typeof(WaterDescriptor))]

namespace Atap.Widgets.Timers.Water;

/// <summary>Water reminders and a glass count. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class WaterDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "water";
    public const string IntervalKey = "interval";
    public const string FromKey = "from";
    public const string UntilKey = "until";
    public const string GoalKey = "goal";

    public string TypeId => TypeIdValue;

    public string NameKey => "water.name";

    public string DescriptionKey => "water.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(IntervalKey, "water.interval", SettingType.Int, 60) { Min = 15, Max = 240, Step = 15, UnitKey = "water.minutes" },
        new(FromKey, "water.from", SettingType.Time, "08:00") { DescriptionKey = "water.hoursHint" },
        new(UntilKey, "water.until", SettingType.Time, "22:00"),
        new(GoalKey, "water.goal", SettingType.Int, 8) { Min = 1, Max = 30, Step = 1, UnitKey = "water.glasses" },
    ];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    // Reminders keep coming even when the card is not on any page.
    public bool WorksWithoutPlacement => true;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Timers/Water/WaterViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new WaterWidget(context, new WindowsSessionLock(context.Ui));
}

/// <summary>
/// One wake for the next reminder, inside active hours. While the PC is locked there is no wake at all, and
/// unlocking starts the interval again, so nobody comes back to a pile of reminders.
/// </summary>
public sealed class WaterWidget : WidgetBase
{
    public const string StateFile = "water.json";

    // A gentle reminder: it may run a minute late to share a wake, and runs once after sleep to plan the next.
    // The active hours are local, so a time zone change plans again.
    private static readonly ScheduleOptions Gentle = new(TimeSpan.FromMinutes(1), false, MissedRun.RunOnce, WallClock: true);

    private readonly WaterViewModel _viewModel;
    private readonly ISessionLock _session;
    private WaterSettings _settings = new();
    private WaterState _state = new();
    private IDisposable? _wake;
    private DateTimeOffset? _next;

    internal WaterWidget(WidgetContext context, ISessionLock session)
        : base(context)
    {
        _session = session;
        _viewModel = new WaterViewModel(context.Text, () => Change(s => s.Drink(Now, _settings, Zone)), () => Change(s => s.UndoDrink(Now, _settings, Zone)));
    }

    public override object ViewModel => _viewModel;

    private DateTimeOffset Now => Context.Time.GetUtcNow();

    private TimeZoneInfo Zone => Context.Time.LocalTimeZone;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _settings = new WaterSettings
        {
            Interval = TimeSpan.FromMinutes(settings.GetInt(WaterDescriptor.IntervalKey)),
            ActiveFrom = settings.GetTime(WaterDescriptor.FromKey),
            ActiveUntil = settings.GetTime(WaterDescriptor.UntilKey),
            DailyGoal = settings.GetInt(WaterDescriptor.GoalKey),
        }.Clamped();
        _state = Context.Storage.Load(StateFile, TimersJsonContext.Default.WaterState, new WaterState(), DataScope.Machine);
        if (_state.Anchor is null)
            _state = _state.Restart(Now);
        _session.Changed += OnSessionChanged;
        Context.Visibility.Changed += OnVisibilityChanged;
        Plan();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        _session.Changed -= OnSessionChanged;
        Context.Visibility.Changed -= OnVisibilityChanged;
        Disarm();
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _session.Dispose();
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void Change(Func<WaterState, WaterState> change)
    {
        _state = change(_state);
        Save();
        Plan();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!_session.IsLocked)
        {
            _state = _state.Restart(Now);
            Save();
        }
        Plan();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Show();

    private void Plan()
    {
        Disarm();
        if (!_session.IsLocked)
        {
            var now = Now;
            if (_state.IsDueAt(_settings, now, Zone))
            {
                _state = _state.Reminded(now);
                Context.Alerts.Show(_viewModel.Reminder(_state.CountAt(now, _settings, Zone), _settings.DailyGoal));
            }
            var next = _state.NextReminder(_settings, now, Zone);
            _state = next.State;
            Save();
            _next = next.Due;
            _wake = Context.Scheduler.At(next.Due, OnWake, Gentle);
        }
        Show();
    }

    private void OnWake()
    {
        _wake = null;
        Plan();
    }

    private void Disarm()
    {
        _wake?.Dispose();
        _wake = null;
        _next = null;
    }

    private void Show() =>
        _viewModel.Show(_state.CountAt(Now, _settings, Zone), _settings.DailyGoal, _next is { } next ? TimeZoneInfo.ConvertTime(next, Zone) : null, _session.IsLocked);

    private void Save() => Context.Storage.Save(StateFile, _state, TimersJsonContext.Default.WaterState, DataScope.Machine);
}

public sealed class WaterViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private int _count;
    private int _goal = 8;
    private DateTimeOffset? _next;
    private bool _paused;

    public WaterViewModel(ILocalizer text, Action drink, Action undo)
    {
        _text = text;
        DrinkCommand = new RelayCommand(drink, () => _count < WaterState.MaxCount);
        UndoCommand = new RelayCommand(undo, () => _count > 0);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IRelayCommand DrinkCommand { get; }

    public IRelayCommand UndoCommand { get; }

    /// <summary>"3 of 8".</summary>
    public string CountText => _text.Format("water.count", ("count", _count.ToString(CultureInfo.CurrentCulture)), ("goal", _goal.ToString(CultureInfo.CurrentCulture)));

    public double Count => Math.Min(_count, _goal);

    public double Goal => _goal;

    public bool GoalMet => _count >= _goal;

    /// <summary>"Next at 2:00 PM", "Paused while the PC is locked", or "Goal reached".</summary>
    public string NextText =>
        _paused ? _text["water.paused"]
        : _next is { } next ? _text.Format("water.next", ("time", next.ToString("t", CultureInfo.CurrentCulture)))
        : string.Empty;

    public string AccessibleSummary => _text.Format("water.accessible", ("count", CountText), ("next", NextText));

    public void Show(int count, int goal, DateTimeOffset? next, bool paused)
    {
        _count = count;
        _goal = goal;
        _next = next;
        _paused = paused;
        OnPropertyChanged(string.Empty);
        DrinkCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>"Time for some water", "3 of 8 glasses today".</summary>
    public Alert Reminder(int count, int goal) =>
        new("water", AlertPriority.Normal, _text["water.alert"])
        {
            Subtitle = _text.Format("water.alertCount", ("count", count.ToString(CultureInfo.CurrentCulture)), ("goal", goal.ToString(CultureInfo.CurrentCulture))),
            IconKey = "bell",
        };

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
