namespace Atap.Core.Commands.Query;

/// <summary>Why <see cref="UnitConverter.Parse"/> gave no conversion.</summary>
public enum ConversionError
{
    None,

    /// <summary>The query is not shaped like "5 km to mi", so it is not meant as a conversion.</summary>
    NotAConversion,

    /// <summary>The shape fits but a unit name is not in <see cref="UnitTable"/>, such as a currency.</summary>
    UnknownUnit,

    /// <summary>Both units are known but measure different things, such as km and kg.</summary>
    DifferentCategories,

    /// <summary>The result is outside the range of <see cref="decimal"/>.</summary>
    Overflow,
}

/// <summary>A finished conversion.</summary>
/// <param name="Amount">The amount typed.</param>
/// <param name="From">The unit typed first.</param>
/// <param name="Result">The amount in <paramref name="To"/>, unrounded.</param>
/// <param name="To">The target unit.</param>
public sealed record UnitConversion(decimal Amount, UnitDefinition From, decimal Result, UnitDefinition To)
{
    /// <summary>Text such as "5 km = 3.10685596118667 mi", rounded by <see cref="QueryNumber.Round"/>.</summary>
    public string Format(IFormatProvider? provider = null) =>
        $"{QueryNumber.Format(Amount, provider)} {From.Symbol} = {QueryNumber.Format(Result, provider)} {To.Symbol}";
}

/// <summary>The outcome of <see cref="UnitConverter.Parse"/>.</summary>
/// <param name="Conversion">The conversion, or null when <paramref name="Error"/> is not None.</param>
/// <param name="Error">What went wrong, or None.</param>
/// <param name="UnknownUnit">The unit text that was not found, for <see cref="ConversionError.UnknownUnit"/>.</param>
public readonly record struct ConversionResult(UnitConversion? Conversion, ConversionError Error, string? UnknownUnit = null)
{
    public bool Success => Error == ConversionError.None;
}

/// <summary>
/// Reads queries such as "5 km to mi", "5km in mi", "100 °C ke °F" or "2,000 sq ft dalam m²" and converts them.
/// </summary>
/// <remarks>
/// The amount is a plain number with a dot for decimals and optional comma groups, and may carry a sign.
/// Then come the unit, a connector and the target unit. Connectors are "to", "in", "into", "ke", "dalam", "=", "->" and "→".
/// Because "in" is also the inch, every connector position is tried from left to right
/// and the first one with a known unit on both sides wins. So "5 in in cm" works.
/// </remarks>
public static class UnitConverter
{
    /// <summary>Longest query that is read.</summary>
    public const int MaxLength = 128;

    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "to", "in", "into", "ke", "dalam", "=", "->", "→",
    };

    /// <summary>Reads and converts <paramref name="query"/>. Never throws.</summary>
    public static ConversionResult Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > MaxLength)
            return new ConversionResult(null, ConversionError.NotAConversion);

        var text = query.AsSpan().Trim();
        var negative = false;
        var signLength = 0;
        if (text.Length > 0 && text[0] is '+' or '-' or '−')
        {
            negative = text[0] != '+';
            signLength = 1;
        }

        var numberLength = Calculator.NumberLength(text[signLength..]);
        if (numberLength == 0
            || Calculator.TryParseNumber(text.Slice(signLength, numberLength), out var amount) != CalcError.None)
            return new ConversionResult(null, ConversionError.NotAConversion);
        if (negative)
            amount = -amount;

        var tokens = SplitTokens(text[(signLength + numberLength)..].ToString());
        if (tokens.Length < 3)
            return new ConversionResult(null, ConversionError.NotAConversion);

        var sawConnector = false;
        var differentCategories = false;
        string? unknown = null;
        for (var i = 1; i < tokens.Length - 1; i++)
        {
            if (!Connectors.Contains(tokens[i]))
                continue;
            sawConnector = true;

            var fromText = string.Join(' ', tokens, 0, i);
            var toText = string.Join(' ', tokens, i + 1, tokens.Length - i - 1);
            var from = UnitTable.Find(fromText);
            var to = UnitTable.Find(toText);
            if (from is null || to is null)
            {
                unknown ??= from is null ? fromText : toText;
                continue;
            }

            if (from.Category != to.Category)
            {
                differentCategories = true;
                continue;
            }

            return TryConvert(amount, from, to, out var result)
                ? new ConversionResult(new UnitConversion(amount, from, result, to), ConversionError.None)
                : new ConversionResult(null, ConversionError.Overflow);
        }

        if (!sawConnector)
            return new ConversionResult(null, ConversionError.NotAConversion);
        if (differentCategories)
            return new ConversionResult(null, ConversionError.DifferentCategories);
        return new ConversionResult(null, ConversionError.UnknownUnit, unknown);
    }

    /// <summary>
    /// Converts <paramref name="amount"/> between two units of one category.
    /// Returns false when the categories differ or the result is outside the range of <see cref="decimal"/>.
    /// </summary>
    public static bool TryConvert(decimal amount, UnitDefinition from, UnitDefinition to, out decimal result)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        result = 0;
        if (from.Category != to.Category)
            return false;
        if (ReferenceEquals(from, to))
        {
            result = amount;
            return true;
        }

        try
        {
            // One division at the end keeps fractional factors such as 1000/3600 exact for as long as possible.
            var numerator = (amount + from.Offset) * from.Numerator * to.Denominator;
            var denominator = from.Denominator * to.Numerator;
            result = numerator / denominator - to.Offset;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Splits on spaces, with "=", "->" and "→" always standing alone even when typed without spaces.</summary>
    private static string[] SplitTokens(string text)
    {
        var spaced = text.Replace("->", " -> ", StringComparison.Ordinal)
            .Replace("→", " → ", StringComparison.Ordinal)
            .Replace("=", " = ", StringComparison.Ordinal);
        return spaced.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }
}
