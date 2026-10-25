using System.Text;

namespace Atap.Core.Lyrics;

/// <summary>
/// Romanizes Japanese hiragana and katakana by table, in modified Hepburn. Kanji and every other character are
/// copied unchanged, since reading kanji needs a dictionary.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Long vowels are written as typed. おう stays ou, and the katakana mark ー repeats the vowel before it (ラーメン raamen).</item>
/// <item>A small っ doubles the next consonant (きって kitte, まっちゃ matcha). At the end of a word it is dropped.</item>
/// <item>ん is n, and n' before a vowel or y (きんえん kin'en).</item>
/// <item>The particles は, へ and を are read by the table as ha, he and o, because a table cannot tell a particle apart.</item>
/// <item>The iteration marks ゝ and ゞ repeat the kana before them. The katakana middle dot becomes a space.</item>
/// <item>Output is lower case. Half-width katakana are not handled.</item>
/// </list>
/// </remarks>
public static class KanaRomanizer
{
    private const char SmallTsu = 'っ';
    private const char N = 'ん';
    private const char LongVowelMark = 'ー';
    private const char Iteration = 'ゝ';
    private const char VoicedIteration = 'ゞ';
    private const char MiddleDot = '・';

    private const string Unvoiced = "かきくけこさしすせそたちつてとはひふへほう";
    private const string Voiced = "がぎぐげござじずぜぞだぢづでどばびぶべぼゔ";

    // Hiragana, plus the few katakana that have no hiragana form. Other katakana fold to hiragana first.
    private static readonly Dictionary<char, string> Kana = new()
    {
        ['ぁ'] = "a", ['あ'] = "a", ['ぃ'] = "i", ['い'] = "i", ['ぅ'] = "u", ['う'] = "u", ['ぇ'] = "e", ['え'] = "e", ['ぉ'] = "o", ['お'] = "o",
        ['か'] = "ka", ['が'] = "ga", ['き'] = "ki", ['ぎ'] = "gi", ['く'] = "ku", ['ぐ'] = "gu", ['け'] = "ke", ['げ'] = "ge", ['こ'] = "ko", ['ご'] = "go",
        ['さ'] = "sa", ['ざ'] = "za", ['し'] = "shi", ['じ'] = "ji", ['す'] = "su", ['ず'] = "zu", ['せ'] = "se", ['ぜ'] = "ze", ['そ'] = "so", ['ぞ'] = "zo",
        ['た'] = "ta", ['だ'] = "da", ['ち'] = "chi", ['ぢ'] = "ji", ['つ'] = "tsu", ['づ'] = "zu", ['て'] = "te", ['で'] = "de", ['と'] = "to", ['ど'] = "do",
        ['な'] = "na", ['に'] = "ni", ['ぬ'] = "nu", ['ね'] = "ne", ['の'] = "no",
        ['は'] = "ha", ['ば'] = "ba", ['ぱ'] = "pa", ['ひ'] = "hi", ['び'] = "bi", ['ぴ'] = "pi", ['ふ'] = "fu", ['ぶ'] = "bu", ['ぷ'] = "pu",
        ['へ'] = "he", ['べ'] = "be", ['ぺ'] = "pe", ['ほ'] = "ho", ['ぼ'] = "bo", ['ぽ'] = "po",
        ['ま'] = "ma", ['み'] = "mi", ['む'] = "mu", ['め'] = "me", ['も'] = "mo",
        ['ゃ'] = "ya", ['や'] = "ya", ['ゅ'] = "yu", ['ゆ'] = "yu", ['ょ'] = "yo", ['よ'] = "yo",
        ['ら'] = "ra", ['り'] = "ri", ['る'] = "ru", ['れ'] = "re", ['ろ'] = "ro",
        ['ゎ'] = "wa", ['わ'] = "wa", ['ゐ'] = "i", ['ゑ'] = "e", ['を'] = "o", ['ん'] = "n",
        ['ゔ'] = "vu", ['ゕ'] = "ka", ['ゖ'] = "ke", ['ゟ'] = "yori",
        ['ヷ'] = "va", ['ヸ'] = "vi", ['ヹ'] = "ve", ['ヺ'] = "vo", ['ヿ'] = "koto",
    };

    // A kana followed by a small kana, read as one sound.
    private static readonly Dictionary<(char, char), string> Pairs = BuildPairs(
    [
        ("きゃ", "kya"), ("きゅ", "kyu"), ("きょ", "kyo"), ("ぎゃ", "gya"), ("ぎゅ", "gyu"), ("ぎょ", "gyo"),
        ("しゃ", "sha"), ("しゅ", "shu"), ("しょ", "sho"), ("じゃ", "ja"), ("じゅ", "ju"), ("じょ", "jo"),
        ("ちゃ", "cha"), ("ちゅ", "chu"), ("ちょ", "cho"), ("ぢゃ", "ja"), ("ぢゅ", "ju"), ("ぢょ", "jo"),
        ("にゃ", "nya"), ("にゅ", "nyu"), ("にょ", "nyo"), ("ひゃ", "hya"), ("ひゅ", "hyu"), ("ひょ", "hyo"),
        ("びゃ", "bya"), ("びゅ", "byu"), ("びょ", "byo"), ("ぴゃ", "pya"), ("ぴゅ", "pyu"), ("ぴょ", "pyo"),
        ("みゃ", "mya"), ("みゅ", "myu"), ("みょ", "myo"), ("りゃ", "rya"), ("りゅ", "ryu"), ("りょ", "ryo"),

        // Sounds from loanwords, mostly written in katakana.
        ("いぇ", "ye"), ("うぃ", "wi"), ("うぇ", "we"), ("うぉ", "wo"),
        ("ゔぁ", "va"), ("ゔぃ", "vi"), ("ゔぇ", "ve"), ("ゔぉ", "vo"), ("ゔゅ", "vyu"),
        ("きぇ", "kye"), ("くぁ", "kwa"), ("くぃ", "kwi"), ("くぇ", "kwe"), ("くぉ", "kwo"), ("ぐぁ", "gwa"),
        ("しぇ", "she"), ("じぇ", "je"), ("ちぇ", "che"), ("すぃ", "si"), ("ずぃ", "zi"),
        ("つぁ", "tsa"), ("つぃ", "tsi"), ("つぇ", "tse"), ("つぉ", "tso"),
        ("てぃ", "ti"), ("でぃ", "di"), ("てゅ", "tyu"), ("でゅ", "dyu"), ("とぅ", "tu"), ("どぅ", "du"),
        ("ふぁ", "fa"), ("ふぃ", "fi"), ("ふぇ", "fe"), ("ふぉ", "fo"), ("ふゅ", "fyu"),
        ("にぇ", "nye"), ("ひぇ", "hye"),
    ]);

    /// <summary>True when <paramref name="text"/> has at least one hiragana or katakana letter.</summary>
    public static bool ContainsKana(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u3041' and <= '\u3096' or >= '\u30A1' and <= '\u30FA')
                return true;
        }

        return false;
    }

    /// <summary>Romanizes the kana in <paramref name="text"/>. Text without kana is returned as it is.</summary>
    public static string Romanize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = UnicodeText.Compose(text);
        if (!ContainsKana(text))
            return text;

        var builder = new StringBuilder(text.Length * 3);
        var doubleNext = false;     // a small っ waits for the next consonant
        var afterN = false;         // the last kana was ん
        var previous = '\0';        // the last single kana, for iteration marks
        string? lastSound = null;   // the last kana sound, for the long vowel mark

        for (var i = 0; i < text.Length; i++)
        {
            var kana = Fold(text[i]);
            string? sound;

            if (i + 1 < text.Length && Pairs.TryGetValue((kana, Fold(text[i + 1])), out var pair))
            {
                sound = pair;
                previous = '\0';
                i++;
            }
            else if (kana == SmallTsu)
            {
                doubleNext = true;
                continue;
            }
            else if (kana == LongVowelMark)
            {
                // After a kana vowel the mark repeats it. Anywhere else it is not a kana mark, so it stays.
                if (lastSound is null)
                    builder.Append(text[i]);
                else if (IsVowel(lastSound[^1]))
                    builder.Append(lastSound[^1]);
                continue;
            }
            else if (kana is Iteration or VoicedIteration && previous != '\0')
            {
                var repeated = kana == VoicedIteration ? Voice(previous) : Unvoice(previous);
                sound = Kana.GetValueOrDefault(repeated);
            }
            else
            {
                sound = Kana.GetValueOrDefault(kana);
                previous = sound is null ? '\0' : kana;
            }

            if (sound is null)
            {
                builder.Append(kana == MiddleDot ? ' ' : text[i]);
                doubleNext = false;
                afterN = false;
                previous = '\0';
                lastSound = null;
                continue;
            }

            if (doubleNext)
            {
                if (kana != N && !IsVowel(sound[0]))
                    builder.Append(sound.StartsWith("ch", StringComparison.Ordinal) ? 't' : sound[0]);
                doubleNext = false;
            }
            else if (afterN && (IsVowel(sound[0]) || sound[0] == 'y'))
            {
                builder.Append('\'');
            }

            builder.Append(sound);
            afterN = kana == N;
            lastSound = sound;
        }

        return builder.ToString();
    }

    /// <summary>Maps katakana to hiragana, so one table serves both.</summary>
    private static char Fold(char c) => c switch
    {
        >= '\u30A1' and <= '\u30F6' => (char)(c - 0x60),
        'ヽ' => Iteration,
        'ヾ' => VoicedIteration,
        _ => c,
    };

    private static char Voice(char kana) => Unvoiced.IndexOf(kana) is var i and >= 0 ? Voiced[i] : kana;

    private static char Unvoice(char kana) => Voiced.IndexOf(kana) is var i and >= 0 ? Unvoiced[i] : kana;

    private static bool IsVowel(char c) => c is 'a' or 'i' or 'u' or 'e' or 'o';

    private static Dictionary<(char, char), string> BuildPairs(ReadOnlySpan<(string Kana, string Sound)> table)
    {
        var pairs = new Dictionary<(char, char), string>(table.Length);
        foreach (var (kana, sound) in table)
            pairs.Add((kana[0], kana[1]), sound);
        return pairs;
    }
}
