using System.Text;

namespace Atap.Core.Productivity;

/// <summary>Cleans text typed into the productivity widgets before it is stored or shown.</summary>
public static class ProductivityText
{
    /// <summary>
    /// One line of text. Control characters become spaces, direction overrides are removed, the
    /// ends are trimmed and the result is cut to <paramref name="maxLength"/> characters.
    /// </summary>
    public static string CleanLine(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (IsDirectionControl(c))
                continue;
            builder.Append(char.IsControl(c) ? ' ' : c);
        }
        return Cut(builder.ToString().Trim(), maxLength).TrimEnd();
    }

    /// <summary>
    /// Text that may span lines. Line breaks and tabs stay, other control characters and direction
    /// overrides are removed, and the result is cut to <paramref name="maxLength"/> characters.
    /// </summary>
    public static string CleanText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (IsDirectionControl(c) || (char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                continue;
            builder.Append(c);
        }
        return Cut(builder.ToString(), maxLength);
    }

    /// <summary>Cuts to at most <paramref name="maxLength"/> characters without splitting a surrogate pair.</summary>
    public static string Cut(string text, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        if (text.Length <= maxLength)
            return text;

        var end = maxLength;
        if (char.IsHighSurrogate(text[end - 1]))
            end--;
        return text[..end];
    }

    private static bool IsDirectionControl(char c) =>
        c is (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069);
}
