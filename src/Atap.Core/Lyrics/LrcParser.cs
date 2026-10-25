using System.Globalization;
using System.Text;

namespace Atap.Core.Lyrics;

/// <summary>
/// Reads LRC lyrics, the format LRCLIB returns in <c>syncedLyrics</c>. Parsing never throws.
/// Lines it cannot read are skipped.
/// </summary>
/// <remarks>
/// <para>
/// Timestamps: <c>[mm:ss]</c>, <c>[mm:ss.x]</c> up to <c>[mm:ss.xxx]</c> (more digits are cut to milliseconds),
/// <c>[mm:ss:xx]</c> and <c>[mm:ss,xx]</c>. Minutes may have up to 5 digits. One line may carry several timestamps,
/// and it is then repeated at each of them.
/// </para>
/// <para>
/// Tags such as <c>[ti:...]</c> are read from lines without timestamps. <c>[offset:+500]</c> moves every line
/// 500 ms sooner, wherever the tag sits in the file. Word timings in angle brackets are removed from the text.
/// </para>
/// </remarks>
public static class LrcParser
{
    /// <summary>Timed lines kept at most. Songs have a few hundred, so this only stops runaway input.</summary>
    public const int MaxLines = 5000;

    /// <summary>Tags kept at most.</summary>
    private const int MaxTags = 64;

    /// <summary>Longest tag name read.</summary>
    private const int MaxTagNameLength = 32;

    /// <summary>Longest word timing read between angle brackets, such as <c>12345:59.999</c>.</summary>
    private const int MaxWordTimeLength = 16;

    /// <summary>Largest offset accepted, in milliseconds. A larger one is ignored.</summary>
    private const long MaxOffsetMilliseconds = 3_600_000;

    /// <summary>Parses LRC text. Null, empty or untimed text gives a document with no lines.</summary>
    public static LrcDocument Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return LrcDocument.Empty;

        var timed = new List<TimedText>();
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        var stamps = new List<TimeSpan>();

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            ReadLine(line, timed, tags, stamps);
            if (timed.Count >= MaxLines)
                break;
        }

        // Sort with the file order as tie-break, since List.Sort is not stable. The offset moves every line
        // by the same amount afterwards, so the order holds. Lines clamped at zero keep their order from before.
        timed.Sort(static (a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Order.CompareTo(b.Order));

        var offset = ReadOffset(tags);
        var lines = new LrcLine[timed.Count];
        for (var i = 0; i < timed.Count; i++)
        {
            var time = timed[i].Time - offset;
            lines[i] = new LrcLine(time < TimeSpan.Zero ? TimeSpan.Zero : time, timed[i].Text);
        }

        return new LrcDocument(lines, tags, offset);
    }

    /// <summary>
    /// Reads one LRC time such as <c>01:02.34</c>, without the brackets. Surrounding spaces are allowed.
    /// </summary>
    public static bool TryParseTime(ReadOnlySpan<char> value, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        value = value.Trim();

        var colon = value.IndexOf(':');
        if (colon is <= 0 or > 5 || !IsDigits(value[..colon]))
            return false;

        var rest = value[(colon + 1)..];
        var separator = rest.IndexOfAny('.', ':', ',');
        var secondsPart = separator < 0 ? rest : rest[..separator];
        if (secondsPart.Length is < 1 or > 2 || !IsDigits(secondsPart))
            return false;

        var milliseconds = 0;
        if (separator >= 0)
        {
            var fraction = rest[(separator + 1)..];
            if (fraction.IsEmpty || !IsDigits(fraction))
                return false;

            var digits = fraction.Length > 3 ? fraction[..3] : fraction;
            var scale = digits.Length switch { 1 => 100, 2 => 10, _ => 1 };
            milliseconds = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) * scale;
        }

        var minutes = int.Parse(value[..colon], NumberStyles.None, CultureInfo.InvariantCulture);
        var seconds = int.Parse(secondsPart, NumberStyles.None, CultureInfo.InvariantCulture);
        time = new TimeSpan(0, 0, minutes, seconds, milliseconds);
        return true;
    }

    private static void ReadLine(ReadOnlySpan<char> line, List<TimedText> timed, Dictionary<string, string> tags, List<TimeSpan> stamps)
    {
        line = line.TrimStart('\uFEFF').Trim();
        if (line.IsEmpty || line[0] != '[')
            return;

        stamps.Clear();
        var rest = line;
        while (!rest.IsEmpty && rest[0] == '[')
        {
            var close = rest.IndexOf(']');
            if (close < 0)
                break;

            var inner = rest[1..close];
            if (TryParseTime(inner, out var time))
                stamps.Add(time);
            else if (stamps.Count > 0 || !TryReadTag(inner, tags))
                break;

            rest = rest[(close + 1)..].TrimStart();
        }

        if (stamps.Count == 0)
            return;

        var text = StripWordTimes(rest);
        foreach (var stamp in stamps)
        {
            if (timed.Count >= MaxLines)
                return;
            timed.Add(new TimedText(stamp, timed.Count, text));
        }
    }

    private static bool TryReadTag(ReadOnlySpan<char> inner, Dictionary<string, string> tags)
    {
        var colon = inner.IndexOf(':');
        if (colon <= 0)
            return false;

        var name = inner[..colon].Trim();
        if (name.IsEmpty || name.Length > MaxTagNameLength || !(char.IsAsciiLetter(name[0]) || name[0] == '#'))
            return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('#' or '_' or '-'))
                return false;
        }

        var key = name.ToString().ToLowerInvariant();
        if (tags.Count < MaxTags || tags.ContainsKey(key))
            tags[key] = inner[(colon + 1)..].Trim().ToString();
        return true;
    }

    private static TimeSpan ReadOffset(Dictionary<string, string> tags)
    {
        if (!tags.TryGetValue("offset", out var value)
            || !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var milliseconds)
            || milliseconds is < -MaxOffsetMilliseconds or > MaxOffsetMilliseconds)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromTicks(milliseconds * TimeSpan.TicksPerMillisecond);
    }

    /// <summary>Removes word timings in angle brackets, such as 00:12.50, and trims the result.</summary>
    private static string StripWordTimes(ReadOnlySpan<char> text)
    {
        if (text.IndexOf('<') < 0)
            return text.Trim().ToString();

        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '<')
            {
                // Look for the closing bracket only as far as a time can reach, so text full of < stays linear.
                var window = text.Slice(i + 1, Math.Min(MaxWordTimeLength + 1, text.Length - i - 1));
                var close = window.IndexOf('>');
                if (close >= 0 && TryParseTime(text.Slice(i + 1, close), out _))
                {
                    i += close + 2;

                    // A timing between two words must not leave two spaces behind.
                    if (builder.Length > 0 && char.IsWhiteSpace(builder[^1]))
                    {
                        while (i < text.Length && char.IsWhiteSpace(text[i]))
                            i++;
                    }

                    continue;
                }
            }

            builder.Append(text[i]);
            i++;
        }

        return builder.ToString().Trim();
    }

    private static bool IsDigits(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    private readonly record struct TimedText(TimeSpan Time, int Order, string Text);
}
