using Atap.Core.Custom;

namespace Atap.Core.Tests.Custom;

public class ThresholdsTests
{
    private static ValueLevel[] Run(ThresholdSpec spec, params double[] values)
    {
        var levels = new ValueLevel[values.Length];
        var previous = ValueLevel.Unknown;
        for (var i = 0; i < values.Length; i++)
            levels[i] = previous = Thresholds.Evaluate(spec, values[i], previous);
        return levels;
    }

    private static int Changes(IReadOnlyList<ValueLevel> levels) =>
        levels.Zip(levels.Skip(1)).Count(pair => pair.First != pair.Second);

    [Fact]
    public void Above_EntersAtTheThresholdAndLeavesOnlyPastTheBand()
    {
        var spec = new ThresholdSpec(Warning: 80, Critical: 90) { Hysteresis = 2 };

        var levels = Run(spec, 79, 80, 79.5, 78.5, 77.9, 79, 80, 90, 89, 88.5, 87.9, 50);

        Assert.Equal(
            [
                ValueLevel.Normal, ValueLevel.Warning, ValueLevel.Warning, ValueLevel.Warning, ValueLevel.Normal, ValueLevel.Normal,
                ValueLevel.Warning, ValueLevel.Critical, ValueLevel.Critical, ValueLevel.Critical, ValueLevel.Warning, ValueLevel.Normal,
            ],
            levels);
    }

    [Fact]
    public void Below_MirrorsAbove()
    {
        // Battery: warn at 20 % and below, critical at 10 % and below.
        var spec = new ThresholdSpec(Warning: 20, Critical: 10, ThresholdDirection.Below) { Hysteresis = 1 };

        var levels = Run(spec, 50, 20, 20.5, 21.5, 10, 10.5, 11.5, 30);

        Assert.Equal(
            [
                ValueLevel.Normal, ValueLevel.Warning, ValueLevel.Warning, ValueLevel.Normal,
                ValueLevel.Critical, ValueLevel.Critical, ValueLevel.Warning, ValueLevel.Normal,
            ],
            levels);
    }

    [Fact]
    public void ValueHoveringAtAThreshold_DoesNotFlap()
    {
        var spec = new ThresholdSpec(Warning: 80, Critical: 90);
        var values = Enumerable.Range(0, 200).Select(i => 80 + 0.6 * Math.Sin(i)).Prepend(70).ToArray();

        var levels = Run(spec, values);

        Assert.Equal(1, Changes(levels));
        Assert.Equal(ValueLevel.Warning, levels[^1]);
    }

    [Fact]
    public void ValueHoveringAtCritical_DoesNotFlap()
    {
        var spec = new ThresholdSpec(Warning: 80, Critical: 90);
        var values = Enumerable.Range(0, 200).Select(i => 90 + 0.6 * Math.Cos(i)).Prepend(95).ToArray();

        var levels = Run(spec, values);

        Assert.Equal(0, Changes(levels));
        Assert.All(levels, level => Assert.Equal(ValueLevel.Critical, level));
    }

    [Fact]
    public void ZeroBand_FlapsSoTheDefaultBandMatters()
    {
        var values = Enumerable.Range(0, 200).Select(i => 80 + 0.6 * Math.Sin(i)).ToArray();

        var withoutBand = Run(new ThresholdSpec(Warning: 80) { Hysteresis = 0 }, values);

        Assert.True(Changes(withoutBand) > 30);
    }

    [Fact]
    public void Band_DefaultsToTwoPercentOfEachThreshold()
    {
        var spec = new ThresholdSpec(Warning: 80, Critical: 90);
        Assert.Equal(1.6, spec.WarningBand, 10);
        Assert.Equal(1.8, spec.CriticalBand, 10);

        var below = new ThresholdSpec(Warning: 20, Critical: 10, ThresholdDirection.Below);
        Assert.Equal(0.4, below.WarningBand, 10);
        Assert.Equal(0.2, below.CriticalBand, 10);

        Assert.Equal(2.0, new ThresholdSpec(Warning: -100).WarningBand, 10);
        Assert.Equal(0, new ThresholdSpec(Warning: -100).CriticalBand);
        Assert.Equal(0, new ThresholdSpec(Warning: 0).WarningBand);
    }

    [Fact]
    public void Band_SetHysteresisAppliesToBothThresholds()
    {
        var spec = new ThresholdSpec(Warning: 50, Critical: 3000) { Hysteresis = 5 };

        Assert.Equal(5, spec.WarningBand);
        Assert.Equal(5, spec.CriticalBand);
        Assert.Equal(0, new ThresholdSpec(Warning: 80) { Hysteresis = -1 }.WarningBand);
        Assert.Equal(0, new ThresholdSpec(Warning: 80) { Hysteresis = double.NaN }.WarningBand);
    }

    [Fact]
    public void FarApartThresholds_LowerLevelStillClears()
    {
        // Ping in ms. Critical's band of 60 must not hold Warning, whose own band is 1.
        var ping = Run(new ThresholdSpec(Warning: 50, Critical: 3000), 60, 30, 10, 0, 49.5, 50, 49.2, 48.9);

        Assert.Equal(
            [
                ValueLevel.Warning, ValueLevel.Normal, ValueLevel.Normal, ValueLevel.Normal,
                ValueLevel.Normal, ValueLevel.Warning, ValueLevel.Warning, ValueLevel.Normal,
            ],
            ping);

        // Bytes: warn at 1 GB, critical at 100 GB.
        var bytes = Run(new ThresholdSpec(Warning: 1e9, Critical: 1e11), 1.5e9, 0.5e9, 1e11, 9.9e10, 9.7e10, 0);

        Assert.Equal(
            [ValueLevel.Warning, ValueLevel.Normal, ValueLevel.Critical, ValueLevel.Critical, ValueLevel.Warning, ValueLevel.Normal],
            bytes);
    }

    [Fact]
    public void FarApartThresholds_Below_CriticalClearsCloseToItsThreshold()
    {
        var spec = new ThresholdSpec(Warning: 1000, Critical: 5, ThresholdDirection.Below);

        var levels = Run(spec, 5, 5.05, 25, 1001, 1015, 1021);

        Assert.Equal(
            [ValueLevel.Critical, ValueLevel.Critical, ValueLevel.Warning, ValueLevel.Warning, ValueLevel.Warning, ValueLevel.Normal],
            levels);
    }

    [Fact]
    public void CriticalBand_StopsAtWarning()
    {
        // Close thresholds: the default Critical band of 1.98 would hold Critical down to 97.02, below Warning.
        var close = new ThresholdSpec(Warning: 98, Critical: 99);

        Assert.Equal(1, close.CriticalBand, 10);
        Assert.Equal(1.96, close.WarningBand, 10);
        Assert.Equal(ValueLevel.Critical, Thresholds.Evaluate(close, 98.5, ValueLevel.Critical));
        Assert.Equal(ValueLevel.Warning, Thresholds.Evaluate(close, 97.5, ValueLevel.Critical));
        Assert.Equal(ValueLevel.Warning, Thresholds.Evaluate(close, 98, ValueLevel.Critical));

        // A set band wider than the gap.
        var wide = new ThresholdSpec(Warning: 80, Critical: 90) { Hysteresis = 20 };

        Assert.Equal(10, wide.CriticalBand);
        Assert.Equal(20, wide.WarningBand);
        Assert.Equal(ValueLevel.Critical, Thresholds.Evaluate(wide, 81, ValueLevel.Critical));
        Assert.Equal(ValueLevel.Warning, Thresholds.Evaluate(wide, 79, ValueLevel.Critical));
        Assert.Equal(ValueLevel.Warning, Thresholds.Evaluate(wide, 61, ValueLevel.Critical));

        var below = new ThresholdSpec(Warning: 20, Critical: 15, ThresholdDirection.Below) { Hysteresis = 10 };

        Assert.Equal(5, below.CriticalBand);
        Assert.Equal(ValueLevel.Critical, Thresholds.Evaluate(below, 19, ValueLevel.Critical));
        Assert.Equal(ValueLevel.Warning, Thresholds.Evaluate(below, 21, ValueLevel.Critical));
    }

    [Fact]
    public void EqualThresholds_KeepTheFullBandAndDoNotFlap()
    {
        var spec = new ThresholdSpec(Warning: 80, Critical: 80);
        var values = Enumerable.Range(0, 200).Select(i => 80 + 0.6 * Math.Sin(i)).Prepend(70).ToArray();

        var levels = Run(spec, values);

        Assert.Equal(1.6, spec.CriticalBand, 10);
        Assert.Equal(1, Changes(levels));
        Assert.Equal(ValueLevel.Critical, levels[^1]);
        Assert.Equal(ValueLevel.Normal, Thresholds.Evaluate(spec, 78, ValueLevel.Critical));
    }

    [Theory]
    [InlineData(ThresholdDirection.Above)]
    [InlineData(ThresholdDirection.Below)]
    public void ZeroThreshold_NeedsASetBand(ThresholdDirection direction)
    {
        Assert.Equal(ThresholdProblem.HysteresisRequired, new ThresholdSpec(Warning: 0, Direction: direction).Validate());
        Assert.Equal(ThresholdProblem.HysteresisRequired, new ThresholdSpec(Critical: 0, Direction: direction).Validate());
        Assert.Equal(ThresholdProblem.None, new ThresholdSpec(Warning: 0, Direction: direction) { Hysteresis = 0.5 }.Validate());
        Assert.Equal(ThresholdProblem.None, new ThresholdSpec(Warning: 0, Direction: direction) { Hysteresis = 0 }.Validate());
    }

    [Fact]
    public void ZeroThreshold_WithASetBand_DoesNotFlap()
    {
        var below = Run(new ThresholdSpec(Warning: 0, Direction: ThresholdDirection.Below) { Hysteresis = 0.5 }, 0, 0.1, 0, 0.1, 0, 0.1);
        var above = Run(new ThresholdSpec(Warning: 0) { Hysteresis = 0.5 }, -0.1, 0, -0.1, 0, -0.1, 0);

        Assert.All(below, level => Assert.Equal(ValueLevel.Warning, level));
        Assert.Equal(1, Changes(above));
        Assert.Equal(ValueLevel.Warning, above[^1]);
    }

    [Fact]
    public void FirstValue_HasNoHysteresis()
    {
        var spec = new ThresholdSpec(Warning: 80) { Hysteresis = 5 };

        Assert.Equal(ValueLevel.Normal, Thresholds.Evaluate(spec, 79));
        Assert.Equal(ValueLevel.Warning, Thresholds.Evaluate(spec, 79, ValueLevel.Warning));
        Assert.Equal(ValueLevel.Normal, Thresholds.Evaluate(spec, 79, ValueLevel.Normal));
    }

    [Fact]
    public void JumpsStraightToCritical()
    {
        var spec = new ThresholdSpec(Warning: 80, Critical: 90);

        Assert.Equal(ValueLevel.Critical, Thresholds.Evaluate(spec, 99, ValueLevel.Normal));
    }

    [Fact]
    public void OnlyCritical_SkipsWarning()
    {
        var spec = new ThresholdSpec(Critical: 90);

        var levels = Run(spec, 50, 95, 89, 88);

        Assert.Equal([ValueLevel.Normal, ValueLevel.Critical, ValueLevel.Critical, ValueLevel.Normal], levels);
    }

    [Fact]
    public void FallingFromCritical_PastBothBands_GoesToNormal()
    {
        var spec = new ThresholdSpec(Warning: 80, Critical: 90) { Hysteresis = 2 };

        Assert.Equal(ValueLevel.Normal, Thresholds.Evaluate(spec, 10, ValueLevel.Critical));
        Assert.Equal(ValueLevel.Warning, Thresholds.Evaluate(spec, 79, ValueLevel.Critical));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NotFiniteValue_IsUnknown(double value)
    {
        Assert.Equal(ValueLevel.Unknown, Thresholds.Evaluate(new ThresholdSpec(Warning: 1), value, ValueLevel.Warning));
    }

    [Fact]
    public void NoThresholds_IsNormal()
    {
        Assert.Equal(ValueLevel.Normal, Thresholds.Evaluate(null, 1e9));
        Assert.Equal(ValueLevel.Normal, Thresholds.Evaluate(new ThresholdSpec(), 1e9, ValueLevel.Critical));
        Assert.True(new ThresholdSpec().IsEmpty);
    }

    [Theory]
    [InlineData(80.0, 90.0, ThresholdDirection.Above, ThresholdProblem.None)]
    [InlineData(90.0, 80.0, ThresholdDirection.Above, ThresholdProblem.WrongOrder)]
    [InlineData(20.0, 10.0, ThresholdDirection.Below, ThresholdProblem.None)]
    [InlineData(10.0, 20.0, ThresholdDirection.Below, ThresholdProblem.WrongOrder)]
    [InlineData(80.0, 80.0, ThresholdDirection.Above, ThresholdProblem.None)]
    [InlineData(double.NaN, 80.0, ThresholdDirection.Above, ThresholdProblem.NotFinite)]
    [InlineData(80.0, double.PositiveInfinity, ThresholdDirection.Above, ThresholdProblem.NotFinite)]
    [InlineData(0.0, 10.0, ThresholdDirection.Above, ThresholdProblem.HysteresisRequired)]
    [InlineData(10.0, 0.0, ThresholdDirection.Below, ThresholdProblem.HysteresisRequired)]
    [InlineData(10.0, 0.0, ThresholdDirection.Above, ThresholdProblem.WrongOrder)]
    public void Validate_ChecksOrderAndNumbers(double warning, double critical, ThresholdDirection direction, ThresholdProblem expected)
    {
        Assert.Equal(expected, new ThresholdSpec(warning, critical, direction).Validate());
    }

    [Fact]
    public void Validate_ChecksHysteresis()
    {
        Assert.Equal(ThresholdProblem.NegativeHysteresis, new ThresholdSpec(Warning: 80) { Hysteresis = -1 }.Validate());
        Assert.Equal(ThresholdProblem.NotFinite, new ThresholdSpec(Warning: 80) { Hysteresis = double.NaN }.Validate());
        Assert.Equal(ThresholdProblem.None, new ThresholdSpec(Warning: 80) { Hysteresis = 0 }.Validate());
    }
}
