using Atap.Core.Placement;
using Atap.Notch;

namespace Atap.Tests.Notch;

public class CubicBezierEaseTests
{
    private static readonly CubicBezierEase Spring = CubicBezierEase.NotchSpring;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-0.5, 0)]
    [InlineData(1.5, 1)]
    [InlineData(double.NaN, 0)]
    public void Ease_StartsAtZeroAndEndsAtOne(double time, double progress)
    {
        Assert.Equal(progress, Spring.Ease(time));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(0.85)]
    public void Ease_MatchesTheCssCurve(double s)
    {
        // A point on cubic-bezier(.22, 1.12, .36, 1), worked out from the control points directly.
        var u = 1 - s;
        var time = (3 * u * u * s * 0.22) + (3 * u * s * s * 0.36) + (s * s * s);
        var progress = (3 * u * u * s * 1.12) + (3 * u * s * s * 1) + (s * s * s);

        Assert.Equal(progress, Spring.Ease(time), 1e-5);
    }

    [Fact]
    public void Ease_IsAGentleSpring()
    {
        var samples = Enumerable.Range(0, 1001).Select(i => Spring.Ease(i / 1000.0)).ToList();
        var peak = samples.Max();

        // Quick at first, then a slight overshoot that settles back to the end.
        Assert.True(Spring.Ease(0.3) > 0.85, "The curve starts too slowly.");
        Assert.InRange(peak, 1.001, 1.01);
        Assert.Equal(1, samples[^1]);
    }

    [Fact]
    public void Overshoot_FitsInTheShoulderRoom()
    {
        var peak = Enumerable.Range(0, 1001).Max(i => Spring.Ease(i / 1000.0));
        var change = NotchShape.ExpandedShoulderRadius - NotchShape.IdleShoulderRadius;

        Assert.True(NotchShape.IdleShoulderRadius + (change * peak) <= NotchShape.ShoulderRoom);
    }
}
