namespace Atap.Core.Custom;

/// <summary>How a custom value looks. Ordered from no value up to the most urgent.</summary>
public enum ValueLevel
{
    /// <summary>No usable value, or a status that matches no known word. Shown grey.</summary>
    Unknown,

    Normal,

    Warning,

    Critical,
}

/// <summary>Which side of a threshold is bad.</summary>
public enum ThresholdDirection
{
    /// <summary>High values are bad, as with disk usage or temperature. A value at the threshold counts.</summary>
    Above,

    /// <summary>Low values are bad, as with free space or battery. A value at the threshold counts.</summary>
    Below,
}

/// <summary>What is wrong with a <see cref="ThresholdSpec"/>.</summary>
public enum ThresholdProblem
{
    None,

    /// <summary>A threshold or the hysteresis is NaN or infinite.</summary>
    NotFinite,

    /// <summary>Critical is on the safe side of Warning, such as Warning 90 and Critical 80 with <see cref="ThresholdDirection.Above"/>.</summary>
    WrongOrder,

    /// <summary>The hysteresis is below zero.</summary>
    NegativeHysteresis,

    /// <summary>
    /// A threshold is 0 and <see cref="ThresholdSpec.Hysteresis"/> is not set. A share of 0 is no band at all,
    /// so the level would flap. Setting the hysteresis, even to 0, clears this.
    /// </summary>
    HysteresisRequired,
}

/// <summary>Warning and critical thresholds for a number, with a hysteresis band so the level does not flap.</summary>
/// <param name="Warning">The value where Warning starts, or null for none.</param>
/// <param name="Critical">The value where Critical starts, or null for none.</param>
/// <param name="Direction">Which side of the thresholds is bad.</param>
public sealed record ThresholdSpec(double? Warning = null, double? Critical = null, ThresholdDirection Direction = ThresholdDirection.Above)
{
    /// <summary>Share of each threshold used as its own band when <see cref="Hysteresis"/> is not set.</summary>
    public const double DefaultHysteresisShare = 0.02;

    /// <summary>
    /// How far a value must move back past a threshold before the level drops, in the value's own unit.
    /// The same band applies to both thresholds. Null gives each threshold <see cref="DefaultHysteresisShare"/>
    /// of its own size, so Warning 50 gets a band of 1 and Critical 3000 a band of 60.
    /// The Critical band never reaches past Warning. See <see cref="CriticalBand"/>.
    /// A threshold of 0 has no size, so it needs this set. See <see cref="ThresholdProblem.HysteresisRequired"/>.
    /// </summary>
    public double? Hysteresis { get; init; }

    /// <summary>True when neither threshold is set. The level is then always Normal for a valid number.</summary>
    public bool IsEmpty => Warning is null && Critical is null;

    /// <summary>The band in use for Warning, after applying the default. Never negative.</summary>
    public double WarningBand => BandFor(Warning);

    /// <summary>
    /// The band in use for Critical, after applying the default. Never negative. When Warning is set apart from Critical,
    /// the band is no wider than the gap between them, so Critical never holds past the Warning threshold.
    /// Equal thresholds act as one, so Critical keeps its full band and drops with Warning.
    /// </summary>
    public double CriticalBand =>
        Warning is double w && Critical is double c && double.IsFinite(w) && double.IsFinite(c) && w != c
            ? Math.Min(BandFor(Critical), Math.Abs(c - w))
            : BandFor(Critical);

    public ThresholdProblem Validate()
    {
        if (Warning is double w && !double.IsFinite(w)
            || Critical is double c && !double.IsFinite(c)
            || Hysteresis is double h && !double.IsFinite(h))
            return ThresholdProblem.NotFinite;
        if (Hysteresis < 0)
            return ThresholdProblem.NegativeHysteresis;
        if (Warning is double warning && Critical is double critical
            && (Direction == ThresholdDirection.Above ? critical < warning : critical > warning))
            return ThresholdProblem.WrongOrder;
        if (Hysteresis is null && (Warning == 0 || Critical == 0))
            return ThresholdProblem.HysteresisRequired;
        return ThresholdProblem.None;
    }

    private double BandFor(double? threshold)
    {
        if (Hysteresis is double set)
            return double.IsFinite(set) && set > 0 ? set : 0;
        return threshold is double t && double.IsFinite(t) ? Math.Abs(t) * DefaultHysteresisShare : 0;
    }
}

/// <summary>Pure threshold checks. The caller keeps the last level and passes it back in.</summary>
public static class Thresholds
{
    /// <summary>
    /// The level for <paramref name="value"/>. A level is entered at its threshold. It is left only once the value
    /// is more than that threshold's band back on the safe side, so a value hovering at a threshold does not flap.
    /// </summary>
    /// <param name="spec">The thresholds, checked with <see cref="ThresholdSpec.Validate"/> first. Null or empty gives Normal.</param>
    /// <param name="value">The new value. NaN or infinity gives Unknown.</param>
    /// <param name="previous">The level from the last call, or Unknown for the first one.</param>
    public static ValueLevel Evaluate(ThresholdSpec? spec, double value, ValueLevel previous = ValueLevel.Unknown)
    {
        if (!double.IsFinite(value))
            return ValueLevel.Unknown;
        if (spec is null || spec.IsEmpty)
            return ValueLevel.Normal;

        // Below is Above on the mirrored number line.
        var sign = spec.Direction == ThresholdDirection.Below ? -1.0 : 1.0;
        var v = sign * value;
        var warning = spec.Warning is double w && double.IsFinite(w) ? sign * w : (double?)null;
        var critical = spec.Critical is double c && double.IsFinite(c) ? sign * c : (double?)null;

        var entered = v >= critical ? ValueLevel.Critical
            : v >= warning ? ValueLevel.Warning
            : ValueLevel.Normal;
        if (entered >= previous)
            return entered;

        // Falling: hold the highest level, up to the previous one, whose own band still covers the value.
        if (previous == ValueLevel.Critical && v > critical - spec.CriticalBand)
            return ValueLevel.Critical;
        if (previous >= ValueLevel.Warning && v > warning - spec.WarningBand)
            return ValueLevel.Warning;
        return entered;
    }
}
