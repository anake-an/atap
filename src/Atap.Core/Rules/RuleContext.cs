namespace Atap.Core.Rules;

/// <summary>
/// The inputs rules are checked against, read by the App at one moment. Only the inputs in
/// <see cref="RuleOutcome.InputsUsed"/> need to be filled. An input left empty never matches.
/// </summary>
public sealed record RuleContext
{
    public required DateTimeOffset Now { get; init; }

    /// <summary>The zone time windows are read in, normally <see cref="TimeZoneInfo.Local"/>.</summary>
    public required TimeZoneInfo TimeZone { get; init; }

    /// <summary>Executable name of the foreground window's process, for example "valorant.exe". A full path also works.</summary>
    public string? ForegroundProcessName { get; init; }

    /// <summary>The connected Wi-Fi network name. Null without Wi-Fi or when Windows does not share it.</summary>
    public string? WifiSsid { get; init; }

    public bool OnBattery { get; init; }

    /// <summary>Battery charge from 0 to 100, or null without a battery.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>Windows energy saver is on.</summary>
    public bool EnergySaverOn { get; init; }
}
