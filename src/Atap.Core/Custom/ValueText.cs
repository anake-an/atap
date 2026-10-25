using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Atap.Core.Custom;

/// <summary>Number parsing, number formatting and text cleaning shared by the value templates.</summary>
internal static class ValueText
{
    /// <summary>Longest text shown for one value or label, in UTF-16 characters, the ellipsis included.</summary>
    public const int MaxLength = 200;

    private const char Ellipsis = '…';

    /// <summary>
    /// Makes remote text safe for one line on the notch. Control characters and line breaks become spaces,
    /// runs of spaces collapse, direction overrides are removed, lone surrogates become U+FFFD,
    /// and long text is cut with an ellipsis.
    /// </summary>
    public static string Clean(string? text, int maxLength = MaxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, maxLength + 2));
        var pendingSpace = false;
        for (var i = 0; i < text.Length && builder.Length <= maxLength; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (IsDirectionControl(c))
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[i + 1]);
                i++;
            }
            else
            {
                builder.Append(char.IsSurrogate(c) ? '\uFFFD' : c);
            }
        }

        if (builder.Length <= maxLength)
            return builder.ToString();

        // Cut one short of the cap for the ellipsis, without splitting a surrogate pair.
        var cut = maxLength - 1;
        if (cut > 0 && char.IsHighSurrogate(builder[cut - 1]))
            cut--;
        return builder.ToString(0, cut).TrimEnd() + Ellipsis;
    }

    /// <summary>Cleans a unit setting. Returns null when nothing is left.</summary>
    public static string? CleanUnit(string? unit)
    {
        var cleaned = Clean(unit);
        if (cleaned.Length > ValueTemplateSpec.MaxUnitLength)
        {
            var cut = ValueTemplateSpec.MaxUnitLength;
            if (char.IsHighSurrogate(cleaned[cut - 1]))
                cut--;
            cleaned = cleaned[..cut].TrimEnd();
        }
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Puts a unit after a value. Units that start with a letter or digit get a space, symbols such as % and ° do not.</summary>
    public static string JoinUnit(string text, string? unit)
    {
        if (string.IsNullOrEmpty(unit) || text.Length == 0)
            return text;
        return char.IsLetterOrDigit(unit[0]) ? text + " " + unit : text + unit;
    }

    /// <summary>
    /// Reads a number from a JSON number or from a string such as "72.5", "-3", "1e3" or "72%".
    /// Strings use the invariant format, so "1,5" is not a number. NaN and infinity are refused.
    /// </summary>
    public static bool TryGetNumber(JsonElement element, out double number)
    {
        number = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out number) && double.IsFinite(number),
            JsonValueKind.String => TryParseNumber(JsonText.GetString(element), out number),
            _ => false,
        };
    }

    public static bool TryParseNumber(string? text, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var span = text.AsSpan().Trim();
        if (span.EndsWith("%", StringComparison.Ordinal))
            span = span[..^1].TrimEnd();
        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
    }

    /// <summary>
    /// Formats a number in the UI culture. Fixed <paramref name="decimals"/> use grouping and that many places.
    /// Null shows up to 2 places without trailing zeros. Very large values use scientific notation.
    /// </summary>
    public static string FormatNumber(double value, int? decimals, IFormatProvider format)
    {
        var places = decimals is int set ? Math.Clamp(set, 0, ValueTemplateSpec.MaxDecimals) : 2;
        if (Math.Abs(value) >= 1e15)
            return value.ToString("0.###E+0", format);

        var rounded = Math.Round(value, places, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            rounded = 0; // Drops the sign of negative zero.
        return decimals is null
            ? rounded.ToString("#,0.##", format)
            : rounded.ToString("N" + places.ToString(CultureInfo.InvariantCulture), format);
    }

    /// <summary>Display text for a plain JSON value, or null for an object, array, null or undefined value.</summary>
    public static string? Display(JsonElement element, int? decimals, IFormatProvider format) => element.ValueKind switch
    {
        JsonValueKind.String => Clean(JsonText.GetString(element)),
        JsonValueKind.Number => element.TryGetDouble(out var number) && double.IsFinite(number)
            ? FormatNumber(number, decimals, format)
            : Clean(element.GetRawText()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    /// <summary>The text a status word is matched against. Numbers use the invariant format.</summary>
    public static string? StatusText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => Clean(JsonText.GetString(element)),
        JsonValueKind.Number => Clean(element.GetRawText()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static bool IsDirectionControl(char c) =>
        c is '\u061C' or '\u200E' or '\u200F' or '\uFEFF'
        || c is >= '\u202A' and <= '\u202E'
        || c is >= '\u2066' and <= '\u2069';
}
