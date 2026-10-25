using System.Text;
using Atap.Core.LocalApi;

namespace Atap.Core.Tests.LocalApi;

/// <summary>Parse must never throw. These feed it broken, hostile and random input.</summary>
public class PushProtocolCorpusTests
{
    /// <summary>One message per line. Lines are hand-made breakage and known parser traps.</summary>
    private const string MalformedCorpus = """
        {
        }
        {}
        []
        [[]]
        ""
        "op"
        0
        -0
        1e999
        true
        null
        NaN
        Infinity
        {"op"}
        {"op":}
        {"op":"push",}
        {"op":"push" "id":"a"}
        {"op":"push","id":"a","title":"x"
        {"op":"push","id":"a","title":"x"}}
        {"op":"push","id":"a","title":"x"}{"op":"end","id":"a"}
        {'op':'ping'}
        {op:"ping"}
        {"op":"ping"//}
        {"op":"ping"/**/}
        {"op":NaN}
        {"op":Infinity}
        {"op":-Infinity}
        {"op":undefined}
        {"op":"ping\u0000"}
        {"op":"\u0070ing"}
        {"op":"pi\ng"}
        {"op":"ping","op":"ping"}
        {"OP":"ping","op":"ping"}
        {"op":["ping"]}
        {"op":{"name":"ping"}}
        {"op":true}
        {"op":0}
        {"op":"push","id":{"$ne":null},"title":"x"}
        {"op":"push","id":["a"],"title":"x"}
        {"op":"push","id":"a","title":{"text":"x"}}
        {"op":"push","id":"a","title":"x","progress":1e309}
        {"op":"push","id":"a","title":"x","progress":-1e309}
        {"op":"push","id":"a","title":"x","progress":0x40}
        {"op":"push","id":"a","title":"x","progress":064}
        {"op":"push","id":"a","title":"x","progress":.5}
        {"op":"push","id":"a","title":"x","progress":5.}
        {"op":"push","id":"a","title":"x","progress":+5}
        {"op":"push","id":"a","title":"x","progress":1e}
        {"op":"push","id":"a","title":"x","ttl":99999999999999999999999999999999}
        {"op":"push","id":"a","title":"x","ttl":-9223372036854775808}
        {"op":"push","id":"a","title":"x","ttl":9223372036854775807}
        {"op":"push","id":"a","title":"x","ttl":"999999h"}
        {"op":"push","id":"a","title":"x","ttl":"9999999999h"}
        {"op":"push","id":"a","title":"x","ttl":"P1D"}
        {"op":"push","id":"a","title":"x","ttl":"00:10:00"}
        {"op":"push","id":"a","title":"\ud800"}
        {"op":"push","id":"a","title":"\udc00\ud800"}
        {"op":"push","id":"a","title":"\uZZZZ"}
        {"op":"push","id":"a","title":"\u12"}
        {"op":"push","id":"a","title":"\x41"}
        {"op":"push","id":"a","title":"\"}
        {"op":"push","id":"a","title":"x\
        {"op":"push","id":"a","\ud800":"x"}
        {"op":"push","id":"a","title":"x","__proto__":{"admin":true}}
        {"op":"push","id":"a","title":"x","constructor":{"prototype":{}}}
        {"op":"push","id":"a","title":"x","$type":"System.IO.FileInfo"}
        {"op":"push","id":"..\\..\\Windows","title":"x"}
        {"op":"push","id":"con","title":"x","icon":"..\\..\\x.png"}
        {"op":"push","id":"a","title":"x","icon":"file:///C:/Windows/win.ini"}
        {"op":"push","id":"a","title":"x","accent":"rgb(255,0,0)"}
        {"op":"push","id":"a","title":"x","url":"https://example.com"}
        {"op":"push","id":"a","title":"x","image":"https://example.com/a.png"}
        {"op":"push","id":"a","title":"x","action":"cmd.exe /c calc"}
        {"op":"alert","title":"x","sound":"C:\\Windows\\Media\\tada.wav"}
        {"op":"end","id":"a","state":"done"}
        {"op":"ping","extra":[1,2,3]}
        {"op":"run","command":"calc.exe"}
        {"op":"open","url":"ms-settings:"}
        {"op":"exec"}
        {"op":"push","id":"a","title":"<script>alert(1)</script>","subtitle":"{Binding Path=Secret}"}
        {"op":"push","id":"a","title":"x","subtitle":"\u202Egnp.exe"}
        {"op":"push","id":"a","title":"x","state":"running","state":"done"}
        {"op":"push","id":"a","title":"x","progress":50,"Progress":51}
        {"op":"push","id":"a","title":"x"} trailing
        {"op":"ping"}
        """;

    /// <summary>Well-formed messages used as seeds for truncation and byte flips.</summary>
    private static readonly string[] Seeds =
    [
        """{"op":"ping"}""",
        """{"op":"push","id":"backup","title":"Backup NAS","subtitle":"Disk 2","progress":64,"state":"running","icon":"backup","accent":"blue","ttl":600}""",
        """{"op": "push", "id": "solat-sync", "title": "Muat turun waktu solat", "subtitle": "Zon SGR01 \ud83d\udd4c", "progress": 37.5, "ttl": "30m"}""",
        """{"op":"end","id":"backup"}""",
        """{"op":"alert","title":"Build gagal","subtitle":"main \u2192 ujian","icon":"error"}""",
    ];

    /// <summary>Bytes that tend to break JSON or UTF-8 when dropped into a valid message.</summary>
    private static readonly byte[] TrickyBytes = [0x00, 0x0A, 0x22, 0x2C, 0x3A, 0x5B, 0x5C, 0x5D, 0x7B, 0x7D, 0x80, 0xBF, 0xC0, 0xE2, 0xED, 0xEF, 0xF4, 0xFF];

    private static string[] Lines => MalformedCorpus.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    public static TheoryData<string> CorpusLines => new(Lines);

    [Theory]
    [MemberData(nameof(CorpusLines))]
    public void Parse_CorpusLine_NeverThrows(string line)
    {
        AssertWellBehaved(PushProtocol.Parse(line));
        AssertWellBehaved(PushProtocol.Parse(Encoding.UTF8.GetBytes(line)));
        PushProtocol.TryParseResponse(line, out _);
    }

    [Fact]
    public void Parse_Corpus_AcceptsOnlyTheHarmlessLines()
    {
        var accepted = Lines
            .Where(line => PushProtocol.Parse(line).IsSuccess)
            .ToList();

        // Escaped "p" is still "ping", the tag soup is only text, and the last line is a plain ping.
        Assert.Equal(
            new[]
            {
                """{"op":"\u0070ing"}""",
                """{"op":"push","id":"a","title":"<script>alert(1)</script>","subtitle":"{Binding Path=Secret}"}""",
                """{"op":"push","id":"a","title":"x","subtitle":"\u202Egnp.exe"}""",
                """{"op":"ping"}""",
            },
            accepted);
    }

    [Fact]
    public void Parse_ByteOrderMarks_OnlyOneIsSkipped()
    {
        var bom = ((char)0xFEFF).ToString();
        var ping = """{"op":"ping"}""";

        Assert.True(PushProtocol.Parse(bom + ping).IsSuccess);
        AssertWellBehaved(PushProtocol.Parse(bom + bom + ping));
        Assert.False(PushProtocol.Parse(bom + bom + ping).IsSuccess);
        Assert.False(PushProtocol.Parse(bom).IsSuccess);
    }

    [Fact]
    public void Parse_EveryPrefixOfEverySeed_NeverThrows()
    {
        foreach (var seed in Seeds)
        {
            for (var length = 0; length <= seed.Length; length++)
            {
                var prefix = seed[..length];
                var result = PushProtocol.Parse(prefix);
                AssertWellBehaved(result);
                if (length < seed.Length)
                    Assert.False(result.IsSuccess, $"Prefix parsed: {prefix}");
            }
        }
    }

    [Fact]
    public void Parse_EveryByteFlipOfEverySeed_NeverThrows()
    {
        var count = 0;
        foreach (var seed in Seeds)
        {
            var bytes = Encoding.UTF8.GetBytes(seed);
            for (var position = 0; position < bytes.Length; position++)
            {
                foreach (var tricky in TrickyBytes)
                {
                    var mutated = (byte[])bytes.Clone();
                    mutated[position] = tricky;
                    AssertWellBehaved(PushProtocol.Parse(mutated));
                    count++;
                }

                var removed = bytes[..position].Concat(bytes[(position + 1)..]).ToArray();
                AssertWellBehaved(PushProtocol.Parse(removed));
                count++;
            }
        }
        Assert.True(count > 1000);
    }

    [Fact]
    public void Parse_RandomBytes_NeverThrows()
    {
        var random = new Random(7001);
        for (var i = 0; i < 5000; i++)
        {
            var bytes = new byte[random.Next(0, 300)];
            random.NextBytes(bytes);
            AssertWellBehaved(PushProtocol.Parse(bytes));
            AssertWellBehaved(PushProtocol.Parse(Encoding.Latin1.GetString(bytes)));
        }
    }

    [Fact]
    public void Parse_RandomJsonLikeText_NeverThrows()
    {
        string[] tokens =
        [
            "{", "}", "[", "]", ":", ",", "\"", "\\", "\\u", "\\ud800", "\\udc00", "\\u202e", "null", "true", "false",
            "-", "0", "1e309", ".", "64", "86400", " ", "\n", "\"op\"", "\"push\"", "\"ping\"", "\"end\"", "\"alert\"",
            "\"id\"", "\"title\"", "\"subtitle\"", "\"progress\"", "\"state\"", "\"icon\"", "\"accent\"", "\"ttl\"",
            "\"a\"", "\"10m\"", "\"backup\"", "\"blue\"", "\u202E", "\uD800", "\uFEFF", "\u0000", "\U0001F389",
        ];
        var random = new Random(4242);
        var accepted = 0;
        for (var i = 0; i < 20000; i++)
        {
            var builder = new StringBuilder();
            var parts = random.Next(1, 40);
            for (var p = 0; p < parts; p++)
                builder.Append(tokens[random.Next(tokens.Length)]);

            var result = PushProtocol.Parse(builder.ToString());
            AssertWellBehaved(result);
            if (result.IsSuccess)
                accepted++;
        }
        Assert.True(accepted < 20000);
    }

    [Fact]
    public void Parse_RandomFieldValues_OnlyAcceptsWhatValidatePasses()
    {
        string[] values = ["null", "\"\"", "\"a\"", "\"Backup\"", "\"-a\"", "0", "-1", "9", "10", "64", "100", "101", "86400", "86401", "true", "[]", "{}", "\"10m\"", "\"25h\"", "\"blue\"", "\"red\"", "\"done\"", "\"info\"", "1.5"];
        string[] fields = ["id", "title", "subtitle", "progress", "state", "icon", "accent", "ttl"];
        var random = new Random(99);
        for (var i = 0; i < 5000; i++)
        {
            var builder = new StringBuilder("{\"op\":\"push\"");
            foreach (var field in fields)
            {
                if (random.Next(4) != 0)
                    builder.Append($",\"{field}\":{values[random.Next(values.Length)]}");
            }
            builder.Append('}');

            var result = PushProtocol.Parse(builder.ToString());
            AssertWellBehaved(result);
            if (result.IsSuccess)
            {
                Assert.Null(PushProtocol.Validate(result.Request));
                Assert.Equal(result.Request, PushProtocol.Parse(PushProtocol.Format(result.Request)).Request);
            }
        }
    }

    [Fact]
    public void Parse_HugeInputs_NeverThrow()
    {
        AssertWellBehaved(PushProtocol.Parse(new string('{', 1_000_000)));
        AssertWellBehaved(PushProtocol.Parse(new string('[', 8000)));
        AssertWellBehaved(PushProtocol.Parse(new byte[1_000_000]));
        AssertWellBehaved(PushProtocol.Parse("{\"op\":\"push\",\"id\":\"a\",\"title\":\"" + new string('\u202E', 2000) + "\"}"));
        AssertWellBehaved(PushProtocol.Parse("{" + string.Join(",", Enumerable.Range(0, 900).Select(i => $"\"f{i}\":{i}")) + "}"));
    }

    private static void AssertWellBehaved(ApiParseResult result)
    {
        Assert.NotNull(result);
        Assert.True(result.Request is null != (result.Error is null), "Exactly one of request and error must be set.");
        if (result.Error is not null)
            Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }
}
