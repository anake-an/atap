using Atap.Core.Lyrics;

namespace Atap.Core.Tests.Lyrics;

public class HangulRomanizerTests
{
    [Theory]
    // Place names given as examples in the Revised Romanization rules.
    [InlineData("구미", "gumi")]
    [InlineData("영동", "yeongdong")]
    [InlineData("백암", "baegam")]
    [InlineData("옥천", "okcheon")]
    [InlineData("합덕", "hapdeok")]
    [InlineData("호법", "hobeop")]
    [InlineData("월곶", "wolgot")]
    [InlineData("벚꽃", "beotkkot")]
    [InlineData("한밭", "hanbat")]
    [InlineData("구리", "guri")]
    [InlineData("설악", "seorak")]
    [InlineData("칠곡", "chilgok")]
    [InlineData("임실", "imsil")]
    [InlineData("울릉", "ulleung")]
    [InlineData("대관령", "daegwallyeong")]
    [InlineData("광희문", "gwanghuimun")]
    [InlineData("서울", "seoul")]
    [InlineData("부산", "busan")]
    public void Romanize_BasicLettersAndFinals(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Theory]
    [InlineData("한국어", "hangugeo")]
    [InlineData("읽어", "ilgeo")]
    [InlineData("없어", "eopseo")]
    [InlineData("앉아", "anja")]
    [InlineData("닭이", "dalgi")]
    [InlineData("밖에", "bakke")]
    [InlineData("있어", "isseo")]
    [InlineData("삶을", "salmeul")]
    [InlineData("싶어", "sipeo")]
    [InlineData("강아지", "gangaji")]
    public void Romanize_Liaison_MovesTheFinalOntoTheNextVowel(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Theory]
    [InlineData("백마", "baengma")]
    [InlineData("감사합니다", "gamsahamnida")]
    [InlineData("읽는", "ingneun")]
    [InlineData("겉모습", "geonmoseup")]
    [InlineData("종로", "jongno")]
    [InlineData("왕십리", "wangsimni")]
    [InlineData("침략", "chimnyak")]
    [InlineData("백리", "baengni")]
    [InlineData("협력", "hyeomnyeok")]
    public void Romanize_NasalAssimilation(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Theory]
    [InlineData("신라", "silla")]
    [InlineData("설날", "seollal")]
    [InlineData("별내", "byeollae")]
    [InlineData("달님", "dallim")]
    public void Romanize_LiquidAssimilation(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Theory]
    [InlineData("해돋이", "haedoji")]
    [InlineData("같이", "gachi")]
    [InlineData("굳히다", "guchida")]
    [InlineData("핥이", "halchi")]
    public void Romanize_Palatalization(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Theory]
    [InlineData("좋고", "joko")]
    [InlineData("놓다", "nota")]
    [InlineData("잡혀", "japyeo")]
    [InlineData("낳지", "nachi")]
    [InlineData("축하", "chuka")]
    [InlineData("밝히다", "balkida")]
    [InlineData("넓히다", "neolpida")]
    [InlineData("앉히다", "anchida")]
    [InlineData("깨끗하다", "kkaekkeutada")]
    [InlineData("많다", "manta")]
    [InlineData("싫고", "silko")]
    public void Romanize_AspirationWithHieut(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Theory]
    [InlineData("좋아", "joa")]
    [InlineData("많아", "mana")]
    [InlineData("싫어", "sireo")]
    [InlineData("괜찮아", "gwaenchana")]
    [InlineData("놓는", "nonneun")]
    [InlineData("않는", "anneun")]
    [InlineData("싫나", "silla")]
    [InlineData("좋소", "joso")]
    [InlineData("많소", "manso")]
    [InlineData("싫소", "silso")]
    public void Romanize_FinalHieut_IsSilentOrAssimilated(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Theory]
    [InlineData("압구정", "apgujeong")]
    [InlineData("낙동강", "nakdonggang")]
    [InlineData("합정", "hapjeong")]
    [InlineData("팔당", "paldang")]
    [InlineData("샛별", "saetbyeol")]
    [InlineData("울산", "ulsan")]
    [InlineData("학교", "hakgyo")]
    public void Romanize_TenseConsonants_AreNotMarked(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Theory]
    [InlineData("안녕하세요", "annyeonghaseyo")]
    [InlineData("사랑해", "saranghae")]
    [InlineData("보고 싶어", "bogo sipeo")]
    [InlineData("오늘 밤", "oneul bam")]
    [InlineData("널 사랑해 baby", "neol saranghae baby")]
    [InlineData("Hello, 세상!", "Hello, sesang!")]
    [InlineData("라면", "ramyeon")]
    public void Romanize_LyricLines(string hangul, string expected)
    {
        Assert.Equal(expected, HangulRomanizer.Romanize(hangul));
    }

    [Fact]
    public void Romanize_SoundChanges_StopAtSpacesAndPunctuation()
    {
        // 책 읽어 keeps the k, unlike 책이 where the final moves over.
        Assert.Equal("chaek ilgeo", HangulRomanizer.Romanize("책 읽어"));
        Assert.Equal("chaegi", HangulRomanizer.Romanize("책이"));
        Assert.Equal("chaek, i", HangulRomanizer.Romanize("책, 이"));
    }

    [Fact]
    public void Romanize_DecomposedLetters_AreComposedFirst()
    {
        // 한 typed as three conjoining letters.
        Assert.Equal("han", HangulRomanizer.Romanize("\u1112\u1161\u11AB"));
    }

    [Fact]
    public void Romanize_TextWithoutHangul_IsReturnedAsItIs()
    {
        const string text = "Lampu kota menyala";

        Assert.Same(text, HangulRomanizer.Romanize(text));
        Assert.Equal("ㅋㅋㅋ", HangulRomanizer.Romanize("ㅋㅋㅋ"));
        Assert.Equal("東京", HangulRomanizer.Romanize("東京"));
    }

    [Fact]
    public void Romanize_BrokenUtf16_DoesNotThrow()
    {
        Assert.Equal("sa\uD800rang", HangulRomanizer.Romanize("사\uD800랑"));
    }

    [Fact]
    public void Romanize_EverySyllable_GivesLowerCaseLatinLetters()
    {
        for (var c = '\uAC00'; c <= '\uD7A3'; c++)
        {
            var single = HangulRomanizer.Romanize(c.ToString());
            var pair = HangulRomanizer.Romanize(new string(c, 2));
            Assert.Matches("^[a-z]+$", single);
            Assert.Matches("^[a-z]+$", pair);
        }
    }

    [Theory]
    [InlineData("사랑", true)]
    [InlineData("Lampu kota", false)]
    [InlineData("ㅋㅋ", false)]
    [InlineData("", false)]
    public void ContainsHangul_LooksForSyllables(string text, bool expected)
    {
        Assert.Equal(expected, HangulRomanizer.ContainsHangul(text));
    }
}
