using System.Globalization;
using System.Text;

namespace Atap.Core.Commands.Query;

/// <summary>
/// Scores how well typed text matches a title. Pure and culture-independent, so the same query
/// gives the same scores on every PC.
/// </summary>
/// <remarks>
/// Text is compared without case or accents ("cafe" finds "Café"). Anything that is not a letter or digit
/// separates words. A capital after a small letter and a switch between letters and digits also start a word,
/// so "PowerShell" has the words "power" and "shell".
/// <para>
/// Each match falls in a tier, from best to worst:
/// the whole title, the start of the title, the start of a word, every query word starting a title word in order,
/// a run of the initials of the title words, anywhere in the title, and the query letters in order starting at a word.
/// Inside a tier a shorter title scores higher.
/// A keyword match scores below a title match of the same tier and above the tier under it.
/// </para>
/// </remarks>
public static class QueryMatcher
{
    private const int TierSize = 100;
    private const int MaxPenalty = TierSize / 2 - 1;
    private const int KeywordPenalty = TierSize / 2;

    private enum Tier
    {
        None,
        Letters,
        Anywhere,
        Initials,
        Words,
        WordStart,
        Start,
        Exact,
    }

    /// <summary>A query normalized once for many candidates.</summary>
    internal sealed class Prepared(string text)
    {
        public string Text { get; } = text;

        public string[] Words { get; } = text.Split(' ');

        public string Compact { get; } = text.Replace(" ", "", StringComparison.Ordinal);
    }

    private enum CharKind
    {
        Separator,
        Lower,
        Upper,
        Digit,
    }

    /// <summary>Scores <paramref name="text"/> against <paramref name="query"/>. 0 means no match. Higher is better.</summary>
    public static int Score(string? query, string? text)
    {
        var prepared = Prepare(query);
        return prepared is null || text is null ? 0 : Score(prepared, text, 0);
    }

    /// <summary>The best score of a candidate's title and keywords.</summary>
    internal static int Score(Prepared query, QueryCandidate candidate)
    {
        var best = Score(query, candidate.Title, 0);
        foreach (var keyword in candidate.Keywords)
        {
            if (keyword is not null)
                best = Math.Max(best, Score(query, keyword, KeywordPenalty));
        }

        return best;
    }

    /// <summary>Normalizes a query, or returns null when nothing is left to match.</summary>
    internal static Prepared? Prepare(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;
        var text = Normalize(query, null);
        return text.Length == 0 ? null : new Prepared(text);
    }

    private static int Score(Prepared query, string text, int basePenalty)
    {
        var starts = new List<int>();
        var normalized = Normalize(text, starts);
        if (normalized.Length == 0)
            return 0;

        var (tier, penalty) = Match(query, normalized, starts);
        if (tier == Tier.None)
            return 0;
        return (int)tier * TierSize - basePenalty - Math.Min(penalty, MaxPenalty);
    }

    private static (Tier Tier, int Penalty) Match(Prepared query, string text, List<int> starts)
    {
        var q = query.Text;
        var extra = Math.Max(0, text.Length - q.Length);

        if (string.Equals(text, q, StringComparison.Ordinal))
            return (Tier.Exact, 0);
        if (text.StartsWith(q, StringComparison.Ordinal))
            return (Tier.Start, extra);
        foreach (var start in starts)
        {
            if (start + q.Length <= text.Length && string.CompareOrdinal(text, start, q, 0, q.Length) == 0)
                return (Tier.WordStart, extra);
        }

        if (query.Words.Length > 1 && WordsInOrder(query.Words, text, starts))
            return (Tier.Words, extra);
        // Initials anywhere in the title, so "ps" finds "Windows PowerShell".
        if (query.Compact.Length >= 2 && Initials(text, starts).Contains(query.Compact, StringComparison.Ordinal))
            return (Tier.Initials, extra);
        if (text.Contains(q, StringComparison.Ordinal))
            return (Tier.Anywhere, extra);
        if (query.Compact.Length >= 3 && LettersInOrder(query.Compact, text, starts, out var gaps))
            return (Tier.Letters, gaps);
        return (Tier.None, 0);
    }

    /// <summary>Each query word starts a later title word than the one before.</summary>
    private static bool WordsInOrder(string[] words, string text, List<int> starts)
    {
        var next = 0;
        foreach (var word in words)
        {
            var found = false;
            while (next < starts.Count)
            {
                var start = starts[next++];
                if (start + word.Length <= text.Length && string.CompareOrdinal(text, start, word, 0, word.Length) == 0)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    private static string Initials(string text, List<int> starts)
    {
        var initials = new char[starts.Count];
        for (var i = 0; i < starts.Count; i++)
            initials[i] = text[starts[i]];
        return new string(initials);
    }

    /// <summary>
    /// The query letters appear in order, the first at a word start. <paramref name="gaps"/> counts the
    /// letters skipped, taking the best word start.
    /// </summary>
    private static bool LettersInOrder(string query, string text, List<int> starts, out int gaps)
    {
        gaps = int.MaxValue;
        foreach (var start in starts)
        {
            if (text[start] != query[0])
                continue;

            var matched = 1;
            var skipped = 0;
            for (var i = start + 1; i < text.Length && matched < query.Length; i++)
            {
                if (text[i] == query[matched])
                    matched++;
                else if (text[i] != ' ')
                    skipped++;
            }

            if (matched == query.Length)
                gaps = Math.Min(gaps, skipped);
        }

        return gaps != int.MaxValue;
    }

    /// <summary>
    /// Lowercase letters and digits with single spaces between words, accents removed.
    /// Fills <paramref name="wordStarts"/> with the index of each word's first character.
    /// </summary>
    internal static string Normalize(string text, List<int>? wordStarts)
    {
        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            // Text with a lone surrogate cannot be normalized. It is matched as it is.
            decomposed = text;
        }

        var builder = new StringBuilder(decomposed.Length);
        var previous = CharKind.Separator;
        var pendingSpace = false;

        foreach (var c in decomposed)
        {
            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            // An apostrophe joins rather than splits, so "Don't" stays one word.
            if (c is '\'' or '’')
                continue;
            if (!char.IsLetterOrDigit(c))
            {
                pendingSpace = builder.Length > 0;
                previous = CharKind.Separator;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            var kind = char.IsDigit(c) ? CharKind.Digit : char.IsUpper(c) ? CharKind.Upper : CharKind.Lower;
            var wordStart = previous == CharKind.Separator
                || (kind == CharKind.Upper && previous == CharKind.Lower)
                || ((kind == CharKind.Digit) != (previous == CharKind.Digit));
            if (wordStart)
                wordStarts?.Add(builder.Length);

            builder.Append(char.ToLowerInvariant(c));
            previous = kind;
        }

        return builder.ToString();
    }
}
