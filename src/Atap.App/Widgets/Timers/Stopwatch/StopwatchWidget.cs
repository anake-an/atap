using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Productivity.Timers;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Timers.Stopwatch;

[assembly: AtapWidget(typeof(StopwatchDescriptor))]

namespace Atap.Widgets.Timers.Stopwatch;

/// <summary>A stopwatch with laps. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class StopwatchDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "stopwatch";

    public string TypeId => TypeIdValue;

    public string NameKey => "stopwatch.name";

    public string DescriptionKey => "stopwatch.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Timers/Stopwatch/StopwatchViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new StopwatchWidget(context);
}

/// <summary>
/// Runs <see cref="StopwatchState"/>, saved on every change, so it keeps counting across a restart. It has no
/// wake of its own: only the display tick, while it runs and is on screen.
/// </summary>
public sealed class StopwatchWidget : WidgetBase
{
    public const string StateFile = "stopwatch.json";

    private readonly StopwatchViewModel _viewModel;
    private readonly TimerTicks _ticks;
    private StopwatchState _state = new();

    public StopwatchWidget(WidgetContext context)
        : base(context)
    {
        _viewModel = new StopwatchViewModel(
            context.Text,
            () => Change(s => s.IsRunning ? s.Stop(Now) : s.Start(Now)),
            () => Change(s => s.Lap(Now)),
            () => Change(s => s.Reset()));
        _ticks = new TimerTicks(context.Scheduler, context.Visibility, Refresh);
    }

    public override object ViewModel => _viewModel;

    private DateTimeOffset Now => Context.Time.GetUtcNow();

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _state = Context.Storage.Load(StateFile, TimersJsonContext.Default.StopwatchState, new StopwatchState(), DataScope.Machine);
        Context.Visibility.Changed += OnVisibilityChanged;
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _ticks.Dispose();
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void Change(Func<StopwatchState, StopwatchState> change)
    {
        _state = change(_state);
        Context.Storage.Save(StateFile, _state, TimersJsonContext.Default.StopwatchState, DataScope.Machine);
        Refresh();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _viewModel.Show(_state, Now);
        SetIsLive(_state.IsRunning);
        _ticks.Update(_state.IsRunning);
    }
}

/// <summary>One lap: "Lap 3", its own length and the total at that point.</summary>
public sealed record LapRow(string Name, string Split, string Total);

public sealed class StopwatchViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    /// <summary>Laps shown in the medium card, newest first.</summary>
    public const int ShownLaps = 3;

    private readonly ILocalizer _text;
    private StopwatchState _state = new();
    private DateTimeOffset _now;

    public StopwatchViewModel(ILocalizer text, Action toggle, Action lap, Action reset)
    {
        _text = text;
        ToggleCommand = new RelayCommand(toggle);
        LapCommand = new RelayCommand(lap, () => IsRunning);
        ResetCommand = new RelayCommand(reset, () => !IsRunning && HasTime);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IRelayCommand ToggleCommand { get; }

    public IRelayCommand LapCommand { get; }

    public IRelayCommand ResetCommand { get; }

    public bool IsRunning => _state.IsRunning;

    public bool HasTime => _state.ElapsedAt(_now) > TimeSpan.Zero;

    /// <summary>"12:34" or "1:02:03".</summary>
    public string TimeText => TimerText.Clock(_state.ElapsedAt(_now));

    /// <summary>"12 min" for the compact notch, which changes once a minute.</summary>
    public string ShortText => TimerText.Elapsed(_state.ElapsedAt(_now));

    public string ToggleLabel => _text[IsRunning ? "stopwatch.stop" : HasTime ? "stopwatch.resume" : "stopwatch.start"];

    public IReadOnlyList<LapRow> Laps { get; private set; } = [];

    public bool HasLaps => Laps.Count > 0;

    public string AccessibleSummary => _text.Format(
        IsRunning ? "stopwatch.accessibleRunning" : "stopwatch.accessible",
        ("time", TimeText));

    public void Show(StopwatchState state, DateTimeOffset now)
    {
        _state = state;
        _now = now;
        Laps = state.LapTimes()
            .Reverse()
            .Take(ShownLaps)
            .Select(l => new LapRow(
                _text.Format("stopwatch.lap", ("number", l.Number.ToString(System.Globalization.CultureInfo.CurrentCulture))),
                TimerText.Clock(l.Split),
                TimerText.Clock(l.Total)))
            .ToList();
        OnPropertyChanged(string.Empty);
        LapCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_state, _now);
}
