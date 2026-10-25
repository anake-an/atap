using System.Text.Json;
using Atap.Core.Custom;
using static Atap.Core.Tests.Custom.CustomFixtures;

namespace Atap.Core.Tests.Custom;

public class JsonPathLiteTests
{
    [Theory]
    [InlineData(NasStatus, "$.disk.usedPercent", "72.5")]
    [InlineData(NasStatus, "$['disk']['usedPercent']", "72.5")]
    [InlineData(NasStatus, "$[\"disk\"][\"usedPercent\"]", "72.5")]
    [InlineData(NasStatus, "$.disk['totalGb']", "4000")]
    [InlineData(NasStatus, "$.status", "\"healthy\"")]
    [InlineData(PiHoleSummary, "$.queries.percent_blocked", "20.189575")]
    [InlineData(PiHoleSummary, "$.gravity.domains_being_blocked", "157420")]
    [InlineData(PiHoleV5Summary, "$.gravity_last_updated.relative.hours", "9")]
    [InlineData(UptimeKumaHeartbeat, "$.heartbeatList['2'][-1].status", "0")]
    [InlineData(UptimeKumaHeartbeat, "$.heartbeatList.1[0].ping", "23")]
    [InlineData(UptimeKumaHeartbeat, "$.heartbeatList['2'][-2].ping", "4")]
    [InlineData(UptimeKumaHeartbeat, "$.uptimeList['2_24']", "0.9583")]
    [InlineData(DockerContainers, "$[2].Names[0]", "\"/backup\"")]
    [InlineData(GlancesFileSystems, "$[1].percent", "79.9")]
    [InlineData(GlancesFileSystems, "$[ 1 ][ 'mnt_point' ]", "\"/volume1\"")]
    public void Evaluate_DefinitePaths_FindOneValue(string json, string path, string expected)
    {
        var result = JsonPathLite.Parse(path).Evaluate(Json(json));

        Assert.Equal(JsonPathOutcome.Found, result.Outcome);
        Assert.Equal(expected, Assert.Single(result.Matches).GetRawText());
    }

    [Fact]
    public void Evaluate_Root_ReturnsWholeDocument()
    {
        var root = Json(NasStatus);

        var result = JsonPathLite.Parse("$").Evaluate(root);

        Assert.Equal(JsonValueKind.Object, Assert.Single(result.Matches).ValueKind);
        Assert.True(JsonPathLite.Root.IsDefinite);
        Assert.Equal("$", JsonPathLite.Root.Expression);
    }

    [Fact]
    public void Evaluate_Wildcard_FindsEveryArrayItemInOrder()
    {
        var path = JsonPathLite.Parse("$[*].State");

        var result = path.Evaluate(Json(DockerContainers));

        Assert.False(path.IsDefinite);
        Assert.Equal(["running", "running", "exited"], result.Matches.Select(m => m.GetString()));
    }

    [Fact]
    public void Evaluate_NestedWildcards_FlattenInOrder()
    {
        var result = JsonPathLite.Parse("$.publicGroupList[*].monitorList[*].name").Evaluate(Json(UptimeKumaStatusPage));

        Assert.Equal(["Jellyfin", "Nextcloud", "Router"], result.Matches.Select(m => m.GetString()));
    }

    [Theory]
    [InlineData("$.clients[*]")]
    [InlineData("$.clients.*")]
    public void Evaluate_WildcardOnObject_FindsEveryValue(string path)
    {
        var result = JsonPathLite.Parse(path).Evaluate(Json(PiHoleSummary));

        Assert.Equal(["14", "19"], result.Matches.Select(m => m.GetRawText()));
    }

    [Theory]
    [InlineData(DockerContainers, "$.length()", 3)]
    [InlineData(DockerContainers, "$[*].length()", 3)]
    [InlineData(DockerContainers, "$[0].Names.length()", 1)]
    [InlineData(PiHoleSummary, "$.queries.types.length()", 16)]
    [InlineData(NasStatus, "$.host.length()", 3)]
    [InlineData(UptimeKumaStatusPage, "$.publicGroupList[*].monitorList[*].length()", 3)]
    [InlineData(UptimeKumaStatusPage, "$.maintenanceList.length()", 0)]
    [InlineData(UptimeKumaStatusPage, "$.maintenanceList[*].length()", 0)]
    [InlineData(UptimeKumaHeartbeat, "$.heartbeatList.length()", 3)]
    [InlineData(UptimeKumaHeartbeat, "$.heartbeatList[*][*].status.length()", 5)]
    public void Evaluate_Length_CountsItemsCharactersOrMatches(string json, string path, int expected)
    {
        var parsed = JsonPathLite.Parse(path);

        var result = parsed.Evaluate(Json(json));

        Assert.True(parsed.EndsWithLength);
        Assert.Equal(JsonPathOutcome.Found, result.Outcome);
        Assert.Equal(expected, Assert.Single(result.Matches).GetInt32());
    }

    [Fact]
    public void Evaluate_Length_CountsTextElementsNotCodeUnits()
    {
        var result = JsonPathLite.Parse("$.name.length()").Evaluate(Json("""{ "name": "Kafe 😀" }"""));

        Assert.Equal(6, result.Matches[0].GetInt32());
    }

    [Theory]
    [InlineData("""{ "sensor.nas_cpu": 5 }""", "$['sensor.nas_cpu']")]
    [InlineData("""{ "it's": 5 }""", @"$['it\'s']")]
    [InlineData("""{ "say \"hi\"": 5 }""", @"$[""say \""hi\""""]")]
    [InlineData("""{ "a\\b": 5 }""", @"$['a\\b']")]
    [InlineData("""{ "a]b[c": 5 }""", "$['a]b[c']")]
    [InlineData("""{ "": 5 }""", "$['']")]
    [InlineData("""{ "cpu-temp": 5 }""", "$.cpu-temp")]
    [InlineData("""{ "$type": 5 }""", "$.$type")]
    [InlineData("""{ "suhu_bilik": 5 }""", "$.suhu_bilik")]
    [InlineData("""{ "température": 5 }""", "$.température")]
    [InlineData("""{ "5m": 5 }""", "$.5m")]
    [InlineData("""{ "length": 5 }""", "$.length")]
    [InlineData("""{ "length": 5 }""", "$['length']")]
    public void Evaluate_MemberNames_MatchExactly(string json, string path)
    {
        var result = JsonPathLite.Parse(path).Evaluate(Json(json));

        Assert.Equal(5, Assert.Single(result.Matches).GetInt32());
    }

    [Fact]
    public void Parse_TrimsSpacesAroundThePath()
    {
        var path = JsonPathLite.Parse("  $.disk.usedPercent \t");

        Assert.Equal("$.disk.usedPercent", path.Expression);
        Assert.Equal("$.disk.usedPercent", path.ToString());
    }

    [Fact]
    public void Evaluate_MissingMember_ReportsWhereItStopped()
    {
        var result = JsonPathLite.Parse("$.disk.free.gb").Evaluate(Json(NasStatus));

        Assert.Equal(JsonPathOutcome.Missing, result.Outcome);
        Assert.False(result.Found);
        Assert.Empty(result.Matches);
        Assert.Equal(6, result.MissingAt);
        Assert.Equal(JsonValueKind.Object, result.MissingOn);
        Assert.False(result.TryGetFirst(out _));
    }

    [Theory]
    [InlineData("$[3]", 1, JsonValueKind.Array)]
    [InlineData("$[-4]", 1, JsonValueKind.Array)]
    [InlineData("$.State", 1, JsonValueKind.Array)]
    [InlineData("$[0].State[0]", 10, JsonValueKind.String)]
    [InlineData("$[0].State.length", 10, JsonValueKind.String)]
    [InlineData("$[0].Id.x.length()", 7, JsonValueKind.String)]
    public void Evaluate_StepThatFindsNothing_IsMissing(string path, int missingAt, JsonValueKind missingOn)
    {
        var result = JsonPathLite.Parse(path).Evaluate(Json(DockerContainers));

        Assert.Equal(JsonPathOutcome.Missing, result.Outcome);
        Assert.Equal(missingAt, result.MissingAt);
        Assert.Equal(missingOn, result.MissingOn);
    }

    [Theory]
    [InlineData("$.uptime.length()", JsonValueKind.Number)]
    [InlineData("$.cpu.temp.length()", JsonValueKind.Number)]
    public void Evaluate_LengthOfANumber_IsMissing(string path, JsonValueKind on)
    {
        var parsed = JsonPathLite.Parse(path);

        var result = parsed.Evaluate(Json(NasStatus));

        Assert.Equal(JsonPathOutcome.Missing, result.Outcome);
        Assert.Equal(path.IndexOf(".length()", StringComparison.Ordinal), result.MissingAt);
        Assert.Equal(on, result.MissingOn);
    }

    [Theory]
    [InlineData("$.maintenanceList[*]")]
    [InlineData("$.publicGroupList[*].missing")]
    public void Evaluate_NothingAfterAWildcard_IsNoMatches(string path)
    {
        var result = JsonPathLite.Parse(path).Evaluate(Json(UptimeKumaStatusPage));

        Assert.Equal(JsonPathOutcome.NoMatches, result.Outcome);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Evaluate_MissingBeforeTheWildcard_IsMissingEvenWithLength()
    {
        var result = JsonPathLite.Parse("$.containers[*].length()").Evaluate(Json(NasStatus));

        Assert.Equal(JsonPathOutcome.Missing, result.Outcome);
    }

    [Fact]
    public void Evaluate_UndefinedRoot_IsMissing()
    {
        var result = JsonPathLite.Parse("$").Evaluate(default);

        Assert.Equal(JsonPathOutcome.Missing, result.Outcome);
        Assert.Equal(0, result.MissingAt);
    }

    [Fact]
    public void Evaluate_ManyMatches_AreCappedAndFlagged()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("[1,2]", JsonPathLite.MaxMatches)) + "]";

        var result = JsonPathLite.Parse("$[*][*]").Evaluate(Json(json));

        Assert.True(result.Truncated);
        Assert.Equal(JsonPathLite.MaxMatches, result.Matches.Count);
    }

    [Fact]
    public void Evaluate_LengthResult_OutlivesTheDocument()
    {
        JsonPathResult result;
        using (var document = JsonDocument.Parse(DockerContainers))
            result = JsonPathLite.Parse("$.length()").Evaluate(document.RootElement);

        Assert.Equal(3, result.Matches[0].GetInt32());
    }

    [Theory]
    [InlineData("", JsonPathErrorKind.Empty, 0)]
    [InlineData("   ", JsonPathErrorKind.Empty, 0)]
    [InlineData("disk.usedPercent", JsonPathErrorKind.MissingRoot, 0)]
    [InlineData("@.status", JsonPathErrorKind.MissingRoot, 0)]
    [InlineData("  .a", JsonPathErrorKind.MissingRoot, 2)]
    [InlineData("$..name", JsonPathErrorKind.RecursiveDescent, 1)]
    [InlineData("$.a..b", JsonPathErrorKind.RecursiveDescent, 3)]
    [InlineData("  $..a", JsonPathErrorKind.RecursiveDescent, 3)]
    [InlineData("$[?(@.status==1)]", JsonPathErrorKind.Filter, 1)]
    [InlineData("$.list[?@.up]", JsonPathErrorKind.Filter, 6)]
    [InlineData("$[(@.length-1)]", JsonPathErrorKind.Filter, 1)]
    [InlineData("$[0:2]", JsonPathErrorKind.Slice, 1)]
    [InlineData("$[:2]", JsonPathErrorKind.Slice, 1)]
    [InlineData("$[::-1]", JsonPathErrorKind.Slice, 1)]
    [InlineData("$[0,1]", JsonPathErrorKind.Union, 1)]
    [InlineData("$['a','b']", JsonPathErrorKind.Union, 1)]
    [InlineData("$[*,0]", JsonPathErrorKind.Union, 1)]
    [InlineData("$.max()", JsonPathErrorKind.UnknownFunction, 2)]
    [InlineData("$.items.sum()", JsonPathErrorKind.UnknownFunction, 8)]
    [InlineData("$.Length()", JsonPathErrorKind.UnknownFunction, 2)]
    [InlineData("$.items.length().x", JsonPathErrorKind.LengthNotLast, 7)]
    [InlineData("$.length()[0]", JsonPathErrorKind.LengthNotLast, 1)]
    [InlineData("$.length(", JsonPathErrorKind.UnexpectedEnd, 9)]
    [InlineData("$.length(x)", JsonPathErrorKind.UnexpectedCharacter, 9)]
    [InlineData("$.", JsonPathErrorKind.UnexpectedEnd, 2)]
    [InlineData("$.a.", JsonPathErrorKind.UnexpectedEnd, 4)]
    [InlineData("$[", JsonPathErrorKind.UnexpectedEnd, 2)]
    [InlineData("$[0", JsonPathErrorKind.UnexpectedEnd, 3)]
    [InlineData("$['a'", JsonPathErrorKind.UnexpectedEnd, 5)]
    [InlineData("$['a", JsonPathErrorKind.UnterminatedName, 2)]
    [InlineData("$[\"a']", JsonPathErrorKind.UnterminatedName, 2)]
    [InlineData(@"$['a\", JsonPathErrorKind.UnterminatedName, 2)]
    [InlineData(@"$['a\n']", JsonPathErrorKind.InvalidEscape, 4)]
    [InlineData("$['\\u0041']", JsonPathErrorKind.InvalidEscape, 3)]
    [InlineData("$[1.5]", JsonPathErrorKind.InvalidIndex, 2)]
    [InlineData("$[1e3]", JsonPathErrorKind.InvalidIndex, 2)]
    [InlineData("$[2a]", JsonPathErrorKind.InvalidIndex, 2)]
    [InlineData("$[-0]", JsonPathErrorKind.InvalidIndex, 2)]
    [InlineData("$[-]", JsonPathErrorKind.InvalidIndex, 2)]
    [InlineData("$[99999999999]", JsonPathErrorKind.InvalidIndex, 2)]
    [InlineData("$[2147483648]", JsonPathErrorKind.InvalidIndex, 2)]
    [InlineData("$[]", JsonPathErrorKind.UnexpectedCharacter, 2)]
    [InlineData("$[name]", JsonPathErrorKind.UnexpectedCharacter, 2)]
    [InlineData("$[0 1]", JsonPathErrorKind.UnexpectedCharacter, 4)]
    [InlineData("$name", JsonPathErrorKind.UnexpectedCharacter, 1)]
    [InlineData("$ .a", JsonPathErrorKind.UnexpectedCharacter, 1)]
    [InlineData("$.a b", JsonPathErrorKind.UnexpectedCharacter, 3)]
    [InlineData("$.a#b", JsonPathErrorKind.UnexpectedCharacter, 3)]
    [InlineData("$.a/b", JsonPathErrorKind.UnexpectedCharacter, 3)]
    [InlineData("$.[0]", JsonPathErrorKind.UnexpectedCharacter, 2)]
    [InlineData("$.@", JsonPathErrorKind.UnexpectedCharacter, 2)]
    [InlineData("$$", JsonPathErrorKind.UnexpectedCharacter, 1)]
    [InlineData("$.a)", JsonPathErrorKind.UnexpectedCharacter, 3)]
    public void TryParse_UnsupportedOrBrokenPaths_GiveAClearError(string path, JsonPathErrorKind kind, int position)
    {
        Assert.False(JsonPathLite.TryParse(path, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Equal(kind, error.Kind);
        Assert.Equal(position, error.Position);
    }

    [Fact]
    public void TryParse_Null_IsEmpty()
    {
        Assert.False(JsonPathLite.TryParse(null, out _, out var error));
        Assert.Equal(JsonPathErrorKind.Empty, error.Kind);
    }

    [Theory]
    [InlineData("$.avg()", "avg")]
    [InlineData("$.a#", "#")]
    [InlineData("$[1.5]", "1.5")]
    [InlineData("$[-0]", "-0")]
    [InlineData(@"$['a\t']", @"\t")]
    public void TryParse_Error_NamesTheOffendingPart(string path, string text)
    {
        Assert.False(JsonPathLite.TryParse(path, out _, out var error));
        Assert.Equal(text, error.Text);
        Assert.Contains(text, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_LoneSurrogate_IsRejected()
    {
        // Theory data cannot carry lone surrogates, so these are facts.
        Assert.False(JsonPathLite.TryParse("$.a\uD800", out _, out var inName));
        Assert.Equal(JsonPathErrorKind.UnexpectedCharacter, inName.Kind);
        Assert.Equal(3, inName.Position);
        Assert.False(JsonPathLite.TryParse("$['\uDC00']", out _, out var inQuotes));
        Assert.Equal(3, inQuotes.Position);
        Assert.True(JsonPathLite.TryParse("$.😀", out _, out _));
    }

    [Fact]
    public void Evaluate_BrokenSurrogateEscapesInTheDocument_NeverThrow()
    {
        var root = Json("""{ "a\uD800": "\uDC00x", "b": ["\uD800", { "\uDBFF": 1 }], "ok": "😀" }""");
        string[] paths = ["$", "$.*", "$.*.*", "$.b[*]", "$.b[*].*", "$['a']", "$.ok", "$.b[0].length()", "$.*.length()", "$[*][*].length()", "$.a�"];

        foreach (var path in paths)
            JsonPathLite.Parse(path).Evaluate(root);

        Assert.Equal(1, JsonPathLite.Parse("$.b[0].length()").Evaluate(root).Matches[0].GetInt32());
        Assert.Equal(2, JsonPathLite.Parse("$.b.length()").Evaluate(root).Matches[0].GetInt32());
        Assert.Equal("😀", JsonPathLite.Parse("$.ok").Evaluate(root).Matches[0].GetString());
    }

    [Fact]
    public void TryParse_TooLong_IsRejected()
    {
        var path = "$" + string.Concat(Enumerable.Repeat(".a", 300));

        Assert.False(JsonPathLite.TryParse(path, out _, out var error));
        Assert.Equal(JsonPathErrorKind.TooLong, error.Kind);
    }

    [Fact]
    public void TryParse_SegmentLimit_CountsLength()
    {
        var steps = string.Concat(Enumerable.Repeat(".a", JsonPathLite.MaxSegments));

        Assert.True(JsonPathLite.TryParse("$" + steps, out _, out _));
        Assert.False(JsonPathLite.TryParse("$" + steps + ".a", out _, out var tooMany));
        Assert.Equal(JsonPathErrorKind.TooManySegments, tooMany.Kind);
        Assert.False(JsonPathLite.TryParse("$" + steps + ".length()", out _, out var withLength));
        Assert.Equal(JsonPathErrorKind.TooManySegments, withLength.Kind);
    }

    [Fact]
    public void Parse_BrokenPath_ThrowsWithTheError()
    {
        var exception = Assert.Throws<JsonPathException>(() => JsonPathLite.Parse("$..a"));

        Assert.Equal(JsonPathErrorKind.RecursiveDescent, exception.Error.Kind);
        Assert.IsAssignableFrom<FormatException>(exception);
    }

    [Fact]
    public void TryParseAndEvaluate_RandomInput_NeverThrow()
    {
        const string alphabet = "$.[]'\"*-0123456789:,?@()\\ abln_#é";
        var random = new Random(4242);
        var documents = new[] { Json(NasStatus), Json(DockerContainers), Json(UptimeKumaHeartbeat), Json("null"), Json("\"text\"") };

        for (var i = 0; i < 20_000; i++)
        {
            var length = random.Next(0, 24);
            var chars = new char[length];
            for (var j = 0; j < length; j++)
                chars[j] = alphabet[random.Next(alphabet.Length)];
            var text = (random.Next(3) == 0 ? "" : "$") + new string(chars);

            if (JsonPathLite.TryParse(text, out var path, out var error))
            {
                Assert.Null(error);
                foreach (var document in documents)
                    path.Evaluate(document);
            }
            else
            {
                Assert.NotNull(error);
                Assert.InRange(error.Position, 0, text.Length);
            }
        }
    }
}
