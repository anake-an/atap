using Atap.Core.Lyrics;

namespace Atap.Core.Tests.Lyrics;

public class LrcParserTests
{
    // Invented lines in the shape LRCLIB returns: a space after each timestamp and an empty line at the end.
    private const string LampuKota = """
        [ti:Lampu Kota]
        [ar:Hujan Petang]
        [al:Senja di Pantai Timur]
        [length: 03:30]
        [by:Atap tests]
        [00:12.40] Lampu kota menyala satu satu
        [00:17.85] Aku menunggu bas terakhir pulang
        [00:23.10] Hujan petang masih di bahu
        [00:28.60]
        [00:41.02][01:35.50] Bawa aku pulang ke jalan yang terang
        [00:46.75] Walau malam panjang
        [03:25.72]
        """;

    [Fact]
    public void Parse_LrclibStyleText_ReadsEveryTimedLineInOrder()
    {
        var document = LrcParser.Parse(LampuKota);

        Assert.Equal(
            [
                Line(0, 12, 400, "Lampu kota menyala satu satu"),
                Line(0, 17, 850, "Aku menunggu bas terakhir pulang"),
                Line(0, 23, 100, "Hujan petang masih di bahu"),
                Line(0, 28, 600, ""),
                Line(0, 41, 20, "Bawa aku pulang ke jalan yang terang"),
                Line(0, 46, 750, "Walau malam panjang"),
                Line(1, 35, 500, "Bawa aku pulang ke jalan yang terang"),
                Line(3, 25, 720, ""),
            ],
            document.Lines);
        Assert.True(document.HasLines);
        Assert.Equal(TimeSpan.Zero, document.Offset);
    }

    [Fact]
    public void Parse_HeaderTags_AreReadByLowerCaseName()
    {
        var document = LrcParser.Parse(LampuKota);

        Assert.Equal("Lampu Kota", document.Title);
        Assert.Equal("Hujan Petang", document.Artist);
        Assert.Equal("Senja di Pantai Timur", document.Album);
        Assert.Equal(new TimeSpan(0, 3, 30), document.Length);
        Assert.Equal("Atap tests", document.Tag("BY"));
        Assert.Null(document.Tag("re"));
    }

    [Fact]
    public void Parse_TagValue_KeepsColonsAndUnknownTags()
    {
        var document = LrcParser.Parse("[TI:Bab 2: Pulang]\n[#:made by hand]\n[x-tool:Editor 1.0]\n[00:01.00]Satu");

        Assert.Equal("Bab 2: Pulang", document.Title);
        Assert.Equal("made by hand", document.Tag("#"));
        Assert.Equal("Editor 1.0", document.Tag("x-tool"));
        Assert.Null(document.Length);
    }

    [Fact]
    public void Parse_PositiveOffset_ShowsLinesSooner()
    {
        var document = LrcParser.Parse("[offset:+500]\n[00:10.00]Satu\n[00:20.00]Dua");

        Assert.Equal(TimeSpan.FromMilliseconds(500), document.Offset);
        Assert.Equal([TimeSpan.FromSeconds(9.5), TimeSpan.FromSeconds(19.5)], document.Lines.Select(l => l.Time));
    }

    [Fact]
    public void Parse_NegativeOffset_ShowsLinesLater()
    {
        var document = LrcParser.Parse("[offset: -250]\n[00:10.00]Satu");

        Assert.Equal(TimeSpan.FromMilliseconds(-250), document.Offset);
        Assert.Equal(TimeSpan.FromSeconds(10.25), document.Lines[0].Time);
    }

    [Fact]
    public void Parse_OffsetAfterTheLines_StillApplies()
    {
        var document = LrcParser.Parse("[00:10.00]Satu\n[offset:1000]");

        Assert.Equal(TimeSpan.FromSeconds(9), document.Lines[0].Time);
    }

    [Fact]
    public void Parse_OffsetLargerThanTheTime_ClampsAtZeroAndKeepsTimeOrder()
    {
        var document = LrcParser.Parse("[offset:+3000]\n[00:02.00]Dua\n[00:01.00]Satu\n[00:05.00]Lima");

        Assert.Equal(
            [Line(0, 0, 0, "Satu"), Line(0, 0, 0, "Dua"), Line(0, 2, 0, "Lima")],
            document.Lines);
    }

    [Theory]
    [InlineData("[offset:abc]")]
    [InlineData("[offset:1.5]")]
    [InlineData("[offset:99999999999]")]
    [InlineData("[offset:]")]
    public void Parse_UnreadableOffset_IsIgnored(string tag)
    {
        var document = LrcParser.Parse(tag + "\n[00:10.00]Satu");

        Assert.Equal(TimeSpan.Zero, document.Offset);
        Assert.Equal(TimeSpan.FromSeconds(10), document.Lines[0].Time);
    }

    [Fact]
    public void Parse_SeveralTimestamps_RepeatTheLineAtEachTime()
    {
        var document = LrcParser.Parse("[00:30.00][00:05.00] [01:10.00]Korus\n[00:20.00]Bait");

        Assert.Equal(
            [Line(0, 5, 0, "Korus"), Line(0, 20, 0, "Bait"), Line(0, 30, 0, "Korus"), Line(1, 10, 0, "Korus")],
            document.Lines);
    }

    [Fact]
    public void Parse_SameTimestamp_KeepsFileOrder()
    {
        var document = LrcParser.Parse("[00:05.00]Asal\n[00:05.00]Terjemahan\n[00:01.00]Mula");

        Assert.Equal(["Mula", "Asal", "Terjemahan"], document.Lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData("01:02", 62_000)]
    [InlineData("1:02", 62_000)]
    [InlineData("01:02.3", 62_300)]
    [InlineData("01:02.34", 62_340)]
    [InlineData("01:02.345", 62_345)]
    [InlineData("01:02.3456", 62_345)]
    [InlineData("01:02:34", 62_340)]
    [InlineData("01:02,34", 62_340)]
    [InlineData(" 00:00.00 ", 0)]
    [InlineData("125:00.00", 7_500_000)]
    [InlineData("00:7.5", 7_500)]
    public void TryParseTime_ReadsEveryCommonForm(string value, int milliseconds)
    {
        Assert.True(LrcParser.TryParseTime(value, out var time));
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("01:")]
    [InlineData(":02")]
    [InlineData("ab:cd")]
    [InlineData("01:2x")]
    [InlineData("01:02.")]
    [InlineData("01:02.3a")]
    [InlineData("01:123")]
    [InlineData("-01:02")]
    [InlineData("+01:02")]
    [InlineData("123456:00")]
    [InlineData("01 :02")]
    [InlineData("01:٠٢")]
    public void TryParseTime_RejectsAnythingElse(string value)
    {
        Assert.False(LrcParser.TryParseTime(value, out _));
    }

    [Fact]
    public void Parse_WordTimings_AreRemovedFromTheText()
    {
        var document = LrcParser.Parse("[00:01.00]<00:01.00>Lampu <00:01.40>kota <00:01.90> menyala <a>b</a>");

        Assert.Equal("Lampu kota menyala <a>b</a>", document.Lines[0].Text);
    }

    [Fact]
    public void Parse_UntimedAndMalformedLines_AreSkipped()
    {
        const string text = """
            Just a plain line
            [Chorus]
            [00:aa]Not a time
            [00:05.00 unclosed
            []
            [:]
            [00:06.00]Masih dibaca

            [ar:Hujan Petang] trailing text
            [00:07.00][Korus] dalam kurungan
            """;

        var document = LrcParser.Parse(text);

        Assert.Equal(["Masih dibaca", "[Korus] dalam kurungan"], document.Lines.Select(l => l.Text));
        Assert.Equal("Hujan Petang", document.Artist);
    }

    [Theory]
    [InlineData("\uFEFF[00:01.00]Satu\r\n[00:02.00]Dua")]
    [InlineData("[00:01.00]Satu\n[00:02.00]Dua\n")]
    [InlineData("[00:01.00]Satu\r[00:02.00]Dua")]
    [InlineData("  [00:01.00]  Satu  \u2028\t[00:02.00]Dua\t")]
    public void Parse_LineEndingsBomAndSpaces_AreHandled(string text)
    {
        var document = LrcParser.Parse(text);

        Assert.Equal([Line(0, 1, 0, "Satu"), Line(0, 2, 0, "Dua")], document.Lines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Lampu kota menyala\nAku menunggu")]
    public void Parse_NoTimedLines_GivesAnEmptyDocument(string? text)
    {
        var document = LrcParser.Parse(text);

        Assert.False(document.HasLines);
        Assert.Empty(document.Lines);
    }

    [Fact]
    public void Parse_RunawayInput_StopsAtMaxLines()
    {
        var text = string.Concat(Enumerable.Repeat("[00:01.00][00:02.00]x\n", LrcParser.MaxLines));

        var document = LrcParser.Parse(text);

        Assert.Equal(LrcParser.MaxLines, document.Lines.Count);
    }

    [Fact]
    public void Parse_TooManyTags_KeepsTheFirstOnesAndStillUpdatesThem()
    {
        var tags = string.Concat(Enumerable.Range(0, 200).Select(i => $"[t{i}:v]\n"));

        var document = LrcParser.Parse("[ti:Satu]\n" + tags + "[ti:Dua]\n[00:01.00]x");

        Assert.Equal(64, document.Tags.Count);
        Assert.Equal("Dua", document.Title);
        Assert.Null(document.Tag("t150"));
    }

    [Fact]
    public void Parse_LinesCannotBeChangedFromOutside()
    {
        var document = LrcParser.Parse(LampuKota);

        Assert.False(document.Lines is LrcLine[]);
        Assert.Throws<NotSupportedException>(() => ((IList<LrcLine>)document.Lines).Clear());
    }

    [Fact]
    public void Parse_MalformedCorpus_NeverThrows()
    {
        string[] corpus =
        [
            "[", "]", "[[[[", "]]]]", "[:", "[0:", "[00:00", "[00:00.", "<00:01.00", "[00:01.00]<", "[00:01.00]<>",
            "[00:01.00]<00:0", "[99999:99.999]", "[00000:00]", "[00:00.00000000000000000000000001]x",
            "[offset:-]", "[offset:+]", "[offset:9223372036854775807]", "[offset:-9223372036854775808]",
            "\uD800[00:01.00]\uDC00", "[00:01.00]\0\0\0", new string('[', 10_000), "[a:" + new string('b', 10_000) + "]",
            "[" + new string('1', 50) + ":00]", "[ti:]", "[ :x]", "[#]", "\u0085\u2029\f\v",
            "[00:01.00]" + new string('<', 200_000), "[00:01.00]" + string.Concat(Enumerable.Repeat("<1:", 50_000)) + ">",
        ];

        foreach (var text in corpus)
        {
            var document = LrcParser.Parse(text);
            Assert.NotNull(document);
            Assert.All(document.Lines, line => Assert.True(line.Time >= TimeSpan.Zero));
        }

        var random = new Random(20261025);
        const string alphabet = "[]<>:.,+-0123456789 \nabc#\r\t";
        for (var round = 0; round < 500; round++)
        {
            var chars = new char[random.Next(0, 200)];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[random.Next(alphabet.Length)];

            var document = LrcParser.Parse(new string(chars));
            Assert.All(document.Lines, line => Assert.True(line.Time >= TimeSpan.Zero));
        }
    }

    private static LrcLine Line(int minutes, int seconds, int milliseconds, string text) =>
        new(new TimeSpan(0, 0, minutes, seconds, milliseconds), text);
}
