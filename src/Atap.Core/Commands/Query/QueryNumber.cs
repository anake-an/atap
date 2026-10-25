using System.Globalization;

namespace Atap.Core.Commands.Query;

/// <summary>Rounding and text for calculator and unit results.</summary>
public static class QueryNumber
{
    /// <summary>Significant digits kept for a value with a fractional part.</summary>
    public const int SignificantDigits = 15;

    private const string Fraction = "############################";
    private const string GroupedPattern = "#,##0." + Fraction;
    private const string PlainPattern = "0." + Fraction;

    /// <summary>
    /// Rounds away the noise of decimal division, so 1/3*3 shows as 1.
    /// A whole number is kept as it is. Anything else keeps <see cref="SignificantDigits"/> significant digits,
    /// but never loses its whole part.
    /// </summary>
    public static decimal Round(decimal value)
    {
        if (value == decimal.Truncate(value))
            return value == 0 ? 0m : value;

        var magnitude = Math.Abs(value);
        int decimals;
        if (magnitude >= 1)
        {
            decimals = Math.Max(0, SignificantDigits - IntegerDigits(magnitude));
        }
        else
        {
            var zeros = 0;
            while (magnitude < 0.1m)
            {
                magnitude *= 10;
                zeros++;
            }

            decimals = Math.Min(28, SignificantDigits + zeros);
        }

        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0m : rounded;
    }

    /// <summary>
    /// Formats a value after <see cref="Round"/>, with no trailing zeros.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="provider">Separators to use. Invariant when null. The App passes the UI culture (en-MY or ms-MY).</param>
    /// <param name="grouping">Whether to group thousands. Off gives text that pastes cleanly into other apps.</param>
    public static string Format(decimal value, IFormatProvider? provider = null, bool grouping = true) =>
        Round(value).ToString(grouping ? GroupedPattern : PlainPattern, provider ?? CultureInfo.InvariantCulture);

    private static int IntegerDigits(decimal magnitude)
    {
        var whole = decimal.Truncate(magnitude);
        var digits = 0;
        while (whole >= 1)
        {
            whole = decimal.Truncate(whole / 10);
            digits++;
        }

        return digits;
    }
}
