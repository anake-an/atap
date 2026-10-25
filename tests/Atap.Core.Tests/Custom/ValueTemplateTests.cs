using System.Globalization;
using Atap.Core.Custom;
using static Atap.Core.Tests.Custom.CustomFixtures;

namespace Atap.Core.Tests.Custom;

public class ValueTemplateTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static ValueView Render(ValueTemplateSpec spec, string json, ValueView? previous = null) =>
        Template(spec).Render(Json(json), previous, Invariant);

    // Number

    [Fact]
    public void Number_ReadsTheSpecExample()
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Number, "$.disk.usedPercent") { Unit = "%" }, NasStatus);

        Assert.True(view.HasValue);
        Assert.Equal(ValueTemplateKind.Number, view.Kind);
        Assert.Equal("72.5", view.Text);
        Assert.Equal("%", view.Unit);
        Assert.Equal("72.5%", view.DisplayText);
        Assert.Equal(72.5, view.Number);
        Assert.Equal(ValueLevel.Normal, view.Level);
        Assert.False(view.IsAlerting);
        Assert.False(view.Stale);
    }

    [Theory]
    [InlineData("$.queries.total", null, "48,213")]
    [InlineData("$.queries.percent_blocked", null, "20.19")]
    [InlineData("$.queries.percent_blocked", 1, "20.2")]
    [InlineData("$.queries.percent_blocked", 0, "20")]
    [InlineData("$.took", null, "0")]
    [InlineData("$.took", 4, "0.0003")]
    [InlineData("$.clients.active", 2, "14.00")]
    [InlineData("$.queries.types.length()", null, "16")]
    public void Number_FormatsWithDecimalsAndGrouping(string path, int? decimals, string expected)
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Number, path) { Decimals = decimals }, PiHoleSummary);

        Assert.Equal(expected, view.Text);
    }

    [Fact]
    public void Number_UsesTheGivenCulture()
    {
        var format = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

        var view = Template(new ValueTemplateSpec(ValueTemplateKind.Number, "$.disk.usedGb")).Render(Json(NasStatus), null, format);

        Assert.Equal("2.900,4", view.Text);
    }

    [Theory]
    [InlineData("""{ "v": "72.5" }""", 72.5)]
    [InlineData("""{ "v": " 72.5% " }""", 72.5)]
    [InlineData("""{ "v": "-3" }""", -3.0)]
    [InlineData("""{ "v": "1e3" }""", 1000.0)]
    [InlineData("""{ "v": -0.001 }""", -0.001)]
    public void Number_AcceptsNumericStrings(string json, double expected)
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Number, "$.v"), json);

        Assert.Equal(expected, view.Number);
    }

    [Fact]
    public void Number_NegativeZeroShowsAsZero()
    {
        Assert.Equal("0", Render(new ValueTemplateSpec(ValueTemplateKind.Number, "$.v"), """{ "v": -0.001 }""").Text);
    }

    [Theory]
    [InlineData("""{ "v": "1,5" }""", ValueProblem.NotANumber)]
    [InlineData("""{ "v": "NaN" }""", ValueProblem.NotANumber)]
    [InlineData("""{ "v": true }""", ValueProblem.NotANumber)]
    [InlineData("""{ "v": [1] }""", ValueProblem.NotANumber)]
    [InlineData("""{ "v": null }""", ValueProblem.NoValue)]
    [InlineData("""{ "w": 1 }""", ValueProblem.NotFound)]
    public void Number_WithoutANumber_HasAProblemAndNoLevel(string json, ValueProblem problem)
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Number, "$.v") { Thresholds = new ThresholdSpec(Warning: 0) { Hysteresis = 1 } }, json);

        Assert.Equal(problem, view.Problem);
        Assert.False(view.HasValue);
        Assert.Equal(ValueLevel.Unknown, view.Level);
        Assert.Equal(string.Empty, view.Text);
        Assert.Null(view.Number);
    }

    [Fact]
    public void Number_ThresholdsMakeItAlert()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.disk.usedPercent") { Thresholds = new ThresholdSpec(Warning: 70, Critical: 90) };

        var view = Render(spec, NasStatus);

        Assert.Equal(ValueLevel.Warning, view.Level);
        Assert.True(view.IsAlerting);
    }

    [Fact]
    public void Number_PreviousViewCarriesHysteresis()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.v") { Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 2 } };
        var template = Template(spec);

        var first = template.Render(Json("""{ "v": 80 }"""), null, Invariant);
        var held = template.Render(Json("""{ "v": 79 }"""), first, Invariant);
        var fresh = template.Render(Json("""{ "v": 79 }"""), null, Invariant);
        var left = template.Render(Json("""{ "v": 77 }"""), held, Invariant);

        Assert.Equal(ValueLevel.Warning, first.Level);
        Assert.Equal(ValueLevel.Warning, held.Level);
        Assert.Equal(ValueLevel.Normal, fresh.Level);
        Assert.Equal(ValueLevel.Normal, left.Level);
    }

    [Fact]
    public void Number_ViewWithNoValueKeepsHysteresis()
    {
        // An error blip between two values must not reset the band, or the level flaps.
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.v") { Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 2 } };
        var template = Template(spec);

        var hot = template.Render(Json("""{ "v": 80 }"""), null, Invariant);
        var empty = template.Render(new PayloadReadResult(PayloadStatus.Empty, default), hot, Invariant);
        var badJson = template.Render(new PayloadReadResult(PayloadStatus.InvalidJson, default), empty, Invariant);
        var notANumber = template.Render(Json("""{ "v": "n/a" }"""), badJson, Invariant);
        var held = template.Render(Json("""{ "v": 79 }"""), notANumber, Invariant);
        var left = template.Render(Json("""{ "v": 77 }"""), held, Invariant);
        var fresh = template.Render(Json("""{ "v": 79 }"""), template.Render(new PayloadReadResult(PayloadStatus.Empty, default)), Invariant);

        Assert.Equal(ValueLevel.Warning, hot.Level);
        Assert.All(new[] { empty, badJson, notANumber }, view =>
        {
            Assert.Equal(ValueLevel.Unknown, view.Level);
            Assert.False(view.IsAlerting);
        });
        Assert.Equal(ValueLevel.Warning, held.Level);
        Assert.Equal(ValueLevel.Normal, left.Level);
        Assert.Equal(ValueLevel.Normal, fresh.Level);
    }

    [Fact]
    public void Number_StaleViewKeepsItsValue()
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Number, "$.cpu.temp") { Unit = "°C" }, NasStatus);

        var stale = view with { Stale = true };

        Assert.True(stale.Stale);
        Assert.Equal("47°C", stale.DisplayText);
    }

    [Theory]
    [InlineData("  GB ", "12 GB")]
    [InlineData("ms", "12 ms")]
    [InlineData("°C", "12°C")]
    [InlineData("%", "12%")]
    [InlineData("", "12")]
    [InlineData("   ", "12")]
    [InlineData("a\u202Eb", "12 ab")]
    [InlineData("kilowatt-hours!", "12 kilowatt-hou")]
    public void Number_CleansTheUnit(string unit, string expected)
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Number, "$.v") { Unit = unit }, """{ "v": 12 }""");

        Assert.Equal(expected, view.DisplayText);
    }

    // Text

    [Fact]
    public void Text_ShowsAString()
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Text, "$.status"), NasStatus);

        Assert.Equal("healthy", view.Text);
        Assert.Equal(ValueLevel.Normal, view.Level);
        Assert.Null(view.Unit);
    }

    [Fact]
    public void Text_RemovesControlAndDirectionCharacters()
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Text, "$.msg"), """{ "msg": "  disk\u202E full\r\n\tsoon\u0007 \u2066x\u2069 " }""");

        Assert.Equal("disk full soon x", view.Text);
    }

    [Fact]
    public void Text_LongTextIsCutWithAnEllipsis()
    {
        var json = "{ \"msg\": \"" + new string('a', 500) + "\" }";

        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Text, "$.msg"), json);

        Assert.Equal(200, view.Text.Length);
        Assert.EndsWith("…", view.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_CutDoesNotSplitASurrogatePair()
    {
        var json = "{ \"msg\": \"" + new string('a', 198) + "\\uD83D\\uDE00\\uD83D\\uDE00\" }";

        var text = Render(new ValueTemplateSpec(ValueTemplateKind.Text, "$.msg"), json).Text;

        Assert.False(char.IsHighSurrogate(text[^2]));
        Assert.EndsWith("…", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_LoneSurrogateIsReplaced()
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Text, "$.msg"), """{ "msg": "a\uD800b" }""");

        Assert.Equal("a\uFFFDb", view.Text);
    }

    [Fact]
    public void Render_BrokenSurrogateEscapesNeverThrow()
    {
        const string json = """{ "a\uD800": "\uDC00", "b": "up\uD800", "c": { "x\uDBFF": 1 } }""";

        var list = Render(new ValueTemplateSpec(ValueTemplateKind.List), json);
        var dot = Render(new ValueTemplateSpec(ValueTemplateKind.StatusDot, "$.b"), json);
        var number = Render(new ValueTemplateSpec(ValueTemplateKind.Number, "$.b"), json);
        var nested = Render(new ValueTemplateSpec(ValueTemplateKind.List, "$.c"), json);

        Assert.Equal(new ValueListItem("a\uFFFD", "\uFFFD", ValueLevel.Unknown), list.Items[0]);
        Assert.Equal("up\uFFFD", dot.Text);
        Assert.Equal(ValueProblem.NotANumber, number.Problem);
        Assert.Equal("x\uFFFD", Assert.Single(nested.Items).Label);
    }

    [Theory]
    [InlineData("""{ "v": 12.346 }""", "12.35")]
    [InlineData("""{ "v": true }""", "true")]
    [InlineData("""{ "v": "1.0.3" }""", "1.0.3")]
    public void Text_ShowsNumbersAndBooleans(string json, string expected)
    {
        Assert.Equal(expected, Render(new ValueTemplateSpec(ValueTemplateKind.Text, "$.v"), json).Text);
    }

    [Theory]
    [InlineData("""{ "v": {} }""", ValueProblem.NotText)]
    [InlineData("""{ "v": [] }""", ValueProblem.NotText)]
    [InlineData("""{ "v": "  " }""", ValueProblem.NoValue)]
    [InlineData("""{ "v": null }""", ValueProblem.NoValue)]
    public void Text_WithoutText_HasAProblem(string json, ValueProblem problem)
    {
        Assert.Equal(problem, Render(new ValueTemplateSpec(ValueTemplateKind.Text, "$.v"), json).Problem);
    }

    [Fact]
    public void Text_NumericTextUsesThresholds()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Text, "$.v") { Thresholds = new ThresholdSpec(Warning: 10) };

        Assert.Equal(ValueLevel.Warning, Render(spec, """{ "v": "12" }""").Level);
        Assert.Equal(ValueLevel.Normal, Render(spec, """{ "v": "twelve" }""").Level);
    }

    // Progress

    [Fact]
    public void Progress_UsesAMaximumFromThePayload()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Progress, "$.disk.usedGb") { MaxPath = "$.disk.totalGb", Unit = "GB" };

        var view = Render(spec, NasStatus);

        Assert.Equal(0.7251, view.Fraction!.Value, 4);
        Assert.Equal("73", view.Text);
        Assert.Equal("%", view.Unit);
        Assert.Equal("73%", view.DisplayText);
        Assert.Equal("2,900.4 / 4,000 GB", view.Detail);
        Assert.Equal(2900.4, view.Number);
    }

    [Fact]
    public void Progress_GlancesBytesWithThresholdsOnThePercentage()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Progress, "$[1].used")
        {
            MaxPath = "$[1].size",
            Decimals = 1,
            Thresholds = new ThresholdSpec(Warning: 75, Critical: 90),
        };

        var view = Render(spec, GlancesFileSystems);

        Assert.Equal("79.9", view.Text);
        Assert.Equal(ValueLevel.Warning, view.Level);
    }

    [Theory]
    [InlineData(72.5, 0.725)]
    [InlineData(150.0, 1.0)]
    [InlineData(-5.0, 0.0)]
    public void Progress_DefaultsTo0To100AndClamps(double value, double fraction)
    {
        var json = "{ \"v\": " + value.ToString(CultureInfo.InvariantCulture) + " }";

        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Progress, "$.v"), json);

        Assert.Equal(fraction, view.Fraction!.Value, 6);
    }

    [Fact]
    public void Progress_UsesMinAndMax()
    {
        // Temperature on a 30 to 80 degree scale.
        var spec = new ValueTemplateSpec(ValueTemplateKind.Progress, "$.cpu.temp") { Min = 30, Max = 80 };

        Assert.Equal(0.34, Render(spec, NasStatus).Fraction!.Value, 6);
    }

    [Theory]
    [InlineData("""{ "v": 5, "max": 0 }""")]
    [InlineData("""{ "v": 5, "max": "lots" }""")]
    [InlineData("""{ "v": 5 }""")]
    public void Progress_BadMaximumFromThePayload_IsAProblem(string json)
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.Progress, "$.v") { MaxPath = "$.max" }, json);

        Assert.Equal(ValueProblem.BadMaximum, view.Problem);
        Assert.Null(view.Fraction);
    }

    // StatusDot

    [Theory]
    [InlineData("$.heartbeatList['1'][-1].status", ValueLevel.Normal, "1")]
    [InlineData("$.heartbeatList['2'][-1].status", ValueLevel.Critical, "0")]
    [InlineData("$.heartbeatList['3'][-1].status", ValueLevel.Unknown, "2")]
    public void StatusDot_UptimeKumaStatusNumbers(string path, ValueLevel level, string text)
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.StatusDot, path), UptimeKumaHeartbeat);

        Assert.Equal(level, view.Level);
        Assert.Equal(text, view.Text);
        Assert.True(view.HasValue);
    }

    [Fact]
    public void StatusDot_CustomWordsReplaceOneList()
    {
        // Uptime Kuma: 2 is pending and 3 is maintenance.
        var spec = new ValueTemplateSpec(ValueTemplateKind.StatusDot, "$.heartbeatList['3'][-1].status") { WarningValues = ["2", "3"] };

        var view = Render(spec, UptimeKumaHeartbeat);

        Assert.Equal(ValueLevel.Warning, view.Level);
        Assert.Equal(ValueLevel.Critical, Render(spec with { ValuePath = "$.heartbeatList['2'][-1].status" }, UptimeKumaHeartbeat).Level);
    }

    [Theory]
    [InlineData("""{ "s": "enabled" }""", ValueLevel.Normal)]
    [InlineData("""{ "s": "  ONLINE " }""", ValueLevel.Normal)]
    [InlineData("""{ "s": "Healthy" }""", ValueLevel.Normal)]
    [InlineData("""{ "s": true }""", ValueLevel.Normal)]
    [InlineData("""{ "s": 1.0 }""", ValueLevel.Normal)]
    [InlineData("""{ "s": "degraded" }""", ValueLevel.Warning)]
    [InlineData("""{ "s": "Maintenance" }""", ValueLevel.Warning)]
    [InlineData("""{ "s": "down" }""", ValueLevel.Critical)]
    [InlineData("""{ "s": false }""", ValueLevel.Critical)]
    [InlineData("""{ "s": "exited" }""", ValueLevel.Critical)]
    [InlineData("""{ "s": 0 }""", ValueLevel.Critical)]
    [InlineData("""{ "s": "sleeping" }""", ValueLevel.Unknown)]
    public void StatusDot_DefaultWords(string json, ValueLevel level)
    {
        Assert.Equal(level, Render(new ValueTemplateSpec(ValueTemplateKind.StatusDot, "$.s"), json).Level);
    }

    [Fact]
    public void StatusDot_PiHoleV5Status()
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.StatusDot, "$.status"), PiHoleV5Summary);

        Assert.Equal(ValueLevel.Normal, view.Level);
        Assert.Equal("enabled", view.Text);
    }

    [Fact]
    public void StatusDot_WordInTwoListsTakesTheWorse()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.StatusDot, "$.s") { OkValues = ["open"], CriticalValues = ["open", "closed"] };

        Assert.Equal(ValueLevel.Critical, Render(spec, """{ "s": "open" }""").Level);
    }

    [Fact]
    public void StatusDot_EmptyListMatchesNothing()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.StatusDot, "$.s") { CriticalValues = [] };

        Assert.Equal(ValueLevel.Unknown, Render(spec, """{ "s": "down" }""").Level);
    }

    [Fact]
    public void StatusDot_NumberWithThresholdsUsesHysteresis()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.StatusDot, "$.cpu.temp") { Thresholds = new ThresholdSpec(Warning: 47, Critical: 70) };
        var template = Template(spec);

        var hot = template.Render(Json(NasStatus), null, Invariant);
        var cooler = template.Render(Json("""{ "cpu": { "temp": 46.5 } }"""), hot, Invariant);

        Assert.Equal(ValueLevel.Warning, hot.Level);
        Assert.Equal(ValueLevel.Warning, cooler.Level);
    }

    [Theory]
    [InlineData("""{ "s": {} }""", ValueProblem.NotText)]
    [InlineData("""{ "s": null }""", ValueProblem.NoValue)]
    [InlineData("""{ }""", ValueProblem.NotFound)]
    public void StatusDot_WithoutAValue_IsUnknown(string json, ValueProblem problem)
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.StatusDot, "$.s"), json);

        Assert.Equal(problem, view.Problem);
        Assert.Equal(ValueLevel.Unknown, view.Level);
    }

    // List

    [Fact]
    public void List_DockerContainersWithState()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List) { ItemLabelPath = "$.Names[0]", ItemValuePath = "$.State" };

        var view = Render(spec, DockerContainers);

        Assert.Equal(3, view.ItemCount);
        Assert.Equal("3", view.Text);
        Assert.Equal(3, view.Number);
        Assert.Equal(
            [
                new ValueListItem("/jellyfin", "running", ValueLevel.Normal),
                new ValueListItem("/nextcloud", "running", ValueLevel.Normal),
                new ValueListItem("/backup", "exited", ValueLevel.Critical),
            ],
            view.Items);
        Assert.Equal(ValueLevel.Critical, view.Level);
        Assert.True(view.IsAlerting);
    }

    [Fact]
    public void List_WildcardPathWithLabels()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List, "$.publicGroupList[*].monitorList[*]") { ItemLabelPath = "$.name" };

        var view = Render(spec, UptimeKumaStatusPage);

        Assert.Equal(["Jellyfin", "Nextcloud", "Router"], view.Items.Select(i => i.Label));
        Assert.All(view.Items, item => Assert.Null(item.Value));
        Assert.All(view.Items, item => Assert.Equal(ValueLevel.Normal, item.Level));
        Assert.Equal(ValueLevel.Normal, view.Level);
    }

    [Fact]
    public void List_ObjectMembersUseTheirNamesAndValues()
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.List), """{ "nas": "up", "router": "down", "printer": "pending" }""");

        Assert.Equal(
            [
                new ValueListItem("nas", "up", ValueLevel.Normal),
                new ValueListItem("router", "down", ValueLevel.Critical),
                new ValueListItem("printer", "pending", ValueLevel.Warning),
            ],
            view.Items);
    }

    [Fact]
    public void List_ObjectOfObjectsNeedsAValuePath()
    {
        const string json = """{ "nas": { "status": "up" }, "router": { "status": "down" } }""";

        var withoutPath = Render(new ValueTemplateSpec(ValueTemplateKind.List), json);
        var withPath = Render(new ValueTemplateSpec(ValueTemplateKind.List) { ItemValuePath = "$.status" }, json);

        Assert.All(withoutPath.Items, item => Assert.Equal(ValueLevel.Normal, item.Level));
        Assert.Equal(["up", "down"], withPath.Items.Select(i => i.Value));
        Assert.Equal(ValueLevel.Critical, withPath.Level);
    }

    [Fact]
    public void List_ArrayOfScalarsUsesTheItemsAsLabels()
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.List, "$[*].Image"), DockerContainers);

        Assert.Equal(["jellyfin/jellyfin:10.10", "nextcloud:30-apache", "restic/restic:0.17"], view.Items.Select(i => i.Label));
    }

    [Fact]
    public void List_MaxItemsCapsTheRowsButNotTheLevel()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List) { ItemLabelPath = "$.Names[0]", ItemValuePath = "$.State", MaxItems = 2 };

        var view = Render(spec, DockerContainers);

        Assert.Equal(2, view.Items.Count);
        Assert.Equal(3, view.ItemCount);
        Assert.Equal(ValueLevel.Critical, view.Level);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(100, ValueTemplateSpec.MaxItemsLimit)]
    public void List_MaxItemsIsClamped(int maxItems, int expected)
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 30)) + "]";

        var view = Render(new ValueTemplateSpec(ValueTemplateKind.List) { MaxItems = maxItems }, json);

        Assert.Equal(expected, view.Items.Count);
        Assert.Equal(30, view.ItemCount);
    }

    [Theory]
    [InlineData("$.maintenanceList")]
    [InlineData("$.maintenanceList[*]")]
    public void List_EmptyListIsAValue(string path)
    {
        var view = Render(new ValueTemplateSpec(ValueTemplateKind.List, path), UptimeKumaStatusPage);

        Assert.True(view.HasValue);
        Assert.Equal(0, view.ItemCount);
        Assert.Empty(view.Items);
        Assert.Equal("0", view.Text);
        Assert.Equal(ValueLevel.Normal, view.Level);
    }

    [Theory]
    [InlineData("$.config.title", ValueProblem.NotAList)]
    [InlineData("$.incident", ValueProblem.NoValue)]
    [InlineData("$.missing", ValueProblem.NotFound)]
    [InlineData("$.missing[*]", ValueProblem.NotFound)]
    public void List_WithoutAList_HasAProblem(string path, ValueProblem problem)
    {
        Assert.Equal(problem, Render(new ValueTemplateSpec(ValueTemplateKind.List, path), UptimeKumaStatusPage).Problem);
    }

    [Fact]
    public void List_ItemThresholdsUseEachItemsLastLevel()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List, "$[*]")
        {
            ItemLabelPath = "$.mnt_point",
            ItemValuePath = "$.percent",
            MaxItems = 1,
            Thresholds = new ThresholdSpec(Warning: 79) { Hysteresis = 2 },
        };
        var template = Template(spec);
        const string cooler = """
            [
              { "mnt_point": "/", "percent": 25.9 },
              { "mnt_point": "/volume1", "percent": 78.2 }
            ]
            """;

        var first = template.Render(Json(GlancesFileSystems), null, Invariant);
        var held = template.Render(Json(cooler), first, Invariant);
        var fresh = template.Render(Json(cooler), null, Invariant);

        // The second disk is not shown with MaxItems 1, but its level still counts and keeps its hysteresis.
        Assert.Equal(ValueLevel.Warning, first.Level);
        Assert.Equal(ValueLevel.Warning, held.Level);
        Assert.Equal(ValueLevel.Normal, fresh.Level);
    }

    [Fact]
    public void List_ViewWithNoValueKeepsItemLevels()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List, "$[*]")
        {
            ItemLabelPath = "$.mnt_point",
            ItemValuePath = "$.percent",
            Thresholds = new ThresholdSpec(Warning: 79) { Hysteresis = 2 },
        };
        var template = Template(spec);
        const string cooler = """[ { "mnt_point": "/volume1", "percent": 78.2 } ]""";

        var first = template.Render(Json(GlancesFileSystems), null, Invariant);
        var blip = template.Render(new PayloadReadResult(PayloadStatus.TooLarge, default), first, Invariant);
        var held = template.Render(Json(cooler), blip, Invariant);

        Assert.Equal(ValueLevel.Unknown, blip.Level);
        Assert.Equal(ValueLevel.Warning, held.Level);
    }

    [Fact]
    public void List_MissingItemValueIsUnknown()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List) { ItemLabelPath = "$.name", ItemValuePath = "$.state" };

        var view = Render(spec, """[ { "name": "a", "state": "up" }, { "name": "b" } ]""");

        Assert.Equal(new ValueListItem("b", null, ValueLevel.Unknown), view.Items[1]);
        Assert.Equal(ValueLevel.Normal, view.Level);
    }

    [Fact]
    public void List_ScalarItemsKeepTheirBandByPosition()
    {
        // A scalar item is labelled with its own value, so the label changes on every refresh.
        var spec = new ValueTemplateSpec(ValueTemplateKind.List, "$.temps") { ItemValuePath = "$", Thresholds = new ThresholdSpec(Warning: 80) };
        var template = Template(spec);
        ValueView? view = null;

        foreach (var json in new[] { """{ "temps": [80.1] }""", """{ "temps": [79.5] }""", """{ "temps": [80.2] }""", """{ "temps": [79.6] }""" })
        {
            view = template.Render(Json(json), view, Invariant);

            Assert.Equal(ValueLevel.Warning, view.Items[0].Level);
            Assert.Equal(ValueLevel.Warning, view.Level);
            Assert.True(view.IsAlerting);
        }
    }

    [Fact]
    public void List_ItemsWithoutLabelsKeepTheirOwnBand()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List) { ItemValuePath = "$.v", Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 5 } };
        var template = Template(spec);

        var first = template.Render(Json("""[ { "v": 85 }, { "v": 10 } ]"""), null, Invariant);
        var second = template.Render(Json("""[ { "v": 79 }, { "v": 79 } ]"""), first, Invariant);

        Assert.Equal([ValueLevel.Warning, ValueLevel.Normal], first.Items.Select(i => i.Level));
        Assert.Equal([ValueLevel.Warning, ValueLevel.Normal], second.Items.Select(i => i.Level));
    }

    [Fact]
    public void List_RepeatedLabelsKeepTheirOwnBand()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List)
        {
            ItemLabelPath = "$.name",
            ItemValuePath = "$.v",
            Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 5 },
        };
        var template = Template(spec);

        var first = template.Render(Json("""[ { "name": "disk", "v": 85 }, { "name": "disk", "v": 10 } ]"""), null, Invariant);
        var second = template.Render(Json("""[ { "name": "disk", "v": 79 }, { "name": "disk", "v": 79 } ]"""), first, Invariant);

        Assert.Equal([ValueLevel.Warning, ValueLevel.Normal], second.Items.Select(i => i.Level));
    }

    [Fact]
    public void List_LabelledItemsKeepTheirBandWhenTheOrderChanges()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List)
        {
            ItemLabelPath = "$.name",
            ItemValuePath = "$.v",
            Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 5 },
        };
        var template = Template(spec);

        var first = template.Render(Json("""[ { "name": "a", "v": 85 }, { "name": "b", "v": 10 } ]"""), null, Invariant);
        var second = template.Render(Json("""[ { "name": "b", "v": 79 }, { "name": "a", "v": 79 } ]"""), first, Invariant);

        Assert.Equal([new ValueListItem("b", "79", ValueLevel.Normal), new ValueListItem("a", "79", ValueLevel.Warning)], second.Items);
    }

    [Fact]
    public void List_ObjectMembersKeepTheirBandByName()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List) { Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 5 } };
        var template = Template(spec);

        var first = template.Render(Json("""{ "a": 85, "b": 10 }"""), null, Invariant);
        var second = template.Render(Json("""{ "b": 79, "a": 79 }"""), first, Invariant);

        Assert.Equal([new ValueListItem("b", "79", ValueLevel.Normal), new ValueListItem("a", "79", ValueLevel.Warning)], second.Items);
    }

    [Fact]
    public void List_ItemWithNoValueKeepsItsBand()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List)
        {
            ItemLabelPath = "$.name",
            ItemValuePath = "$.v",
            Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 5 },
        };
        var template = Template(spec);

        var first = template.Render(Json("""[ { "name": "a", "v": 85 } ]"""), null, Invariant);
        var blip = template.Render(Json("""[ { "name": "a", "v": null } ]"""), first, Invariant);
        var held = template.Render(Json("""[ { "name": "a", "v": 79 } ]"""), blip, Invariant);

        Assert.Equal(ValueLevel.Unknown, blip.Items[0].Level);
        Assert.Equal(ValueLevel.Warning, held.Items[0].Level);
    }

    // No-value words

    [Fact]
    public void NoValueWords_TurnAFieldIntoNoValue()
    {
        // Home Assistant REST API, GET /api/states/sensor.nas_cpu_temperature, while the sensor cannot be read.
        const string unavailable = """{ "entity_id": "sensor.nas_cpu_temperature", "state": "unavailable" }""";
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.state") { NoValueWords = StatusWords.DefaultNoValue };

        Assert.Equal(ValueProblem.NoValue, Render(spec, unavailable).Problem);
        Assert.Equal(ValueProblem.NoValue, Render(spec with { Kind = ValueTemplateKind.Text }, unavailable).Problem);
        Assert.Equal(ValueProblem.NoValue, Render(spec with { Kind = ValueTemplateKind.Progress }, unavailable).Problem);
        Assert.Equal(ValueProblem.NoValue, Render(spec with { Kind = ValueTemplateKind.StatusDot }, """{ "state": " Unknown " }""").Problem);
        Assert.Equal(ValueLevel.Unknown, Render(spec with { Kind = ValueTemplateKind.StatusDot }, unavailable).Level);
        Assert.Equal(47.5, Render(spec, """{ "state": "47.5" }""").Number);
    }

    [Fact]
    public void NoValueWords_AreOffByDefault()
    {
        const string unavailable = """{ "state": "unavailable" }""";
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.state");

        Assert.Equal(ValueProblem.NotANumber, Render(spec, unavailable).Problem);
        Assert.Equal("unavailable", Render(spec with { Kind = ValueTemplateKind.Text }, unavailable).Text);
        Assert.Equal(ValueProblem.NotANumber, Render(spec with { NoValueWords = [] }, unavailable).Problem);
    }

    [Fact]
    public void NoValueWords_KeepHysteresis()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.state")
        {
            NoValueWords = StatusWords.DefaultNoValue,
            Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 5 },
        };
        var template = Template(spec);

        var hot = template.Render(Json("""{ "state": "85" }"""), null, Invariant);
        var blip = template.Render(Json("""{ "state": "unavailable" }"""), hot, Invariant);
        var held = template.Render(Json("""{ "state": "79" }"""), blip, Invariant);

        Assert.Equal(ValueProblem.NoValue, blip.Problem);
        Assert.Equal(ValueLevel.Warning, held.Level);
    }

    [Fact]
    public void NoValueWords_ApplyToListItems()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.List) { NoValueWords = StatusWords.DefaultNoValue };

        var members = Render(spec, """{ "nas": "on", "router": "unavailable" }""");
        var items = Render(spec with { ItemLabelPath = "$.name", ItemValuePath = "$.state" }, """[ { "name": "nas", "state": "unknown" } ]""");

        Assert.Equal(new ValueListItem("router", null, ValueLevel.Unknown), members.Items[1]);
        Assert.Equal(ValueLevel.Normal, members.Level);
        Assert.Equal(new ValueListItem("nas", null, ValueLevel.Unknown), items.Items[0]);
    }

    // Shared behaviour

    [Fact]
    public void Render_PreviousViewOfAnotherKindIsIgnored()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.v") { Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 5 } };
        var other = new ValueView { Kind = ValueTemplateKind.Text, Level = ValueLevel.Warning };

        Assert.Equal(ValueLevel.Normal, Render(spec, """{ "v": 79 }""", other).Level);
    }

    [Theory]
    [InlineData(PayloadStatus.Empty, ValueProblem.NoValue)]
    [InlineData(PayloadStatus.InvalidJson, ValueProblem.InvalidJson)]
    [InlineData(PayloadStatus.InvalidText, ValueProblem.InvalidText)]
    [InlineData(PayloadStatus.TooLarge, ValueProblem.TooLarge)]
    public void Render_UnreadablePayloadHasNoValue(PayloadStatus status, ValueProblem problem)
    {
        var template = Template(new ValueTemplateSpec(ValueTemplateKind.Number));

        var view = template.Render(new PayloadReadResult(status, default));

        Assert.Equal(problem, view.Problem);
        Assert.Equal(ValueLevel.Unknown, view.Level);
    }

    [Fact]
    public void Render_ReadPayloadIsRendered()
    {
        var template = Template(new ValueTemplateSpec(ValueTemplateKind.Number, "$.disk.usedPercent"));

        var view = template.Render(PayloadReader.Read(NasStatus, PayloadFormat.Json), null, Invariant);

        Assert.Equal(72.5, view.Number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryCreate_EmptyValuePathMeansRoot(string? path)
    {
        var template = Template(new ValueTemplateSpec(ValueTemplateKind.Number, path!));

        Assert.Equal(47, template.Render(Json("47"), null, Invariant).Number);
    }

    [Theory]
    [InlineData(ValueTemplateKind.Number, "$..v", null, null, null, ValueTemplateField.ValuePath)]
    [InlineData(ValueTemplateKind.Progress, "$.v", "$.max()", null, null, ValueTemplateField.MaxPath)]
    [InlineData(ValueTemplateKind.List, "$", null, "$[0:1]", null, ValueTemplateField.ItemLabelPath)]
    [InlineData(ValueTemplateKind.List, "$", null, null, "name", ValueTemplateField.ItemValuePath)]
    public void TryCreate_BadPathNamesTheField(ValueTemplateKind kind, string valuePath, string? maxPath, string? labelPath, string? itemValuePath, ValueTemplateField field)
    {
        var spec = new ValueTemplateSpec(kind, valuePath) { MaxPath = maxPath, ItemLabelPath = labelPath, ItemValuePath = itemValuePath };

        Assert.False(ValueTemplate.TryCreate(spec, out var template, out var error));
        Assert.Null(template);
        Assert.Equal(field, error.Field);
        Assert.NotNull(error.Path);
    }

    [Fact]
    public void TryCreate_IgnoresSettingsForOtherTemplates()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.v") { MaxPath = "$..x", ItemLabelPath = "$[?]", Max = -5 };

        Assert.True(ValueTemplate.TryCreate(spec, out _, out _));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(50.0, 10.0)]
    [InlineData(double.NaN, 100.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    public void TryCreate_ProgressRangeMustBeFiniteAndRising(double min, double max)
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Progress) { Min = min, Max = max };

        Assert.False(ValueTemplate.TryCreate(spec, out _, out var error));
        Assert.Equal(ValueTemplateField.Range, error.Field);
    }

    [Fact]
    public void TryCreate_BadThresholdsAreRejected()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number) { Thresholds = new ThresholdSpec(Warning: 90, Critical: 80) };

        Assert.False(ValueTemplate.TryCreate(spec, out _, out var error));
        Assert.Equal(ValueTemplateField.Thresholds, error.Field);
        Assert.Equal(ThresholdProblem.WrongOrder, error.Threshold);
    }

    [Fact]
    public void TryCreate_ZeroThresholdNeedsASetBand()
    {
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number) { Thresholds = new ThresholdSpec(Warning: 0) };

        Assert.False(ValueTemplate.TryCreate(spec, out _, out var error));
        Assert.Equal(ValueTemplateField.Thresholds, error.Field);
        Assert.Equal(ThresholdProblem.HysteresisRequired, error.Threshold);
        Assert.True(ValueTemplate.TryCreate(spec with { Thresholds = new ThresholdSpec(Warning: 0) { Hysteresis = 1 } }, out _, out _));
    }

    [Fact]
    public void Render_RandomPayloadsNeverThrow()
    {
        var random = new Random(7);
        var payloads = new[] { NasStatus, PiHoleSummary, UptimeKumaHeartbeat, DockerContainers, GlancesFileSystems, "null", "[]", "{}", "\"x\"", "1e308", "[[[[1]]]]" };
        var paths = new[] { "$", "$[*]", "$.disk", "$[0]", "$[-1].State", "$.*.*", "$.length()", "$[*].length()", "$.queries.types" };
        var kinds = Enum.GetValues<ValueTemplateKind>();

        for (var i = 0; i < 2_000; i++)
        {
            var spec = new ValueTemplateSpec(kinds[random.Next(kinds.Length)], paths[random.Next(paths.Length)])
            {
                MaxPath = random.Next(2) == 0 ? null : paths[random.Next(paths.Length)],
                ItemLabelPath = random.Next(2) == 0 ? null : paths[random.Next(paths.Length)],
                ItemValuePath = random.Next(2) == 0 ? null : paths[random.Next(paths.Length)],
                Thresholds = random.Next(2) == 0 ? null : new ThresholdSpec(Warning: random.Next(100)) { Hysteresis = 1 },
            };
            var template = Template(spec);

            var view = template.Render(Json(payloads[random.Next(payloads.Length)]), null, Invariant);

            Assert.NotNull(view.Text);
            Assert.InRange(view.Fraction ?? 0, 0, 1);
        }
    }
}
