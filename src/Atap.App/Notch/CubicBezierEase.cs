using System.Windows.Media.Animation;

namespace Atap.Notch;

/// <summary>
/// A cubic Bezier timing curve, as CSS <c>cubic-bezier(x1, y1, x2, y2)</c>. <see cref="NotchSpring"/> is the notch's
/// gentle spring: it starts quickly, goes past the end by less than half a percent and settles.
/// It is worked out only while an animation runs.
/// </summary>
/// <remarks>WPF key splines only take control points between 0 and 1, so they cannot overshoot.</remarks>
internal sealed class CubicBezierEase : IEasingFunction
{
    /// <summary>The website's notch curve, <c>cubic-bezier(.22, 1.12, .36, 1)</c>.</summary>
    public static readonly CubicBezierEase NotchSpring = new(0.22, 1.12, 0.36, 1);

    private const double Precision = 1e-7;

    private readonly double _x1;
    private readonly double _y1;
    private readonly double _x2;
    private readonly double _y2;

    /// <param name="x1">First control point, time. Between 0 and 1.</param>
    /// <param name="y1">First control point, progress. May go past 1 for an overshoot.</param>
    /// <param name="x2">Second control point, time. Between 0 and 1.</param>
    /// <param name="y2">Second control point, progress.</param>
    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        _x1 = Math.Clamp(x1, 0, 1);
        _y1 = y1;
        _x2 = Math.Clamp(x2, 0, 1);
        _y2 = y2;
    }

    /// <summary>Progress at a point in time, both from 0 to 1. Progress may go past 1 for a moment.</summary>
    public double Ease(double normalizedTime)
    {
        if (double.IsNaN(normalizedTime) || normalizedTime <= 0)
            return 0;
        if (normalizedTime >= 1)
            return 1;
        return Bezier(SolveForTime(normalizedTime), _y1, _y2);
    }

    // One coordinate of the curve from (0, 0) to (1, 1) at parameter s.
    private static double Bezier(double s, double p1, double p2)
    {
        var u = 1 - s;
        return (3 * u * u * s * p1) + (3 * u * s * s * p2) + (s * s * s);
    }

    private static double Slope(double s, double p1, double p2)
    {
        var u = 1 - s;
        return (3 * u * u * p1) + (6 * u * s * (p2 - p1)) + (3 * s * s * (1 - p2));
    }

    /// <summary>The curve parameter where the time coordinate is <paramref name="time"/>. Newton steps, then halving if they stall.</summary>
    private double SolveForTime(double time)
    {
        var s = time;
        for (var i = 0; i < 8; i++)
        {
            var error = Bezier(s, _x1, _x2) - time;
            if (Math.Abs(error) < Precision)
                return s;
            var slope = Slope(s, _x1, _x2);
            if (Math.Abs(slope) < 1e-6)
                break;
            s -= error / slope;
        }

        // The time coordinate only rises when both time control points are between 0 and 1.
        double low = 0, high = 1;
        s = time;
        while (high - low > Precision)
        {
            if (Bezier(s, _x1, _x2) < time)
                low = s;
            else
                high = s;
            s = (low + high) / 2;
        }
        return s;
    }
}
