using Atap.Platform.Power;

namespace Atap.Tests.Infrastructure;

/// <summary>Power readings a test sets by hand. Starts on mains power with no battery and no energy saver.</summary>
internal sealed class FakePowerReadings : IPowerReadings
{
    public bool OnBattery { get; set; }

    public int? BatteryPercent { get; set; }

    public bool EnergySaverOn { get; set; }

    public bool Disposed { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Sets the readings and raises Changed, as a Windows power event would.</summary>
    public void Set(bool onBattery, int? percent = null, bool energySaver = false)
    {
        OnBattery = onBattery;
        BatteryPercent = percent;
        EnergySaverOn = energySaver;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Disposed = true;
}
