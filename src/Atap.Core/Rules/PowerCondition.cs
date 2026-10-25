namespace Atap.Core.Rules;

/// <summary>The power state a <see cref="PowerCondition"/> looks for.</summary>
public enum PowerConditionState
{
    /// <summary>Running on battery.</summary>
    OnBattery,

    /// <summary>On mains power. A desktop with no battery is always plugged in.</summary>
    PluggedIn,

    /// <summary>Windows energy saver is on.</summary>
    EnergySaver,

    /// <summary>The battery charge is below <see cref="PowerCondition.Percent"/>, plugged in or not.</summary>
    BatteryBelow,
}

/// <summary>Matches on battery, plugged in, while Windows energy saver is on, or below a battery level.</summary>
public sealed class PowerCondition : RuleCondition
{
    public override ConditionKind Kind => ConditionKind.Power;

    public PowerConditionState State { get; set; }

    /// <summary>The level for <see cref="PowerConditionState.BatteryBelow"/>, 1 to 100. Ignored for the other states.</summary>
    public int? Percent { get; set; }

    public override bool IsValid => State switch
    {
        PowerConditionState.OnBattery or PowerConditionState.PluggedIn or PowerConditionState.EnergySaver => true,
        PowerConditionState.BatteryBelow => Percent is >= 1 and <= 100,
        _ => false,
    };

    public override bool Matches(RuleContext context) => IsValid && State switch
    {
        PowerConditionState.OnBattery => context.OnBattery,
        PowerConditionState.PluggedIn => !context.OnBattery,
        PowerConditionState.EnergySaver => context.EnergySaverOn,
        // No battery, or Windows does not report a level: never below.
        PowerConditionState.BatteryBelow => context.BatteryPercent is { } percent && percent < Percent,
        _ => false,
    };
}
