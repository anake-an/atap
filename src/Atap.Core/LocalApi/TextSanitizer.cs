using System.Buffers;
using System.Globalization;
using System.Text;

namespace Atap.Core.LocalApi;

/// <summary>
/// Cleans text that came from outside Atap (pushes, alerts, process labels) before it is shown on the notch.
/// </summary>
public static class TextSanitizer
{
    /// <summary>
    /// Returns <paramref name="text"/> as one clean line:
    /// <list type="bullet">
    /// <item>Line breaks, tabs and other whitespace become one space, and runs of whitespace collapse.</item>
    /// <item>Other control characters (Cc) are removed.</item>
    /// <item>Bidi controls are removed: U+202A to U+202E, U+2066 to U+2069, U+200E, U+200F and U+061C.
    /// They could flip how the rest of the text reads.</item>
    /// <item>U+FEFF and lone surrogates are removed.</item>
    /// <item>Leading and trailing whitespace is trimmed.</item>
    /// </list>
    /// Null gives an empty string. Joiners (U+200C, U+200D) stay, since some scripts and emoji need them.
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (IsClean(text))
            return text;

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        var index = 0;
        while (index < text.Length)
        {
            var start = index;
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            index += consumed;
            if (status != OperationStatus.Done)
                continue;

            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (IsRemoved(rune))
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(text.AsSpan(start, consumed));
        }
        return builder.ToString();
    }

    /// <summary>Number of Unicode scalar values in <paramref name="text"/>. This is how text limits are counted.</summary>
    public static int CountCharacters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var count = 0;
        foreach (var _ in text.EnumerateRunes())
            count++;
        return count;
    }

    /// <summary>True for a rune <see cref="Sanitize"/> drops outright (whitespace is handled before this).</summary>
    private static bool IsRemoved(Rune rune)
    {
        var value = rune.Value;
        return Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control
            || value is >= 0x202A and <= 0x202E
            || value is >= 0x2066 and <= 0x2069
            || value is 0x200E or 0x200F or 0x061C or 0xFEFF;
    }

    /// <summary>Fast path: true when <see cref="Sanitize"/> would return the text unchanged.</summary>
    private static bool IsClean(string text)
    {
        if (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
            return false;

        var previousWasSpace = false;
        foreach (var c in text)
        {
            if (char.IsSurrogate(c))
                return false;   // Let the slow path pair them and drop lone ones.
            if (char.IsWhiteSpace(c))
            {
                if (c != ' ' || previousWasSpace)
                    return false;
                previousWasSpace = true;
                continue;
            }
            previousWasSpace = false;
            if (IsRemoved(new Rune(c)))
                return false;
        }
        return true;
    }
}
