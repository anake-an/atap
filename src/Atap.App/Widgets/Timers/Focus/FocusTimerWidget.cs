using Atap.Core;
using Atap.Core.Alerts;
using Atap.Core.Commands;
using Atap.Core.Productivity.Timers;
using Atap.Core.Scheduling;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Widgets.Timers.Focus;

[assembly: AtapWidget(typeof(FocusTimerDescriptor))]

namespace Atap.Widgets.Timers.Focus;

/// <summary>Focus sessions and breaks. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class FocusTimerDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "focus-timer";
    public const string FocusKey = "focus";
    public const string ShortBreakKey = "shortBreak";
    public const string LongBreakKey = "longBreak";
    public const string SessionsKey = "sessions";
    public const string AutoBreaksKey = "autoBreaks";
    public const string AutoFocusKey = "autoFocus";

    public string TypeId => TypeIdValue;

    public string NameKey => "focus.name";

    public string DescriptionKey => "focus.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(FocusKey, "focus.focusLength", SettingType.Int, 25) { Min = 5, Max = 180, Step = 5, UnitKey = "focus.minutes" },
        new(ShortBreakKey, "focus.shortBreak", SettingType.Int, 5) { Min = 1, Max = 60, Step = 1, UnitKey = "focus.minutes" },
        new(LongBreakKey, "focus.longBreak", SettingType.Int, 15) { Min = 5, Max = 60, Step = 5, UnitKey = "focus.minutes" },
        new(SessionsKey, "focus.sessions", SettingType.Int, 4) { Min = 1, Max = 12, Step = 1, DescriptionKey = "focus.sessionsHint" },
        new(AutoBreaksKey, "focus.autoBreaks", SettingType.Bool, false),
        new(AutoFocusKey, "focus.autoFocus", SettingType.Bool, false),
    ];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Timers/Focus/FocusTimerViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new FocusTimerWidget(context);
}

/// <summary>
/// Runs <see cref="FocusTimerState"/>. The end time is saved, so closing Atap or restarting the PC keeps the timer.
/// One exact wake at the end of each phase, plus a display tick only while the timer runs and is on screen.
/// </summary>
public sealed class FocusTimerWidget : WidgetBase
{
    public const string StateFile = "focus.json";
    public const string ToggleCommandId = "focus.toggle";

    // The end of a phase is a time the user waits for: no slack, never stretched, and reported after sleep.
    private static readonly ScheduleOptions Exact = new(TimeSpan.Zero, false, MissedRun.RunOnce);
    private static readonly TimeSpan AddStep = TimeSpan.FromMinutes(5);

    private readonly FocusTimerViewModel _viewModel;
    private readonly TimerTicks _ticks;
    private FocusSettings _settings = new();
    private FocusTimerState _state = new();
    private IDisposable? _due;
    private DateTimeOffset? _dueAt;
    private IDisposable? _command;

    public FocusTimerWidget(WidgetContext context)
        : base(context)
    {
        _viewModel = new FocusTimerViewModel(context.Text, Toggle, () => Change(s => s.AddTime(AddStep, Now)), () => Change(s => s.Skip()), () => Change(s => s.Reset()));
        _ticks = new TimerTicks(context.Scheduler, context.Visibility, Refresh);
    }

    public override object ViewModel => _viewModel;

    private DateTimeOffset Now => Context.Time.GetUtcNow();

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _settings = new FocusSettings
        {
            Focus = TimeSpan.FromMinutes(settings.GetInt(FocusTimerDescriptor.FocusKey)),
            ShortBreak = TimeSpan.FromMinutes(settings.GetInt(FocusTimerDescriptor.ShortBreakKey)),
            LongBreak = TimeSpan.FromMinutes(settings.GetInt(FocusTimerDescriptor.LongBreakKey)),
            SessionsPerLongBreak = settings.GetInt(FocusTimerDescriptor.SessionsKey),
            AutoStartBreaks = settings.GetBool(FocusTimerDescriptor.AutoBreaksKey),
            AutoStartFocus = settings.GetBool(FocusTimerDescriptor.AutoFocusKey),
        }.Clamped();
        _state = Context.Storage.Load(StateFile, TimersJsonContext.Default.FocusTimerState, new FocusTimerState(), DataScope.Machine).Normalize(Now);
        _command = Context.Commands.Register(new AtapCommand(ToggleCommandId, "focus.toggle", _ => { Toggle(); return Task.CompletedTask; })
        {
            KeywordsKey = "focus.keywords",
        });
        Context.Visibility.Changed += OnVisibilityChanged;
        // A phase that ended while Atap was closed is reported now, marked late.
        OnDue();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _command?.Dispose();
        _command = null;
        _due?.Dispose();
        _due = null;
        _dueAt = null;
        _ticks.Dispose();
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void Toggle() => Change(s => s.Run == FocusRun.Running ? s.Pause(Now) : s.Start(_settings, Now));

    private void Change(Func<FocusTimerState, FocusTimerState> change)
    {
        _state = change(_state);
        Save();
        Refresh();
    }

    private void OnDue()
    {
        _dueAt = null;
        var advance = _state.Advance(_settings, Now);
        if (advance.Ended is { } ended)
        {
            _state = advance.State;
            Save();
            Context.Alerts.Show(_viewModel.AlertFor(ended, _state, _settings));
        }
        Refresh();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var now = Now;
        _viewModel.Show(_state, _settings, now);
        SetIsLive(_state.IsLive);
        if (_state.NextDue != _dueAt)
        {
            _due?.Dispose();
            _dueAt = _state.NextDue;
            _due = _dueAt is { } due ? Context.Scheduler.At(due, OnDue, Exact) : null;
        }
        _ticks.Update(_state.Run == FocusRun.Running);
    }

    private void Save() => Context.Storage.Save(StateFile, _state, TimersJsonContext.Default.FocusTimerState, DataScope.Machine);
}
