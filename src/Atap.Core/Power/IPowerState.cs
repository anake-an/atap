namespace Atap.Core.Power;

/// <summary>Battery and power, from Windows power events. Never polled.</summary>
public interface IPowerState
{
    bool OnBattery { get; }

    /// <summary>Null on PCs without a battery.</summary>
    int? BatteryPercent { get; }

    /// <summary>The Windows energy saver is on.</summary>
    bool EnergySaverOn { get; }

    /// <summary>Atap's own battery saver is active: deferrable refreshes slow down and animations stop.</summary>
    bool SaverActive { get; }

    /// <summary>Raised on the UI thread.</summary>
    event EventHandler? Changed;
}
