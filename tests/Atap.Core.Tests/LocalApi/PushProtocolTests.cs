using System.Text;
using Atap.Core.LocalApi;

namespace Atap.Core.Tests.LocalApi;

public class PushProtocolTests
{
    // What the CLI sends for: atap push --id backup --title "Backup NAS" --subtitle "Disk 2" --progress 64 --icon backup --accent blue --ttl 10m
    private const string CliPush = """{"op":"push","id":"backup","title":"Backup NAS","subtitle":"Disk 2","progress":64,"state":"running","icon":"backup","accent":"blue","ttl":600}""";

    // Windows PowerShell 5.1: @{ Op='push'; Id='backup'; Title="Ali's backup"; Progress=64; Subtitle=$null } | ConvertTo-Json
    private const string PowerShell51Push = """
        {
            "Op":  "push",
            "Id":  "backup",
            "Title":  "Ali\u0027s backup",
            "Progress":  64,
            "Subtitle":  null
        }
        """;

    // PowerShell 7: [ordered]@{ op='push'; id='rsync-nas'; title='Sandaran NAS'; state='done'; ttl='1h' } | ConvertTo-Json -Compress
    private const string PowerShell7Push = """{"op":"push","id":"rsync-nas","title":"Sandaran NAS","state":"done","ttl":"1h"}""";

    // Python: json.dumps({...}) with the default ensure_ascii and separators.
    private const string PythonPush = """{"op": "push", "id": "solat-sync", "title": "Muat turun waktu solat", "subtitle": "Zon SGR01 \ud83d\udd4c", "progress": 37.5, "state": "running", "icon": "download", "accent": "violet", "ttl": "30m"}""";

    // bash: jq -nc --arg t "Build gagal" '{op:"alert",title:$t,subtitle:"main \u2192 ujian",icon:"error"}'
    private const string JqAlert = """{"op":"alert","title":"Build gagal","subtitle":"main \u2192 ujian","icon":"error"}""";

    private const string CliEnd = """{"op":"end","id":"backup"}""";

    private const string CliPing = """{"op":"ping"}""";

    public static TheoryData<string> ValidFixtures => [CliPush, PowerShell51Push, PowerShell7Push, PythonPush, JqAlert, CliEnd, CliPing];

    [Fact]
    public void Parse_CliPush_ReadsEveryField()
    {
        var request = ParseOk<PushRequest>(CliPush);

        Assert.Equal("backup", request.Id);
        Assert.Equal("Backup NAS", request.Title);
        Assert.Equal("Disk 2", request.Subtitle);
        Assert.Equal(64, request.Progress);
        Assert.Equal(PushState.Running, request.State);
        Assert.Equal(PushIcon.Backup, request.Icon);
        Assert.Equal(PushAccent.Blue, request.Accent);
        Assert.Equal(TimeSpan.FromMinutes(10), request.Ttl);
    }

    [Fact]
    public void Parse_PowerShell51_IgnoresNameCaseAndTreatsNullAsLeftOut()
    {
        var request = ParseOk<PushRequest>(PowerShell51Push);

        Assert.Equal("backup", request.Id);
        Assert.Equal("Ali's backup", request.Title);
        Assert.Equal(64, request.Progress);
        Assert.Null(request.Subtitle);
        Assert.Null(request.Ttl);
        Assert.Equal(PushProtocol.DefaultTtl, request.EffectiveTtl);
    }

    [Fact]
    public void Parse_PowerShell7_ReadsTextDurationAndState()
    {
        var request = ParseOk<PushRequest>(PowerShell7Push);

        Assert.Equal(PushState.Done, request.State);
        Assert.Equal(TimeSpan.FromHours(1), request.Ttl);
        Assert.Null(request.Progress);
    }

    [Fact]
    public void Parse_Python_ReadsEscapedEmojiAndFractionalProgress()
    {
        var request = ParseOk<PushRequest>(PythonPush);

        Assert.Equal("Zon SGR01 \U0001F54C", request.Subtitle);
        Assert.Equal(37.5, request.Progress);
        Assert.Equal(PushIcon.Download, request.Icon);
        Assert.Equal(PushAccent.Violet, request.Accent);
        Assert.Equal(TimeSpan.FromMinutes(30), request.Ttl);
    }

    [Fact]
    public void Parse_Alert_ReadsTitleSubtitleAndIcon()
    {
        var request = ParseOk<AlertRequest>(JqAlert);

        Assert.Equal("Build gagal", request.Title);
        Assert.Equal("main \u2192 ujian", request.Subtitle);
        Assert.Equal(PushIcon.Error, request.Icon);
    }

    [Fact]
    public void Parse_EndAndPing()
    {
        Assert.Equal("backup", ParseOk<EndRequest>(CliEnd).Id);
        Assert.IsType<PingRequest>(PushProtocol.Parse(CliPing).Request);
    }

    [Fact]
    public void Parse_MinimalPush_UsesDefaults()
    {
        var request = ParseOk<PushRequest>("""{"op":"push","id":"a","title":"x"}""");

        Assert.Null(request.Subtitle);
        Assert.Null(request.Progress);
        Assert.Equal(PushState.Running, request.State);
        Assert.Null(request.Icon);
        Assert.Null(request.Accent);
        Assert.Null(request.Ttl);
        Assert.Equal(TimeSpan.FromMinutes(15), request.EffectiveTtl);
    }

    [Fact]
    public void Parse_EnumValuesIgnoreCase()
    {
        var request = ParseOk<PushRequest>("""{"op":"PUSH","id":"a","title":"x","state":"Failed","icon":"WARNING","accent":"Coral"}""");

        Assert.Equal(PushState.Failed, request.State);
        Assert.Equal(PushIcon.Warning, request.Icon);
        Assert.Equal(PushAccent.Coral, request.Accent);
    }

    [Fact]
    public void Parse_SkipsLeadingByteOrderMark()
    {
        Assert.True(PushProtocol.Parse("\uFEFF" + CliPing).IsSuccess);
    }

    [Fact]
    public void Parse_CleansTextBeforeUse()
    {
        var request = ParseOk<PushRequest>("""{"op":"push","id":"a","title":" Backup\u202E\n NAS\u0007 ","subtitle":"\u2066\t"}""");

        Assert.Equal("Backup NAS", request.Title);
        Assert.Null(request.Subtitle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ping")]
    [InlineData("{")]
    [InlineData("""{"op":"ping",}""")]
    [InlineData("""{'op':'ping'}""")]
    [InlineData("""[{"op":"ping"}]""")]
    [InlineData("\"ping\"")]
    [InlineData("null")]
    [InlineData("""{"op":"ping"} {"op":"ping"}""")]
    [InlineData("""/* hi */ {"op":"ping"}""")]
    public void Parse_NotAJsonObject_IsInvalid(string? json)
    {
        AssertError(ApiErrorCode.Invalid, json);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"op":null}""")]
    [InlineData("""{"op":7}""")]
    [InlineData("""{"op":["ping"]}""")]
    [InlineData("""{"id":"a","title":"x"}""")]
    public void Parse_MissingOrBadOp_IsInvalid(string json)
    {
        AssertError(ApiErrorCode.Invalid, json);
    }

    [Theory]
    [InlineData("""{"op":"run","cmd":"calc.exe"}""")]
    [InlineData("""{"op":"open","url":"https://example.com"}""")]
    [InlineData("""{"op":"delete"}""")]
    [InlineData("""{"op":""}""")]
    [InlineData("""{"op":"ping "}""")]
    public void Parse_UnknownOp_IsUnknownOp(string json)
    {
        AssertError(ApiErrorCode.UnknownOp, json);
    }

    [Theory]
    [InlineData("""{"op":"push","id":"a","title":"x","url":"https://example.com"}""")]
    [InlineData("""{"op":"push","id":"a","title":"x","image":"C:\\x.png"}""")]
    [InlineData("""{"op":"push","id":"a","title":"x","action":"notepad"}""")]
    [InlineData("""{"op":"push","id":"a","titel":"x"}""")]
    [InlineData("""{"op":"ping","id":"a"}""")]
    [InlineData("""{"op":"end","id":"a","title":"x"}""")]
    [InlineData("""{"op":"alert","title":"x","progress":5}""")]
    [InlineData("""{"op":"alert","title":"x","id":"a"}""")]
    public void Parse_FieldNotAllowedForOp_IsInvalid(string json)
    {
        AssertError(ApiErrorCode.Invalid, json);
    }

    [Theory]
    [InlineData("""{"op":"push","id":"a","title":"x","title":"y"}""")]
    [InlineData("""{"op":"push","id":"a","title":"x","Title":"y"}""")]
    [InlineData("""{"op":"ping","op":"push"}""")]
    public void Parse_RepeatedField_IsInvalid(string json)
    {
        AssertError(ApiErrorCode.Invalid, json);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("backup-nas")]
    [InlineData("a-")]
    [InlineData("9lives")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void Parse_ValidId_IsAccepted(string id)
    {
        Assert.Equal(id, ParseOk<EndRequest>($$"""{"op":"end","id":"{{id}}"}""").Id);
        Assert.True(PushProtocol.IsValidId(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-a")]
    [InlineData("Backup")]
    [InlineData("has space")]
    [InlineData("a_b")]
    [InlineData("a.b")]
    [InlineData("caf\u00e9")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void Parse_BadId_IsInvalid(string id)
    {
        AssertError(ApiErrorCode.Invalid, $$"""{"op":"end","id":"{{id}}"}""");
        AssertError(ApiErrorCode.Invalid, $$"""{"op":"push","id":"{{id}}","title":"x"}""");
        Assert.False(PushProtocol.IsValidId(id));
    }

    [Theory]
    [InlineData("""{"op":"end"}""")]
    [InlineData("""{"op":"end","id":null}""")]
    [InlineData("""{"op":"end","id":5}""")]
    [InlineData("""{"op":"push","title":"x"}""")]
    public void Parse_MissingOrNonTextId_IsInvalid(string json)
    {
        AssertError(ApiErrorCode.Invalid, json);
    }

    [Theory]
    [InlineData("""{"op":"push","id":"a"}""")]
    [InlineData("""{"op":"push","id":"a","title":null}""")]
    [InlineData("""{"op":"push","id":"a","title":""}""")]
    [InlineData("""{"op":"push","id":"a","title":"  \t "}""")]
    [InlineData("""{"op":"push","id":"a","title":"\u202E\u2066\u0000"}""")]
    [InlineData("""{"op":"push","id":"a","title":64}""")]
    [InlineData("""{"op":"push","id":"a","title":"x","subtitle":7}""")]
    [InlineData("""{"op":"alert"}""")]
    [InlineData("""{"op":"alert","title":"\n"}""")]
    public void Parse_MissingOrNonTextTitle_IsInvalid(string json)
    {
        AssertError(ApiErrorCode.Invalid, json);
    }

    [Fact]
    public void Parse_TitleLimit_IsExactly48Characters()
    {
        var at = new string('a', PushProtocol.MaxTitleLength);
        var over = at + "a";

        Assert.Equal(at, ParseOk<PushRequest>(Push(title: at)).Title);
        AssertError(ApiErrorCode.Invalid, Push(title: over));
        Assert.Equal(at, ParseOk<AlertRequest>(Alert(title: at)).Title);
        AssertError(ApiErrorCode.Invalid, Alert(title: over));
    }

    [Fact]
    public void Parse_SubtitleLimit_IsExactly64Characters()
    {
        var at = new string('b', PushProtocol.MaxSubtitleLength);
        var over = at + "b";

        Assert.Equal(at, ParseOk<PushRequest>(Push(subtitle: at)).Subtitle);
        AssertError(ApiErrorCode.Invalid, Push(subtitle: over));
        Assert.Equal(at, ParseOk<AlertRequest>(Alert(subtitle: at)).Subtitle);
        AssertError(ApiErrorCode.Invalid, Alert(subtitle: over));
    }

    [Fact]
    public void Parse_TextLimit_IsCountedAfterCleaning()
    {
        var title = string.Concat(Enumerable.Repeat("a\\u202E", PushProtocol.MaxTitleLength));

        Assert.Equal(new string('a', PushProtocol.MaxTitleLength), ParseOk<PushRequest>(Push(title: title)).Title);
    }

    [Fact]
    public void Parse_TextLimit_CountsEmojiAsOneCharacter()
    {
        var title = string.Concat(Enumerable.Repeat("\\ud83c\\udf89", PushProtocol.MaxTitleLength));

        Assert.Equal(PushProtocol.MaxTitleLength, TextSanitizer.CountCharacters(ParseOk<PushRequest>(Push(title: title)).Title));
        AssertError(ApiErrorCode.Invalid, Push(title: title + "\\ud83c\\udf89"));
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("100", 100.0)]
    [InlineData("64", 64.0)]
    [InlineData("0.5", 0.5)]
    [InlineData("99.99", 99.99)]
    [InlineData("1e1", 10.0)]
    public void Parse_ProgressInRange_IsAccepted(string value, double expected)
    {
        Assert.Equal(expected, ParseOk<PushRequest>(Push(extra: $",\"progress\":{value}")).Progress);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("100.1")]
    [InlineData("-1")]
    [InlineData("1e309")]
    [InlineData("\"64\"")]
    [InlineData("true")]
    [InlineData("[64]")]
    public void Parse_ProgressOutOfRangeOrNotANumber_IsInvalid(string value)
    {
        AssertError(ApiErrorCode.Invalid, Push(extra: $",\"progress\":{value}"));
    }

    [Theory]
    [InlineData("\"paused\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    public void Parse_BadState_IsInvalid(string value)
    {
        AssertError(ApiErrorCode.Invalid, Push(extra: $",\"state\":{value}"));
    }

    [Theory]
    [InlineData("icon", "\"rocket\"")]
    [InlineData("icon", "\"\"")]
    [InlineData("icon", "\"https://example.com/a.png\"")]
    [InlineData("icon", "3")]
    [InlineData("accent", "\"red\"")]
    [InlineData("accent", "\"#FF0000\"")]
    [InlineData("accent", "\"\"")]
    public void Parse_IconOrAccentOutsideFixedList_IsInvalid(string field, string value)
    {
        AssertError(ApiErrorCode.Invalid, Push(extra: $",\"{field}\":{value}"));
    }

    [Fact]
    public void Parse_AlertIconOutsideFixedList_IsInvalid()
    {
        AssertError(ApiErrorCode.Invalid, """{"op":"alert","title":"x","icon":"rocket"}""");
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("600", 600)]
    [InlineData("86400", 86400)]
    [InlineData("\"10s\"", 10)]
    [InlineData("\"10m\"", 600)]
    [InlineData("\"1h30m\"", 5400)]
    [InlineData("\"24h\"", 86400)]
    [InlineData("\"1H2M3S\"", 3723)]
    [InlineData("\"900\"", 900)]
    public void Parse_TtlInRange_IsAccepted(string value, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), ParseOk<PushRequest>(Push(extra: $",\"ttl\":{value}")).Ttl);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("86401")]
    [InlineData("600.5")]
    [InlineData("1e30")]
    [InlineData("99999999999999999999999")]
    [InlineData("\"9s\"")]
    [InlineData("\"25h\"")]
    [InlineData("\"24h1s\"")]
    [InlineData("\"10x\"")]
    [InlineData("\"m\"")]
    [InlineData("\"10m10m\"")]
    [InlineData("\"30s1m\"")]
    [InlineData("\"10 m\"")]
    [InlineData("\"\"")]
    [InlineData("true")]
    public void Parse_TtlOutOfRangeOrMalformed_IsInvalid(string value)
    {
        AssertError(ApiErrorCode.Invalid, Push(extra: $",\"ttl\":{value}"));
    }

    [Fact]
    public void Parse_AtByteLimit_IsAccepted()
    {
        var json = PadToBytes(CliPing, PushProtocol.MaxMessageBytes);

        Assert.Equal(PushProtocol.MaxMessageBytes, Encoding.UTF8.GetByteCount(json));
        Assert.True(PushProtocol.Parse(json).IsSuccess);
        Assert.True(PushProtocol.Parse(Encoding.UTF8.GetBytes(json)).IsSuccess);
    }

    [Fact]
    public void Parse_OverByteLimit_IsTooLarge()
    {
        var json = PadToBytes(CliPing, PushProtocol.MaxMessageBytes + 1);

        AssertError(ApiErrorCode.TooLarge, json);
        Assert.Equal(ApiErrorCode.TooLarge, PushProtocol.Parse(Encoding.UTF8.GetBytes(json)).Error?.Code);
    }

    [Fact]
    public void Parse_MultiByteTextOverByteLimit_IsTooLarge()
    {
        // 3000 characters but 9000 UTF-8 bytes.
        var json = "{\"op\":\"ping\",\"x\":\"" + new string('\u20AC', 3000) + "\"}";

        Assert.True(json.Length < PushProtocol.MaxMessageBytes);
        AssertError(ApiErrorCode.TooLarge, json);
    }

    [Fact]
    public void Parse_Bytes_ReadsUtf8WithOrWithoutByteOrderMark()
    {
        var bytes = Encoding.UTF8.GetBytes(PythonPush);

        Assert.Equal(PushProtocol.Parse(PythonPush).Request, PushProtocol.Parse(bytes).Request);
        Assert.True(PushProtocol.Parse([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(CliPing)]).IsSuccess);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0x7B, 0x22, 0x6F, 0x70, 0x22, 0x3A, 0x22, 0xC0, 0xAF, 0x22, 0x7D })]   // overlong "/"
    [InlineData(new byte[] { 0x7B, 0x22, 0x6F, 0x70, 0x22, 0x3A, 0x22, 0xED, 0xA0, 0x80, 0x22, 0x7D })]   // encoded surrogate
    [InlineData(new byte[] { 0x7B, 0x22, 0x6F, 0x70, 0x22, 0x3A, 0x22, 0xE2, 0x82, 0x22, 0x7D })]   // cut short
    [InlineData(new byte[0])]
    public void Parse_BadUtf8_IsInvalid(byte[] bytes)
    {
        var result = PushProtocol.Parse(bytes);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.Invalid, result.Error.Code);
    }

    [Theory]
    [InlineData("""{"op":"push","id":"a","title":"\ud800"}""")]
    [InlineData("""{"op":"push","id":"a","title":"x\udfffy"}""")]
    [InlineData("""{"op":"push","id":"a","title":"\ud83c"}""")]
    [InlineData("""{"op":"push","id":"a","\ud800":"x"}""")]
    public void Parse_EscapedLoneSurrogate_IsInvalid(string json)
    {
        AssertError(ApiErrorCode.Invalid, json);
    }

    [Fact]
    public void Parse_DeepNesting_IsInvalid()
    {
        var json = "{\"op\":\"push\",\"id\":\"a\",\"title\":" + new string('[', 2000) + new string(']', 2000) + "}";

        AssertError(ApiErrorCode.Invalid, json);
    }

    [Fact]
    public void Parse_ErrorMessage_NeverEchoesRequestText()
    {
        var longName = new string('z', 1000) + "\u202E";
        var result = PushProtocol.Parse($$"""{"op":"push","id":"a","title":"x","{{longName}}":1}""");

        Assert.False(result.IsSuccess);
        Assert.True(result.Error.Message.Length < 200);
        Assert.DoesNotContain('\u202E', result.Error.Message);

        var secret = PushProtocol.Parse("""{"op":"push","id":"a","title":"hunter2-secret-value","progress":500}""");
        Assert.DoesNotContain("hunter2", secret.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_BuiltRequestWithDirtyText_IsInvalid()
    {
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", "Back\u202Eup")));
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", " padded ")));
        Assert.NotNull(PushProtocol.Validate(new AlertRequest("x") { Subtitle = "line\nbreak" }));
    }

    [Fact]
    public void Validate_BuiltRequestWithEmptyText_IsInvalid()
    {
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", "")));
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", "x") { Subtitle = "" }));
        Assert.NotNull(PushProtocol.Validate(new AlertRequest("x") { Subtitle = "" }));
        Assert.Null(PushProtocol.Validate(new AlertRequest("x") { Subtitle = null }));
    }

    [Fact]
    public void Validate_BuiltRequestWithUndefinedEnumOrPartSecond_IsInvalid()
    {
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", "x") { Icon = (PushIcon)99 }));
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", "x") { Accent = (PushAccent)(-1) }));
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", "x") { State = (PushState)3 }));
        Assert.NotNull(PushProtocol.Validate(new AlertRequest("x") { Icon = (PushIcon)ApiNames.Icons.Count }));
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", "x") { Ttl = TimeSpan.FromSeconds(10.5) }));
        Assert.NotNull(PushProtocol.Validate(new PushRequest("a", "x") { Progress = double.NaN }));
    }

    [Fact]
    public void Format_InvalidRequest_Throws()
    {
        Assert.Throws<ArgumentException>(() => PushProtocol.Format(new PushRequest("Bad Id", "x")));
        Assert.Throws<ArgumentException>(() => PushProtocol.Format(new AlertRequest(new string('a', 49))));
    }

    [Fact]
    public void Format_FullPush_MatchesTheCliWireFormat()
    {
        var request = new PushRequest("backup", "Backup NAS")
        {
            Subtitle = "Disk 2",
            Progress = 64,
            Icon = PushIcon.Backup,
            Accent = PushAccent.Blue,
            Ttl = TimeSpan.FromMinutes(10),
        };

        Assert.Equal(CliPush, PushProtocol.Format(request));
    }

    [Fact]
    public void Format_EndAndPing_MatchTheCliWireFormat()
    {
        Assert.Equal(CliEnd, PushProtocol.Format(new EndRequest("backup")));
        Assert.Equal(CliPing, PushProtocol.Format(new PingRequest()));
    }

    public static TheoryData<ApiRequest> RoundTripRequests =>
    [
        new PingRequest(),
        new EndRequest("backup"),
        new EndRequest("abcdefghijklmnopqrstuvwxyz012345"),
        new PushRequest("a", "x"),
        new PushRequest("backup", "Backup NAS")
        {
            Subtitle = "Disk 2",
            Progress = 64,
            State = PushState.Running,
            Icon = PushIcon.Backup,
            Accent = PushAccent.Blue,
            Ttl = TimeSpan.FromMinutes(10),
        },
        new PushRequest("solat-sync", "Muat turun waktu solat") { Subtitle = "Zon SGR01 \U0001F54C", Progress = 37.5, Ttl = TimeSpan.FromSeconds(10) },
        new PushRequest("q", "Ali's <b>&\"quoted\"\\") { Progress = 0, State = PushState.Failed, Ttl = TimeSpan.FromHours(24) },
        new PushRequest("p", "x") { Progress = 100, State = PushState.Done, Ttl = TimeSpan.FromSeconds(3723) },
        new PushRequest("p", "x") { Progress = 1.0 / 3 },
        new AlertRequest("Build gagal"),
        new AlertRequest("Build gagal") { Subtitle = "main \u2192 ujian", Icon = PushIcon.Error },
        new AlertRequest(new string('a', 48)) { Subtitle = new string('b', 64) },
    ];

    [Theory]
    [MemberData(nameof(RoundTripRequests))]
    public void Format_ThenParse_RoundTrips(ApiRequest request)
    {
        var json = PushProtocol.Format(request);
        var result = PushProtocol.Parse(json);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(request, result.Request);
        Assert.StartsWith($"{{\"op\":\"{ApiNames.Of(request.Op)}\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void Parse_ThenFormat_RoundTrips(string fixture)
    {
        var first = PushProtocol.Parse(fixture);
        Assert.True(first.IsSuccess, first.Error?.Message);

        var json = PushProtocol.Format(first.Request);
        var second = PushProtocol.Parse(json);

        Assert.Equal(first.Request, second.Request);
        Assert.Equal(json, PushProtocol.Format(second.Request!));
    }

    [Fact]
    public void Format_EveryStateIconAndAccent_RoundTrips()
    {
        foreach (var state in Enum.GetValues<PushState>())
            Assert.Equal(state, RoundTrip(new PushRequest("a", "x") { State = state }).State);
        foreach (var icon in Enum.GetValues<PushIcon>())
        {
            Assert.Equal(icon, RoundTrip(new PushRequest("a", "x") { Icon = icon }).Icon);
            Assert.Equal(icon, ((AlertRequest)PushProtocol.Parse(PushProtocol.Format(new AlertRequest("x") { Icon = icon })).Request!).Icon);
        }
        foreach (var accent in Enum.GetValues<PushAccent>())
            Assert.Equal(accent, RoundTrip(new PushRequest("a", "x") { Accent = accent }).Accent);

        static PushRequest RoundTrip(PushRequest request) => (PushRequest)PushProtocol.Parse(PushProtocol.Format(request)).Request!;
    }

    public static TheoryData<ApiResponse> RoundTripResponses
    {
        get
        {
            var data = new TheoryData<ApiResponse> { ApiResponse.Success, ApiResponse.Pong("0.2.0-preview.1"), ApiResponse.Pong(null) };
            foreach (var code in Enum.GetValues<ApiErrorCode>())
            {
                data.Add(ApiResponse.Failure(code));
                data.Add(ApiResponse.Failure(code, "The field 'title' is required."));
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RoundTripResponses))]
    public void FormatResponse_ThenParse_RoundTrips(ApiResponse response)
    {
        Assert.True(PushProtocol.TryParseResponse(PushProtocol.FormatResponse(response), out var parsed));
        Assert.Equal(response, parsed);
    }

    [Fact]
    public void FormatResponse_MatchesWireFormat()
    {
        Assert.Equal("""{"ok":true}""", PushProtocol.FormatResponse(ApiResponse.Success));
        Assert.Equal("""{"ok":true,"version":"0.2.0"}""", PushProtocol.FormatResponse(ApiResponse.Pong("0.2.0")));
        Assert.Equal("""{"ok":false,"error":"rate_limited"}""", PushProtocol.FormatResponse(ApiResponse.Failure(ApiErrorCode.RateLimited)));
        Assert.Equal(
            """{"ok":false,"error":"widget_disabled","message":"Off"}""",
            PushProtocol.FormatResponse(ApiResponse.Failure(new ApiError(ApiErrorCode.WidgetDisabled, "Off"))));
    }

    [Fact]
    public void TryParseResponse_IgnoresUnknownFields()
    {
        Assert.True(PushProtocol.TryParseResponse("""{"ok":true,"later":{"x":1}}""", out var response));
        Assert.True(response.Ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("""{"ok":"yes"}""")]
    [InlineData("""{"ok":true,"error":"invalid"}""")]
    [InlineData("""{"ok":false}""")]
    [InlineData("""{"ok":false,"error":"exploded"}""")]
    [InlineData("""{"ok":true""")]
    [InlineData("""{"ok":true,"message":"\ud800"}""")]
    public void TryParseResponse_BadResponse_IsFalse(string? json)
    {
        Assert.False(PushProtocol.TryParseResponse(json, out var response));
        Assert.Null(response);
    }

    [Fact]
    public void ApiNames_CoverEveryEnumValueOnce()
    {
        AssertNames(ApiNames.Ops, Enum.GetValues<ApiOp>(), ApiNames.Of, (string n, out ApiOp v) => ApiNames.TryParse(n, out v));
        AssertNames(ApiNames.States, Enum.GetValues<PushState>(), ApiNames.Of, (string n, out PushState v) => ApiNames.TryParse(n, out v));
        AssertNames(ApiNames.Icons, Enum.GetValues<PushIcon>(), ApiNames.Of, (string n, out PushIcon v) => ApiNames.TryParse(n, out v));
        AssertNames(ApiNames.Accents, Enum.GetValues<PushAccent>(), ApiNames.Of, (string n, out PushAccent v) => ApiNames.TryParse(n, out v));
        AssertNames(ApiNames.ErrorCodes, Enum.GetValues<ApiErrorCode>(), ApiNames.Of, (string n, out ApiErrorCode v) => ApiNames.TryParse(n, out v));
    }

    [Fact]
    public void ApiNames_ErrorCodesMatchThePlan()
    {
        Assert.Equal(new[] { "invalid", "too_large", "rate_limited", "unknown_op", "widget_disabled" }, ApiNames.ErrorCodes);
        Assert.Equal(new[] { "ping", "push", "end", "alert" }, ApiNames.Ops);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("45", 45)]
    [InlineData("45s", 45)]
    [InlineData("10m", 600)]
    [InlineData("2h", 7200)]
    [InlineData("1h30m", 5400)]
    [InlineData("1h30m15s", 5415)]
    [InlineData("1h15s", 3615)]
    [InlineData("90m", 5400)]
    [InlineData("999999h", 3_599_996_400)]
    public void TryParseDuration_ReadsUnits(string text, long seconds)
    {
        Assert.True(PushProtocol.TryParseDuration(text, out var duration));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("h")]
    [InlineData("-5m")]
    [InlineData("+5m")]
    [InlineData("5m ")]
    [InlineData(" 5m")]
    [InlineData("1.5h")]
    [InlineData("5d")]
    [InlineData("5ms")]
    [InlineData("1m1h")]
    [InlineData("1s1s")]
    [InlineData("5m10")]
    [InlineData("1000000h")]
    [InlineData("\u0661\u0660m")]   // Arabic-Indic digits
    public void TryParseDuration_RefusesMalformedText(string? text)
    {
        Assert.False(PushProtocol.TryParseDuration(text, out _));
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(45, "45s")]
    [InlineData(600, "10m")]
    [InlineData(5400, "1h30m")]
    [InlineData(3615, "1h15s")]
    [InlineData(86400, "24h")]
    public void FormatDuration_RoundTrips(int seconds, string expected)
    {
        var text = PushProtocol.FormatDuration(TimeSpan.FromSeconds(seconds));

        Assert.Equal(expected, text);
        Assert.True(PushProtocol.TryParseDuration(text, out var parsed));
        Assert.Equal(TimeSpan.FromSeconds(seconds), parsed);
    }

    private delegate bool TryParseName<T>(string name, out T value);

    private static void AssertNames<T>(IReadOnlyList<string> names, T[] values, Func<T, string> of, TryParseName<T> tryParse)
        where T : struct, Enum
    {
        Assert.Equal(values.Length, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var value in values)
        {
            var name = of(value);
            Assert.Equal(name.ToLowerInvariant(), name);
            Assert.True(tryParse(name, out var parsed));
            Assert.Equal(value, parsed);
            Assert.True(tryParse(name.ToUpperInvariant(), out parsed));
            Assert.Equal(value, parsed);
        }
        Assert.False(tryParse("nope", out _));
    }

    private static T ParseOk<T>(string json)
        where T : ApiRequest
    {
        var result = PushProtocol.Parse(json);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return Assert.IsType<T>(result.Request);
    }

    private static void AssertError(ApiErrorCode expected, string? json)
    {
        var result = PushProtocol.Parse(json);
        Assert.False(result.IsSuccess, $"Parsed as {result.Request}");
        Assert.Null(result.Request);
        Assert.Equal(expected, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }

    /// <summary>A push with id "a". Title and subtitle are JSON string contents, already escaped.</summary>
    private static string Push(string title = "x", string? subtitle = null, string extra = "") =>
        $$"""{"op":"push","id":"a","title":"{{title}}"{{(subtitle is null ? "" : $",\"subtitle\":\"{subtitle}\"")}}{{extra}}}""";

    private static string Alert(string title = "x", string? subtitle = null) =>
        $$"""{"op":"alert","title":"{{title}}"{{(subtitle is null ? "" : $",\"subtitle\":\"{subtitle}\"")}}}""";

    /// <summary>Pads a JSON object with spaces before its closing brace until it is exactly <paramref name="bytes"/> long in UTF-8.</summary>
    private static string PadToBytes(string json, int bytes)
    {
        var padding = bytes - Encoding.UTF8.GetByteCount(json);
        return json[..^1] + new string(' ', padding) + "}";
    }
}
