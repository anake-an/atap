namespace Atap.Core.Power;

/// <summary>When battery saver turns on. Stored as a string in <see cref="BatterySaverPolicy.ModeKey"/>.</summary>
public enum SaverMode
{
    /// <summary>On while the PC runs on battery. The default.</summary>
    OnBattery,

    /// <summary>Always on, plugged in or not.</summary>
    Always,

    /// <summary>Never on.</summary>
    Never,

    /// <summary>On while on battery with the charge below <see cref="BatterySaverPolicy.PercentKey"/>.</summary>
    BelowPercent,

    /// <summary>On while Windows energy saver is on.</summary>
    FollowWindows,
}

/// <summary>
/// Turns the <c>saver.*</c> settings and the power state into <c>SaverActive</c>. While it is true,
/// deferrable scheduler periods and HTTP MaxAge are multiplied by <see cref="Multiplier"/> and
/// animations stop. Alarms and prayer reminders are never deferred.
/// </summary>
public static class BatterySaverPolicy
{
    public const string ModeKey = "saver.mode";

    public const string PercentKey = "saver.percent";

    public const SaverMode DefaultMode = SaverMode.OnBattery;

    public const int DefaultPercent = 20;

    /// <summary>The one factor for deferrable scheduler periods and HTTP MaxAge while saver is active.</summary>
    public const int Multiplier = 4;

    /// <summary>The stored values of <see cref="ModeKey"/>, in <see cref="SaverMode"/> order.</summary>
    public static IReadOnlyList<string> ModeChoices { get; } = ["onBattery", "always", "never", "belowPercent", "followWindows"];

    /// <summary>The mode for a stored value, ignoring case. Missing or unknown values give <see cref="DefaultMode"/>.</summary>
    public static SaverMode ParseMode(string? value)
    {
        for (var index = 0; index < ModeChoices.Count; index++)
        {
            if (string.Equals(ModeChoices[index], value?.Trim(), StringComparison.OrdinalIgnoreCase))
                return (SaverMode)index;
        }
        return DefaultMode;
    }

    /// <summary>The stored value for <paramref name="mode"/>.</summary>
    public static string ToSettingValue(SaverMode mode) =>
        Enum.IsDefined(mode) ? ModeChoices[(int)mode] : ModeChoices[(int)DefaultMode];

    /// <summary>Whether battery saver is on.</summary>
    /// <param name="mode">The <see cref="ModeKey"/> setting.</param>
    /// <param name="belowPercent">The <see cref="PercentKey"/> setting, used only by <see cref="SaverMode.BelowPercent"/>. Clamped to 0 to 100.</param>
    /// <param name="onBattery">The PC runs on battery.</param>
    /// <param name="batteryPercent">Battery charge from 0 to 100, or null without a battery or level.</param>
    /// <param name="energySaverOn">Windows energy saver is on.</param>
    public static bool IsActive(SaverMode mode, int belowPercent, bool onBattery, int? batteryPercent, bool energySaverOn) => mode switch
    {
        SaverMode.Always => true,
        SaverMode.Never => false,
        // Plugging in ends it, as with Windows battery saver. An unknown level is never below.
        SaverMode.BelowPercent => onBattery && batteryPercent is { } percent && percent < Math.Clamp(belowPercent, 0, 100),
        SaverMode.FollowWindows => energySaverOn,
        _ => onBattery,
    };
}
