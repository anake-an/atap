namespace Atap.Core.Alerts;

/// <summary>How urgent an alert is. Decides how long it shows and what happens while alerts are quiet.</summary>
public enum AlertPriority
{
    /// <summary>Volume, brightness, Caps Lock. Replaced by the next one with the same key. Dropped while quiet.</summary>
    Overlay,

    /// <summary>General notices. Go to the Missed list while quiet.</summary>
    Normal,

    /// <summary>Prayer times and reminders. Go to the Missed list while quiet.</summary>
    High,

    /// <summary>Alarms and finished timers. Stay until dismissed. Wait while the notch is hidden.</summary>
    Critical,
}

/// <summary>The colour family of an alert.</summary>
public enum AlertTone
{
    Neutral,
    Positive,
    Warning,
    Negative,
}

/// <param name="Key">Identifies the alert within its source. A new alert with the same key replaces the old one.</param>
/// <param name="Priority">How urgent it is.</param>
/// <param name="Title">Already localized text.</param>
public sealed record Alert(string Key, AlertPriority Priority, string Title)
{
    public string? Subtitle { get; init; }

    public string? IconKey { get; init; }

    /// <summary>0 to 1 for a level bar (volume, brightness, progress).</summary>
    public double? Level { get; init; }

    public AlertTone Tone { get; init; }

    /// <summary>Overrides the default time on screen for the priority.</summary>
    public TimeSpan? Duration { get; init; }

    public string? SoundKey { get; init; }
}

/// <summary>What a widget or feature uses to show alerts. The source is set by whoever created the sink.</summary>
public interface IAlertSink
{
    void Show(Alert alert);

    void Dismiss(string key);
}

/// <param name="Source">The instance or feature id that sent it.</param>
/// <param name="Alert">The alert.</param>
/// <param name="ReceivedAt">When it arrived.</param>
public sealed record ShownAlert(string Source, Alert Alert, DateTimeOffset ReceivedAt);

/// <summary>One alert shows at a time, picked by priority. No Windows toast is ever used.</summary>
public interface IAlertQueue
{
    ShownAlert? Current { get; }

    /// <summary>Normal and High alerts that arrived while quiet, newest first. Memory only.</summary>
    IReadOnlyList<ShownAlert> Missed { get; }

    IAlertSink For(string source);

    /// <summary>Quiet while any owner asks for it (a rule, Windows quiet hours, a hidden notch).</summary>
    void SetQuiet(string ownerId, bool quiet);

    void ClearMissed();

    /// <summary>Raised on the UI thread when <see cref="Current"/> or <see cref="Missed"/> changes.</summary>
    event EventHandler? Changed;
}
