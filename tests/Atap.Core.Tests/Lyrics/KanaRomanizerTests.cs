using Atap.Core.Lyrics;

namespace Atap.Core.Tests.Lyrics;

public class KanaRomanizerTests
{
    [Theory]
    [InlineData("あいうえお", "aiueo")]
    [InlineData("かきくけこ", "kakikukeko")]
    [InlineData("さしすせそ", "sashisuseso")]
    [InlineData("たちつてと", "tachitsuteto")]
    [InlineData("なにぬねの", "naninuneno")]
    [InlineData("はひふへほ", "hahifuheho")]
    [InlineData("まみむめも", "mamimumemo")]
    [InlineData("やゆよ", "yayuyo")]
    [InlineData("らりるれろ", "rarirurero")]
    [InlineData("わをん", "waon")]
    [InlineData("がぎぐげご", "gagigugego")]
    [InlineData("ざじずぜぞ", "zajizuzezo")]
    [InlineData("だぢづでど", "dajizudedo")]
    [InlineData("ばびぶべぼ", "babibubebo")]
    [InlineData("ぱぴぷぺぽ", "papipupepo")]
    public void Romanize_EveryRowOfTheTable(string kana, string expected)
    {
        Assert.Equal(expected, KanaRomanizer.Romanize(kana));
    }

    [Theory]
    [InlineData("きょう", "kyou")]
    [InlineData("しゃしん", "shashin")]
    [InlineData("ちゃいろ", "chairo")]
    [InlineData("じゃあね", "jaane")]
    [InlineData("にゅうがく", "nyuugaku")]
    [InlineData("りょこう", "ryokou")]
    [InlineData("ぴょん", "pyon")]
    public void Romanize_ContractedSounds(string kana, string expected)
    {
        Assert.Equal(expected, KanaRomanizer.Romanize(kana));
    }

    [Theory]
    [InlineData("きって", "kitte")]
    [InlineData("まっちゃ", "matcha")]
    [InlineData("いっしょ", "issho")]
    [InlineData("ずっと", "zutto")]
    [InlineData("ちょっと", "chotto")]
    [InlineData("あっ", "a")]
    [InlineData("あっ!", "a!")]
    public void Romanize_SmallTsu_DoublesTheNextConsonant(string kana, string expected)
    {
        Assert.Equal(expected, KanaRomanizer.Romanize(kana));
    }

    [Theory]
    [InlineData("きんえん", "kin'en")]
    [InlineData("ほんや", "hon'ya")]
    [InlineData("せんぱい", "senpai")]
    [InlineData("しんぶん", "shinbun")]
    [InlineData("こんにちは", "konnichiha")]
    public void Romanize_N_TakesAnApostropheBeforeVowelsAndY(string kana, string expected)
    {
        Assert.Equal(expected, KanaRomanizer.Romanize(kana));
    }

    [Theory]
    [InlineData("コーヒー", "koohii")]
    [InlineData("ラーメン", "raamen")]
    [InlineData("パーティー", "paatii")]
    [InlineData("ファン", "fan")]
    [InlineData("ヴァイオリン", "vaiorin")]
    [InlineData("ウィンドウズ", "windouzu")]
    [InlineData("シェア", "shea")]
    [InlineData("ディスコ", "disuko")]
    [InlineData("チェック", "chekku")]
    [InlineData("ツアー", "tsuaa")]
    [InlineData("ジョン・スミス", "jon sumisu")]
    [InlineData("ヴ", "vu")]
    public void Romanize_Katakana(string kana, string expected)
    {
        Assert.Equal(expected, KanaRomanizer.Romanize(kana));
    }

    [Theory]
    [InlineData("ありがとう", "arigatou")]
    [InlineData("さようなら", "sayounara")]
    [InlineData("きみがすき", "kimigasuki")]
    [InlineData("東京へ行こう", "東京he行kou")]
    [InlineData("夢を見た", "夢o見ta")]
    [InlineData("行った", "行tta")]
    [InlineData("君の名前、教えて。", "君no名前、教ete。")]
    [InlineData("Lampu kota と ネオン", "Lampu kota to neon")]
    public void Romanize_KanjiAndOtherText_StayAsTheyAre(string text, string expected)
    {
        Assert.Equal(expected, KanaRomanizer.Romanize(text));
    }

    [Theory]
    [InlineData("こゝろ", "kokoro")]
    [InlineData("いすゞ", "isuzu")]
    [InlineData("バナヽ", "banana")]
    [InlineData("ゝ", "ゝ")]
    public void Romanize_IterationMarks_RepeatTheKanaBefore(string kana, string expected)
    {
        Assert.Equal(expected, KanaRomanizer.Romanize(kana));
    }

    [Fact]
    public void Romanize_LongVowelMark_StaysWhenNotAfterKana()
    {
        Assert.Equal("Aー", KanaRomanizer.Romanize("Aー"));
        Assert.Equal("ー a", KanaRomanizer.Romanize("ー あ"));
        Assert.Equal("n", KanaRomanizer.Romanize("ンー"));
    }

    [Fact]
    public void Romanize_SeparateVoicingMark_IsComposedFirst()
    {
        Assert.Equal("ga", KanaRomanizer.Romanize("か\u3099"));
        Assert.Equal("pa", KanaRomanizer.Romanize("ハ\u309A"));
    }

    [Fact]
    public void Romanize_TextWithoutKana_IsReturnedAsItIs()
    {
        const string text = "東京 Lampu kota";

        Assert.Same(text, KanaRomanizer.Romanize(text));
        Assert.Equal("・ー", KanaRomanizer.Romanize("・ー"));
    }

    [Fact]
    public void Romanize_EveryKana_GivesLatinText()
    {
        for (var c = '\u3041'; c <= '\u3096'; c++)
            Assert.Matches("^[a-z]*$", KanaRomanizer.Romanize(c.ToString()));
        for (var c = '\u30A1'; c <= '\u30FA'; c++)
            Assert.Matches("^[a-z]*$", KanaRomanizer.Romanize(c.ToString()));
    }

    [Theory]
    [InlineData("ありがとう", true)]
    [InlineData("カタカナ", true)]
    [InlineData("東京", false)]
    [InlineData("ー・", false)]
    [InlineData("", false)]
    public void ContainsKana_LooksForKanaLetters(string text, bool expected)
    {
        Assert.Equal(expected, KanaRomanizer.ContainsKana(text));
    }
}
