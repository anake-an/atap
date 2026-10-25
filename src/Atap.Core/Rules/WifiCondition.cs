namespace Atap.Core.Rules;

/// <summary>
/// Matches while the PC is connected to one of <see cref="Ssids"/>. The match is exact: case,
/// spaces and symbols must all be the same. With no Wi-Fi, or when Windows does not share the
/// network name (location off), it never matches.
/// </summary>
public sealed class WifiCondition : RuleCondition
{
    public override ConditionKind Kind => ConditionKind.Wifi;

    /// <summary>Network names, for example "Rumah_unifi".</summary>
    public List<string> Ssids { get; set; } = [];

    public override bool IsValid => Ssids?.Exists(ssid => !string.IsNullOrEmpty(ssid)) ?? false;

    public override bool Matches(RuleContext context) =>
        !string.IsNullOrEmpty(context.WifiSsid)
        && Ssids is not null
        && Ssids.Contains(context.WifiSsid, StringComparer.Ordinal);
}
