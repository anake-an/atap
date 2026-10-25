using Atap.Core.Power;
using Atap.Core.Settings;

namespace Atap.Platform.Power;

/// <summary>What Windows says about power. <see cref="WindowsPowerReadings"/> reads it, tests pass their own.</summary>
internal interface IPowerReadings : IDisposable
{
    bool OnBattery { get; }

    /// <summary>Null on PCs without a battery.</summary>
    int? BatteryPercent { get; }

    bool EnergySaverOn { get; }

    /// <summary>Raised on the UI thread when any reading may have changed.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// Battery and power state, and whether Atap's battery saver is on. Windows power events and the
/// <c>saver.*</c> settings feed <see cref="BatterySaverPolicy"/>. Never polled.
/// </summary>
internal sealed class PowerState : IPowerState, IDisposable
{
    private readonly IPowerReadings _readings;
    private readonly IAppSettingsView _settings;
    private (bool OnBattery, int? Percent, bool EnergySaver, bool Saver) _last;

    public PowerState(IPowerReadings readings, IAppSettingsView settings)
    {
        _readings = readings;
        _settings = settings;
        _last = Read();
        _readings.Changed += OnInputsChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public bool OnBattery => _last.OnBattery;

    public int? BatteryPercent => _last.Percent;

    public bool EnergySaverOn => _last.EnergySaver;

    public bool SaverActive => _last.Saver;

    /// <summary>Raised on the UI thread when a reading or the saver changes.</summary>
    public event EventHandler? Changed;

    public void Dispose()
    {
        _readings.Changed -= OnInputsChanged;
        _settings.Changed -= OnSettingsChanged;
        _readings.Dispose();
    }

    private (bool, int?, bool, bool) Read()
    {
        var values = _settings.Values;
        var mode = BatterySaverPolicy.ParseMode(values.GetString(BatterySaverPolicy.ModeKey));
        var below = values.GetInt(BatterySaverPolicy.PercentKey);
        var onBattery = _readings.OnBattery;
        var percent = _readings.BatteryPercent;
        var energySaver = _readings.EnergySaverOn;
        return (onBattery, percent, energySaver, BatterySaverPolicy.IsActive(mode, below, onBattery, percent, energySaver));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(BatterySaverPolicy.ModeKey) || e.Keys.Contains(BatterySaverPolicy.PercentKey))
            OnInputsChanged(sender, e);
    }

    private void OnInputsChanged(object? sender, EventArgs e)
    {
        var next = Read();
        if (next == _last)
            return;
        _last = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Windows' power events from <c>PowerManager</c>: battery, mains and energy saver. Windows raises them on a
/// worker thread, so they are passed to the UI thread. A PC without the API reads as mains power with no saver.
/// </summary>
internal sealed class WindowsPowerReadings : IPowerReadings
{
    private readonly SynchronizationContext _ui;
    private readonly bool _available;

    public WindowsPowerReadings(SynchronizationContext ui)
    {
        _ui = ui;
        try
        {
            Windows.System.Power.PowerManager.BatteryStatusChanged += OnChanged;
            Windows.System.Power.PowerManager.PowerSupplyStatusChanged += OnChanged;
            Windows.System.Power.PowerManager.RemainingChargePercentChanged += OnChanged;
            Windows.System.Power.PowerManager.EnergySaverStatusChanged += OnChanged;
            _available = true;
        }
        catch (Exception)
        {
            _available = false;
        }
    }

    public event EventHandler? Changed;

    public bool OnBattery => Try(() =>
        Windows.System.Power.PowerManager.BatteryStatus == Windows.System.Power.BatteryStatus.Discharging
        || (Windows.System.Power.PowerManager.PowerSupplyStatus == Windows.System.Power.PowerSupplyStatus.NotPresent && HasBattery()), false);

    public int? BatteryPercent => Try<int?>(() => HasBattery() ? Windows.System.Power.PowerManager.RemainingChargePercent : null, null);

    public bool EnergySaverOn => Try(() => Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On, false);

    public void Dispose()
    {
        if (!_available)
            return;
        Windows.System.Power.PowerManager.BatteryStatusChanged -= OnChanged;
        Windows.System.Power.PowerManager.PowerSupplyStatusChanged -= OnChanged;
        Windows.System.Power.PowerManager.RemainingChargePercentChanged -= OnChanged;
        Windows.System.Power.PowerManager.EnergySaverStatusChanged -= OnChanged;
    }

    private static bool HasBattery() => Windows.System.Power.PowerManager.BatteryStatus != Windows.System.Power.BatteryStatus.NotPresent;

    private T Try<T>(Func<T> read, T fallback)
    {
        if (!_available)
            return fallback;
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private void OnChanged(object? sender, object e) => _ui.Post(_ => Changed?.Invoke(this, EventArgs.Empty), null);
}
