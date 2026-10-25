using System.Text;
using System.Text.Json;
using Atap.Core.Custom;
using static Atap.Core.Tests.Custom.CustomFixtures;

namespace Atap.Core.Tests.Custom;

public class PayloadReaderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    [InlineData("\uFEFF")]
    public void EmptyText_IsEmptyWithANullRoot(string? text)
    {
        var result = PayloadReader.Read(text);

        Assert.Equal(PayloadStatus.Empty, result.Status);
        Assert.False(result.IsOk);
        Assert.Equal(JsonValueKind.Null, result.Root.ValueKind);
    }

    [Fact]
    public void Json_IsParsedInEveryFormatButPlain()
    {
        Assert.Equal(JsonValueKind.Object, PayloadReader.Read(NasStatus, PayloadFormat.Json).Root.ValueKind);
        Assert.Equal(JsonValueKind.Object, PayloadReader.Read(NasStatus, PayloadFormat.Auto).Root.ValueKind);
        Assert.Equal(JsonValueKind.Array, PayloadReader.Read(DockerContainers).Root.ValueKind);
        Assert.Equal(JsonValueKind.String, PayloadReader.Read(NasStatus, PayloadFormat.Plain).Root.ValueKind);
    }

    [Theory]
    [InlineData("72.5", "72.5")]
    [InlineData("  72.5 \n", "72.5")]
    [InlineData("true", "true")]
    [InlineData("null", "null")]
    [InlineData("Backup OK\nTook 3 min", "Backup OK\nTook 3 min")]
    [InlineData("{not json", "{not json")]
    [InlineData("<html>", "<html>")]
    public void AutoFormat_PlainTextIsOneTrimmedString(string text, string expected)
    {
        var result = PayloadReader.Read(text);

        Assert.Equal(PayloadStatus.Ok, result.Status);
        Assert.Equal(expected, result.Root.GetString());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("[1, 2,]")]
    public void JsonFormat_RefusesTextThatDoesNotParse(string text)
    {
        var result = PayloadReader.Read(text, PayloadFormat.Json);

        Assert.Equal(PayloadStatus.InvalidJson, result.Status);
    }

    [Fact]
    public void JsonFormat_RefusesVeryDeepDocuments()
    {
        var deep = new string('[', 100) + new string(']', 100);

        Assert.Equal(PayloadStatus.InvalidJson, PayloadReader.Read(deep, PayloadFormat.Json).Status);
        Assert.Equal(JsonValueKind.String, PayloadReader.Read(deep).Root.ValueKind);
    }

    [Fact]
    public void JsonFormat_DepthIsCappedAt32()
    {
        static string Nested(int depth) => new string('[', depth) + new string(']', depth);

        Assert.Equal(PayloadStatus.Ok, PayloadReader.Read(Nested(PayloadReader.MaxDepth), PayloadFormat.Json).Status);
        Assert.Equal(PayloadStatus.InvalidJson, PayloadReader.Read(Nested(PayloadReader.MaxDepth + 1), PayloadFormat.Json).Status);
        Assert.Equal(PayloadStatus.InvalidJson, PayloadReader.Read(Encoding.UTF8.GetBytes(Nested(PayloadReader.MaxDepth + 1)), PayloadFormat.Json).Status);
    }

    [Fact]
    public void Limits_MatchThePlan()
    {
        Assert.Equal(32, PayloadReader.MaxDepth);
        Assert.Equal(256 * 1024, PayloadReader.JsonUrlMaxBytes);
        Assert.Equal(PayloadReader.JsonUrlMaxBytes, PayloadReader.DefaultMaxBytes);
        Assert.Equal(PayloadStatus.TooLarge, PayloadReader.Read(new string('1', PayloadReader.JsonUrlMaxBytes + 1)).Status);
        Assert.Equal(PayloadStatus.Ok, PayloadReader.Read(new string('1', PayloadReader.JsonUrlMaxBytes)).Status);
    }

    [Fact]
    public void Text_OverTheCapIsTooLarge()
    {
        Assert.Equal(PayloadStatus.TooLarge, PayloadReader.Read("abcdef", maxBytes: 5).Status);
        Assert.Equal(PayloadStatus.Ok, PayloadReader.Read("abcde", maxBytes: 5).Status);
        // Three characters of two UTF-8 bytes each.
        Assert.Equal(PayloadStatus.TooLarge, PayloadReader.Read("ééé", maxBytes: 5).Status);
        Assert.Equal(PayloadStatus.Ok, PayloadReader.Read("ééé", maxBytes: 6).Status);
    }

    [Fact]
    public void Bytes_OverTheCapIsTooLarge()
    {
        Assert.Equal(PayloadStatus.TooLarge, PayloadReader.Read("abcdef"u8, maxBytes: 5).Status);
        Assert.Equal(PayloadStatus.Ok, PayloadReader.Read("abcde"u8, maxBytes: 5).Status);
    }

    [Fact]
    public void Bytes_ReadLikeText()
    {
        var result = PayloadReader.Read(Encoding.UTF8.GetBytes(PiHoleSummary), PayloadFormat.Json);

        Assert.Equal(48213, result.Root.GetProperty("queries").GetProperty("total").GetInt32());
    }

    [Fact]
    public void Bytes_ThatAreNotUtf8AreInvalidText()
    {
        Assert.Equal(PayloadStatus.InvalidText, PayloadReader.Read(new byte[] { 0x7B, 0xFF, 0x7D }).Status);
    }

    [Fact]
    public void Text_WithLoneSurrogateDoesNotThrow()
    {
        var result = PayloadReader.Read("a\uD800b", PayloadFormat.Plain);

        Assert.Equal("a\uFFFDb", result.Root.GetString());
    }

    [Fact]
    public void JsonText_WithLoneSurrogateIsReadWithAReplacement()
    {
        // Theory data cannot carry lone surrogates, so this is a fact. These are raw characters, not JSON escapes.
        Assert.Equal("\uFFFD", PayloadReader.Read("{\"a\":\"\uD800\"}").Root.GetProperty("a").GetString());
        Assert.Equal("x\uFFFD", PayloadReader.Read("{\"a\":\"x\uD800\"}", PayloadFormat.Json).Root.GetProperty("a").GetString());
        Assert.Equal("\uFFFD", PayloadReader.Read("\"\uD800\"", PayloadFormat.Json).Root.GetString());
        Assert.Equal("\uFFFD", PayloadReader.Read("\"\uD800\"").Root.GetString());
        Assert.Equal("\uFFFD", PayloadReader.Read("[\"\uDC00\"]").Root[0].GetString());
        Assert.Equal("\uFFFD", PayloadReader.Read("{\"\uDBFF\":1}", PayloadFormat.Json).Root.EnumerateObject().Single().Name);

        // A pair that makes one character is kept.
        Assert.Equal("\uD83D\uDE00", PayloadReader.Read("[\"\uD83D\uDE00\"]", PayloadFormat.Json).Root[0].GetString());

        // Broken JSON with a lone surrogate is still refused or read as text.
        Assert.Equal(PayloadStatus.InvalidJson, PayloadReader.Read("{\"a\":\uD800}", PayloadFormat.Json).Status);
        Assert.Equal("{\"a\":\uFFFD}", PayloadReader.Read("{\"a\":\uD800}").Root.GetString());
    }

    [Fact]
    public void JsonText_WithLoneSurrogateEscapeIsReadWithAReplacement()
    {
        // JSON escapes, not raw characters. They are rewritten before parsing, so System.Text.Json reads them without throwing.
        Assert.Equal("\uFFFD", PayloadReader.Read("\"\\uD800\"").Root.GetString());
        Assert.Equal("a\uFFFDb", PayloadReader.Read("[\"a\\udc00b\"]", PayloadFormat.Json).Root[0].GetString());
        Assert.Equal("\uFFFD", PayloadReader.Read("{\"\\uD800\":0}").Root.EnumerateObject().Single().Name);
        Assert.Equal(1, PayloadReader.Read("{\"\\uD800\":0,\"x\":1}").Root.GetProperty("x").GetInt32());
        Assert.Equal("\uFFFDA", PayloadReader.Read("\"\\uD83D\\u0041\"").Root.GetString());
        Assert.Equal("\uFFFD\uFFFD", PayloadReader.Read("\"\\uDE00\\uD83D\"").Root.GetString());
        Assert.Equal("\uFFFD\uD83D\uDE00", PayloadReader.Read("\"\\uD800\\uD83D\\uDE00\"").Root.GetString());

        // A pair of escapes that makes one character is kept.
        Assert.Equal("\uD83D\uDE00", PayloadReader.Read("\"\\uD83D\\uDE00\"").Root.GetString());

        // An escaped backslash does not start an escape.
        Assert.Equal("\\uD800", PayloadReader.Read("\"\\\\uD800\"").Root.GetString());

        // Text that falls back to a plain string keeps what was sent.
        Assert.Equal("{\\uD800", PayloadReader.Read("{\\uD800").Root.GetString());
    }

    [Fact]
    public void LargePayloadOfBrokenEscapes_ReadsWithoutTheSlowFallback()
    {
        // Every name read here would throw inside System.Text.Json without the rewrite.
        var json = "[" + string.Join(",", Enumerable.Repeat("{\"\\uD800\":0,\"x\":1}", 3_000)) + "]";
        var root = PayloadReader.Read(json, PayloadFormat.Json).Root;

        Assert.All(root.EnumerateArray(), item => Assert.Equal("\uFFFD", item.EnumerateObject().First().Name));
        Assert.Equal(3_000, JsonPathLite.Parse("$[*].x").Evaluate(root).Matches.Count);
    }

    [Fact]
    public void RandomText_NeverThrows()
    {
        var random = new Random(31);
        char[] pieces = ['{', '}', '[', ']', '"', ':', ',', '\\', 'a', '1', ' ', '\uD800', '\uDBFF', '\uDC00', '\uDFFF', '\uD83D', '\uFEFF'];
        var template = Template(new ValueTemplateSpec(ValueTemplateKind.Text, "$[0]"));

        for (var i = 0; i < 5_000; i++)
        {
            var chars = new char[random.Next(0, 32)];
            for (var j = 0; j < chars.Length; j++)
                chars[j] = pieces[random.Next(pieces.Length)];
            if (chars.Length > 0)
                chars[0] = "{[\""[random.Next(3)];
            var text = new string(chars);

            foreach (var format in Enum.GetValues<PayloadFormat>())
                template.Render(PayloadReader.Read(text, format));
        }
    }

    [Fact]
    public void Root_OutlivesTheInput()
    {
        var result = PayloadReader.Read(Encoding.UTF8.GetBytes(NasStatus));
        GC.Collect();

        Assert.Equal(72.5, result.Root.GetProperty("disk").GetProperty("usedPercent").GetDouble());
    }

    [Fact]
    public void RandomBytes_NeverThrow()
    {
        var random = new Random(99);
        var template = Template(new ValueTemplateSpec(ValueTemplateKind.Text, "$[0]"));

        for (var i = 0; i < 5_000; i++)
        {
            var bytes = new byte[random.Next(0, 64)];
            random.NextBytes(bytes);
            if (bytes.Length > 0 && random.Next(2) == 0)
                bytes[0] = (byte)'[';

            foreach (var format in Enum.GetValues<PayloadFormat>())
                template.Render(PayloadReader.Read(bytes, format));
        }
    }
}
