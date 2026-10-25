using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Productivity.Timers;
using Atap.Notch.Automation;

namespace Atap.Widgets.Timers.Focus;

public sealed class FocusTimerViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private FocusTimerState _state = new();
    private FocusSettings _settings = new();
    private DateTimeOffset _now;

    public FocusTimerViewModel(ILocalizer text, Action toggle, Action addTime, Action skip, Action reset)
    {
        _text = text;
        ToggleCommand = new RelayCommand(toggle);
        AddTimeCommand = new RelayCommand(addTime, () => !IsIdle);
        SkipCommand = new RelayCommand(skip);
        ResetCommand = new RelayCommand(reset);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IRelayCommand ToggleCommand { get; }

    public IRelayCommand AddTimeCommand { get; }

    public IRelayCommand SkipCommand { get; }

    public IRelayCommand ResetCommand { get; }

    public bool IsRunning => _state.Run == FocusRun.Running;

    public bool IsPaused => _state.Run == FocusRun.Paused;

    public bool IsIdle => _state.Run == FocusRun.Idle;

    public bool IsBreak => _state.Phase != FocusPhase.Focus;

    /// <summary>"Focus", "Short break" or "Long break".</summary>
    public string PhaseText => _text[PhaseKey(_state.Phase)];

    /// <summary>"24:59". While idle, the full length of the phase that is next.</summary>
    public string TimeText => TimerText.Clock(Remaining);

    /// <summary>"25 min" left, also while paused. For the compact notch, which changes once a minute.</summary>
    public string ShortText => TimerText.Remaining(Remaining, _text.Language);

    /// <summary>"Paused" under or beside the time in the compact notch, or empty while it runs or waits.</summary>
    public string CompactState => IsPaused ? _text["focus.paused"] : string.Empty;

    /// <summary>0 to 100.</summary>
    public double Progress => _state.ProgressAt(_now) * 100;

    /// <summary>"Session 2 of 4".</summary>
    public string SessionText => _text.Format("focus.session",
        ("number", Math.Min(_state.CompletedInCycle + (IsBreak ? 0 : 1), _settings.SessionsPerLongBreak).ToString(CultureInfo.CurrentCulture)),
        ("count", _settings.SessionsPerLongBreak.ToString(CultureInfo.CurrentCulture)));

    /// <summary>"Start", "Pause" or "Resume", for the main button's screen reader name.</summary>
    public string ToggleLabel => _text[IsRunning ? "focus.pause" : IsPaused ? "focus.resume" : "focus.start"];

    public string AccessibleSummary => _text.Format(
        IsIdle ? "focus.accessibleIdle" : IsPaused ? "focus.accessiblePaused" : "focus.accessible",
        ("phase", PhaseText),
        ("left", TimerText.Remaining(Remaining, _text.Language)));

    private TimeSpan Remaining => IsIdle ? _settings.LengthOf(_state.Phase) : _state.RemainingAt(_now);

    public void Show(FocusTimerState state, FocusSettings settings, DateTimeOffset now)
    {
        _state = state;
        _settings = settings;
        _now = now;
        OnPropertyChanged(string.Empty);
        AddTimeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>"Focus done. Time for a 5 min break." Late ends, reported after a restart, say when they ended.</summary>
    public Alert AlertFor(FocusPhaseEnd ended, FocusTimerState next, FocusSettings settings)
    {
        var title = _text[ended.Phase == FocusPhase.Focus ? "focus.alert.focusDone" : "focus.alert.breakDone"];
        var subtitle = ended.Late
            ? _text.Format("focus.alert.endedAt", ("time", ended.EndedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)))
            : next.Phase == FocusPhase.Focus
                ? _text["focus.alert.backToFocus"]
                : _text.Format("focus.alert.breakNext", ("length", TimerText.Remaining(settings.LengthOf(next.Phase), _text.Language)));
        return new Alert("focus", AlertPriority.Critical, title)
        {
            Subtitle = subtitle,
            IconKey = "timer",
            Tone = AlertTone.Positive,
            SoundKey = ended.Late ? null : AlertSounds.Default,
        };
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private static string PhaseKey(FocusPhase phase) => phase switch
    {
        FocusPhase.ShortBreak => "focus.phase.shortBreak",
        FocusPhase.LongBreak => "focus.phase.longBreak",
        _ => "focus.phase.focus",
    };

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
