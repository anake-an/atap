using Atap.Composition;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Microsoft.Win32;

[assembly: AtapFeature(typeof(Atap.Platform.Clock.SystemClockFeature))]

namespace Atap.Platform.Clock;

/// <summary>
/// Tells the scheduler when the Windows clock or time zone changes and when the PC wakes from sleep.
/// Always on, in safe mode too, because every scheduled item depends on it.
/// </summary>
internal sealed class SystemClockFeature : IFeatureDescriptor
{
    public const string FeatureId = "systemClock";

    private static readonly SettingDefinition[] NoSettings = [];
    private static readonly string[] NoKeys = [];

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings => NoSettings;

    public IReadOnlyCollection<string> EnabledBy => NoKeys;

    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) =>
        new SystemClockEvents(context.SchedulerControl, context.Ui, new WindowsClockEvents());
}

/// <summary>Where clock changes and resume come from. Tests raise their own.</summary>
internal interface IClockEventSource : IDisposable
{
    /// <summary>The wall clock or the time zone changed.</summary>
    event EventHandler? ClockChanged;

    /// <summary>The PC woke from sleep.</summary>
    event EventHandler? Resumed;
}

/// <summary>The running feature. Each event reaches the scheduler on the UI thread.</summary>
internal sealed class SystemClockEvents(ISchedulerControl scheduler, SynchronizationContext ui, IClockEventSource source) : IAppFeature
{
    private bool _stopped;

    public Task StartAsync()
    {
        source.ClockChanged += OnClockChanged;
        source.Resumed += OnResumed;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _stopped = true;
        source.ClockChanged -= OnClockChanged;
        source.Resumed -= OnResumed;
        source.Dispose();
        return ValueTask.CompletedTask;
    }

    // .NET keeps the local time zone it first read, so it is read again before the minute tick runs.
    // The zone may also have changed while the PC slept.
    private void OnClockChanged(object? sender, EventArgs e) => ui.Post(_ =>
    {
        if (_stopped)
            return;
        TimeZoneInfo.ClearCachedData();
        scheduler.NotifyClockChanged();
    }, null);

    private void OnResumed(object? sender, EventArgs e) => ui.Post(_ =>
    {
        if (_stopped)
            return;
        TimeZoneInfo.ClearCachedData();
        scheduler.NotifyResumed();
    }, null);
}

/// <summary>The Windows events. SystemEvents may raise them on its own thread.</summary>
internal sealed class WindowsClockEvents : IClockEventSource
{
    public WindowsClockEvents()
    {
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event EventHandler? ClockChanged;

    public event EventHandler? Resumed;

    public void Dispose()
    {
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnTimeChanged(object? sender, EventArgs e) => ClockChanged?.Invoke(this, EventArgs.Empty);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // A line in the log, so a run that ends badly shows whether the PC had just slept or woken.
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume)
            Atap.Diagnostics.Log.Info(e.Mode == PowerModes.Suspend ? "Windows is going to sleep." : "Windows woke up.");
        if (e.Mode == PowerModes.Resume)
            Resumed?.Invoke(this, EventArgs.Empty);
    }
}
