using Atap.Core.Lyrics;

namespace Atap.Core.Tests.Lyrics;

public class LyricsRomanizerTests
{
    [Theory]
    [InlineData("사랑해", "saranghae")]
    [InlineData("ありがとう", "arigatou")]
    [InlineData("사랑 と 夢", "sarang to 夢")]
    [InlineData("Lampu kota", "Lampu kota")]
    [InlineData("", "")]
    public void Romanize_HandlesHangulAndKanaTogether(string text, string expected)
    {
        Assert.Equal(expected, LyricsRomanizer.Romanize(text));
    }

    [Theory]
    [InlineData("사랑해", true)]
    [InlineData("カタカナ", true)]
    [InlineData("\u1112\u1161\u11AB", true)]
    [InlineData("東京", false)]
    [InlineData("Lampu kota", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanRomanize_TellsWhetherAnExtraRowIsUseful(string? text, bool expected)
    {
        Assert.Equal(expected, LyricsRomanizer.CanRomanize(text));
    }
}
