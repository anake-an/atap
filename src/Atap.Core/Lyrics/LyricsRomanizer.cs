namespace Atap.Core.Lyrics;

/// <summary>Romanizes a lyric line: Korean Hangul and Japanese kana. Kanji and every other script stay as they are.</summary>
public static class LyricsRomanizer
{
    /// <summary>True when <paramref name="text"/> has Hangul or kana to romanize, so a widget can skip the extra row otherwise.</summary>
    public static bool CanRomanize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var composed = UnicodeText.Compose(text);
        return HangulRomanizer.ContainsHangul(composed) || KanaRomanizer.ContainsKana(composed);
    }

    /// <summary>Romanizes the Hangul and kana in <paramref name="text"/>. See <see cref="HangulRomanizer"/> and <see cref="KanaRomanizer"/>.</summary>
    public static string Romanize(string text) => KanaRomanizer.Romanize(HangulRomanizer.Romanize(text));
}
