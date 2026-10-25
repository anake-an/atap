using System.Globalization;
using System.Text;
using Atap.Core.LocalApi;

namespace Atap.Core.Tests.LocalApi;

public class TextSanitizerTests
{
    // Code points are written as numbers so no invisible character ever sits in this file.
    private const char Rlo = (char)0x202E;
    private const char Lri = (char)0x2066;
    private const char Pdi = (char)0x2069;
    private const char Rlm = (char)0x200F;
    private const char Zwj = (char)0x200D;
    private const char Nbsp = (char)0x00A0;
    private const char Bom = (char)0xFEFF;

    [Theory]
    [InlineData(0x202A)]
    [InlineData(0x202B)]
    [InlineData(0x202C)]
    [InlineData(0x202D)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    [InlineData(0x2067)]
    [InlineData(0x2068)]
    [InlineData(0x2069)]
    [InlineData(0x200E)]
    [InlineData(0x200F)]
    [InlineData(0x061C)]
    public void Sanitize_RemovesBidiControls(int codePoint)
    {
        var control = (char)codePoint;

        Assert.Equal("Backup NAS", TextSanitizer.Sanitize($"Back{control}up NAS{control}"));
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x0007)]
    [InlineData(0x001B)]
    [InlineData(0x007F)]
    [InlineData(0x0080)]
    [InlineData(0x009B)]
    [InlineData(0x009F)]
    public void Sanitize_RemovesControlCharacters(int codePoint)
    {
        var control = (char)codePoint;

        Assert.Equal("Sandaran siap", TextSanitizer.Sanitize($"{control}Sandaran{control} siap"));
    }

    [Fact]
    public void Sanitize_RemovesTrojanSourceStyleOverride()
    {
        // The override would show "nimda" reversed, so the text reads differently from what is stored.
        var tricky = $"user {Rlo}{Lri}nimda{Pdi}{Lri}";

        Assert.Equal("user nimda", TextSanitizer.Sanitize(tricky));
    }

    [Theory]
    [InlineData("Backup\nNAS")]
    [InlineData("Backup\r\nNAS")]
    [InlineData("Backup\tNAS")]
    [InlineData("Backup\u2028NAS")]
    [InlineData("Backup\u2029NAS")]
    [InlineData("Backup\u0085NAS")]
    [InlineData("Backup\u00A0\u00A0NAS")]
    [InlineData("  \t Backup   NAS \r\n")]
    [InlineData("Backup \u202E NAS")]
    public void Sanitize_MakesOneLineWithSingleSpaces(string input)
    {
        Assert.Equal("Backup NAS", TextSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("\u202E\u2066\u0000")]
    public void Sanitize_EmptyOrInvisibleGivesEmpty(string? input)
    {
        Assert.Equal(string.Empty, TextSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_RemovesLoneSurrogatesAndByteOrderMark()
    {
        // Built in code: xUnit cannot carry lone surrogates through InlineData.
        var high = (char)0xD800;
        var low = (char)0xDC00;

        Assert.Equal("AB", TextSanitizer.Sanitize($"A{high}B"));
        Assert.Equal("AB", TextSanitizer.Sanitize($"A{low}B"));
        Assert.Equal("AB", TextSanitizer.Sanitize($"A{low}{high}B"));
        Assert.Equal(string.Empty, TextSanitizer.Sanitize(((char)0xDBFF).ToString()));
        Assert.Equal("Backup", TextSanitizer.Sanitize(Bom + "Backup"));
    }

    [Theory]
    [InlineData("Waktu solat dikemas kini")]
    [InlineData("Muat turun 64% siap")]
    [InlineData("\u062C\u0627\u0648\u064A")]                      // Jawi script, right to left on its own
    [InlineData("Selesai \U0001F389")]                            // emoji outside the BMP
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467")]   // family emoji joined with U+200D
    [InlineData("Cafe\u0301")]                                    // combining accent
    [InlineData("\uD55C\uAD6D\uC5B4 \u3072\u3089\u304C\u306A")]  // Hangul and kana
    public void Sanitize_KeepsCleanTextAsItIs(string text)
    {
        Assert.Equal(text, TextSanitizer.Sanitize(text));
    }

    [Fact]
    public void Sanitize_KeepsZeroWidthJoiner()
    {
        var text = $"a{Zwj}b";

        Assert.Equal(text, TextSanitizer.Sanitize(text));
    }

    [Fact]
    public void Sanitize_CleanTextReturnsSameInstance()
    {
        var text = "Backup NAS";

        Assert.Same(text, TextSanitizer.Sanitize(text));
    }

    [Fact]
    public void Sanitize_EveryBmpCharacter_LeavesNoControlOrBidiCharacter()
    {
        for (var value = 0; value <= 0xFFFF; value++)
        {
            var text = $"a{(char)value}b";

            var clean = TextSanitizer.Sanitize(text);

            AssertSafe(clean);
        }
    }

    [Fact]
    public void Sanitize_IsIdempotent()
    {
        var random = new Random(2026);
        for (var i = 0; i < 2000; i++)
        {
            var text = RandomText(random, random.Next(0, 40));

            var once = TextSanitizer.Sanitize(text);

            Assert.Equal(once, TextSanitizer.Sanitize(once));
            AssertSafe(once);
        }
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 3)]
    [InlineData("\U0001F389", 1)]
    [InlineData("Cafe\u0301", 5)]
    [InlineData("\U0001F468\u200D\U0001F469", 3)]
    public void CountCharacters_CountsScalarValues(string text, int expected)
    {
        Assert.Equal(expected, TextSanitizer.CountCharacters(text));
    }

    private static void AssertSafe(string clean)
    {
        Assert.Equal(clean.Trim(), clean);
        Assert.DoesNotContain("  ", clean, StringComparison.Ordinal);
        foreach (var c in clean)
        {
            Assert.False(char.GetUnicodeCategory(c) == UnicodeCategory.Control, $"U+{(int)c:X4} left in");
            Assert.False(c is (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069) or (char)0x200E or Rlm or (char)0x061C or Bom, $"U+{(int)c:X4} left in");
            Assert.True(c == ' ' || !char.IsWhiteSpace(c), $"U+{(int)c:X4} left in");
        }
        for (var i = 0; i < clean.Length; i++)
        {
            if (char.IsHighSurrogate(clean[i]))
            {
                Assert.True(i + 1 < clean.Length && char.IsLowSurrogate(clean[i + 1]), "Lone high surrogate left in");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(clean[i]), "Lone low surrogate left in");
            }
        }
    }

    /// <summary>Mixes plain letters with the characters the sanitizer must deal with.</summary>
    private static string RandomText(Random random, int length)
    {
        char[] tricky = ['\n', '\t', ' ', Nbsp, Rlo, Lri, Rlm, (char)0x0000, (char)0x009F, (char)0xD83C, (char)0xDF89, Bom, (char)0x2028, 'a', 'Z', (char)0x062C];
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            builder.Append(random.Next(3) == 0 ? (char)random.Next(0, 0x10000) : tricky[random.Next(tricky.Length)]);
        return builder.ToString();
    }
}
