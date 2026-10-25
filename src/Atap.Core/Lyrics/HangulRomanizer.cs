using System.Text;

namespace Atap.Core.Lyrics;

/// <summary>
/// Romanizes Korean with the Revised Romanization of Korean. It applies the standard's sound change rules between
/// syllables of one word: liaison, nasal assimilation, ㄹ assimilation, palatalization and aspiration with ㅎ.
/// Anything that is not a Hangul syllable is copied unchanged, so spaces, punctuation and Latin text stay as they are.
/// </summary>
/// <remarks>
/// Rules that depend on what a word means are not applied: the added ㄴ in compounds such as 알약, ㄴ before ㄹ in
/// compounds such as 신문로, and the kept h in proper nouns such as 묵호. Tense consonants are not marked, as the
/// standard says. No hyphens are added. Output is lower case. Single letters such as ㅋ stay in Hangul.
/// </remarks>
public static class HangulRomanizer
{
    private const char FirstSyllable = '\uAC00';
    private const char LastSyllable = '\uD7A3';
    private const int VowelCount = 21;
    private const int FinalCount = 28;

    // Initial consonants, by their position in the Unicode syllable order.
    private const int G = 0, Kk = 1, N = 2, D = 3, R = 5, M = 6, B = 7, S = 9, Ss = 10, Silent = 11, J = 12, Ch = 14, K = 15, T = 16, P = 17, H = 18;

    // Final consonants, by their position in the Unicode syllable order. 0 is no final.
    private const int NoFinal = 0, FinalG = 1, FinalKk = 2, FinalGs = 3, FinalN = 4, FinalNj = 5, FinalNh = 6, FinalD = 7, FinalL = 8,
        FinalLg = 9, FinalLb = 11, FinalLt = 13, FinalLp = 14, FinalLh = 15, FinalB = 17, FinalBs = 18, FinalS = 19, FinalSs = 20,
        FinalNg = 21, FinalJ = 22, FinalCh = 23, FinalK = 24, FinalT = 25, FinalP = 26, FinalH = 27;

    private const int VowelI = 20;

    private static readonly string[] Initials =
        ["g", "kk", "n", "d", "tt", "r", "m", "b", "pp", "s", "ss", "", "j", "jj", "ch", "k", "t", "p", "h"];

    private static readonly string[] Vowels =
        ["a", "ae", "ya", "yae", "eo", "e", "yeo", "ye", "o", "wa", "wae", "oe", "yo", "u", "wo", "we", "wi", "yu", "eu", "ui", "i"];

    // How each final sounds at the end of a word or before a consonant. Double finals sound as one of their letters.
    private static readonly string[] Finals =
        ["", "k", "k", "k", "n", "n", "n", "t", "l", "k", "m", "l", "l", "l", "p", "l", "m", "p", "p", "t", "t", "ng", "t", "t", "k", "t", "p", "t"];

    // Before a silent ㅇ the final moves to the next syllable. Kept stays behind as the final, Moved becomes the
    // next initial, and -1 means nothing moves. A double final keeps its first letter and moves the second.
    private static readonly (int Kept, int Moved)[] Liaison =
    [
        (NoFinal, -1), (NoFinal, G), (NoFinal, Kk), (FinalG, S), (NoFinal, N), (FinalN, J), (NoFinal, N), (NoFinal, D), (NoFinal, R),
        (FinalL, G), (FinalL, M), (FinalL, B), (FinalL, S), (FinalL, T), (FinalL, P), (NoFinal, R), (NoFinal, M), (NoFinal, B),
        (FinalB, S), (NoFinal, S), (NoFinal, Ss), (FinalNg, -1), (NoFinal, J), (NoFinal, Ch), (NoFinal, K), (NoFinal, T), (NoFinal, P),
        (NoFinal, -1),
    ];

    /// <summary>True when <paramref name="text"/> has at least one Hangul syllable.</summary>
    public static bool ContainsHangul(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (IsSyllable(c))
                return true;
        }

        return false;
    }

    /// <summary>Romanizes every Hangul word in <paramref name="text"/>. Text without Hangul is returned as it is.</summary>
    public static string Romanize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = UnicodeText.Compose(text);
        if (!ContainsHangul(text))
            return text;

        var builder = new StringBuilder(text.Length * 4);
        string? carried = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!IsSyllable(c))
            {
                builder.Append(c);
                continue;
            }

            var (initial, vowel, final) = Split(c);
            builder.Append(carried ?? Initials[initial]).Append(Vowels[vowel]);

            // Sound changes happen only where two syllables of one word touch.
            if (i + 1 < text.Length && IsSyllable(text[i + 1]))
            {
                var (nextInitial, nextVowel, _) = Split(text[i + 1]);
                var (finalText, initialText) = Join(final, nextInitial, nextVowel);
                builder.Append(finalText);
                carried = initialText;
            }
            else
            {
                builder.Append(Finals[final]);
                carried = null;
            }
        }

        return builder.ToString();
    }

    private static bool IsSyllable(char c) => c is >= FirstSyllable and <= LastSyllable;

    private static (int Initial, int Vowel, int Final) Split(char syllable)
    {
        var index = syllable - FirstSyllable;
        return (index / (VowelCount * FinalCount), index / FinalCount % VowelCount, index % FinalCount);
    }

    /// <summary>Romanizes the meeting of one syllable's final with the next syllable's initial.</summary>
    private static (string Final, string Initial) Join(int final, int initial, int nextVowel)
    {
        if (final == NoFinal)
            return ("", Initials[initial]);

        // Liaison: before a silent initial the final is said as the start of the next syllable (한국어 hangugeo).
        if (initial == Silent)
        {
            var (kept, moved) = Liaison[final];
            if (moved < 0)
                return (Finals[kept], "");

            // Palatalization: ㄷ and ㅌ before 이 sound as ㅈ and ㅊ (같이 gachi).
            if (nextVowel == VowelI)
                moved = moved switch { D => J, T => Ch, _ => moved };
            return (Finals[kept], Initials[moved]);
        }

        // A final ㅎ aspirates a following ㄱ, ㄷ or ㅈ and sounds as ㄴ before ㄴ (좋고 joko, 놓는 nonneun). Before ㅅ it
        // makes a tense ss, which is not written (좋소 joso).
        if (final is FinalH or FinalNh or FinalLh)
        {
            var first = final switch { FinalNh => "n", FinalLh => "l", _ => "" };
            switch (initial)
            {
                case G: return (first, "k");
                case D: return (first, "t");
                case J: return (first, "ch");
                case S: return (first, "s");
                case N: return final == FinalLh ? ("l", "l") : ("n", "n");
            }

            final = final switch { FinalNh => FinalN, FinalLh => FinalL, _ => FinalD };
        }

        // An initial ㅎ aspirates the final before it (축하 chuka, 굳히다 guchida).
        if (initial == H && Aspirate(final, nextVowel) is { } aspirated)
            return aspirated;

        var sound = Finals[final];
        return initial switch
        {
            // Nasal assimilation: k, t and p become ng, n and m before ㄴ and ㅁ (백마 baengma). ㄹ makes ㄴ an l (설날 seollal).
            N when sound == "l" => ("l", "l"),
            N or M => (Nasal(sound), Initials[initial]),

            // ㄹ assimilation: ㄹ after ㄴ or ㄹ is ll (신라 silla), and n after any other final (종로 jongno, 백리 baengni).
            R when sound is "n" or "l" => ("l", "l"),
            R => (Nasal(sound), "n"),

            _ => (sound, Initials[initial]),
        };
    }

    private static (string Final, string Initial)? Aspirate(int final, int nextVowel) => final switch
    {
        FinalG or FinalKk or FinalGs or FinalK => ("", "k"),
        FinalLg => ("l", "k"),
        FinalD => ("", nextVowel == VowelI ? "ch" : "t"),
        FinalS or FinalSs or FinalT => ("", "t"),
        FinalJ or FinalCh => ("", "ch"),
        FinalNj => ("n", "ch"),
        FinalB or FinalP or FinalBs => ("", "p"),
        FinalLb or FinalLp => ("l", "p"),
        FinalLt => ("l", "t"),
        _ => null,
    };

    private static string Nasal(string sound) => sound switch
    {
        "k" => "ng",
        "t" => "n",
        "p" => "m",
        _ => sound,
    };
}
