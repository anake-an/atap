using System.Globalization;
using System.Text;
using Atap.Core.Custom;
using static Atap.Core.Tests.Custom.CustomFixtures;

namespace Atap.Core.Tests.Custom;

public class MqttMappingTests
{
    private const string CpuTopic = "homeassistant/sensor/nas_cpu_temperature/state";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static ValueView Map(MqttMapping mapping, string topic, string payload, ValueTemplateSpec spec)
    {
        Assert.True(mapping.TryMap(topic, Utf8(payload), out var read));
        return Template(spec).Render(read, null, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void PlainPayload_MapsAtTheRoot()
    {
        var view = Map(new MqttMapping(CpuTopic), CpuTopic, HomeAssistantPlainState, new ValueTemplateSpec(ValueTemplateKind.Number) { Unit = "°C" });

        Assert.Equal(47, view.Number);
        Assert.Equal("47°C", view.DisplayText);
    }

    [Fact]
    public void JsonPayload_MapsAField()
    {
        const string topic = "zigbee2mqtt/living_room_sensor";
        var mapping = new MqttMapping(topic);

        var temperature = Map(mapping, topic, Zigbee2MqttSensor, new ValueTemplateSpec(ValueTemplateKind.Number, "$.temperature") { Unit = "°C", Decimals = 1 });
        var battery = Map(
            mapping,
            topic,
            Zigbee2MqttSensor,
            new ValueTemplateSpec(ValueTemplateKind.Progress, "$.battery") { Thresholds = new ThresholdSpec(Warning: 20, Critical: 10, ThresholdDirection.Below) });

        Assert.Equal("27.3°C", temperature.DisplayText);
        Assert.Equal(0.97, battery.Fraction);
        Assert.Equal(ValueLevel.Normal, battery.Level);
    }

    [Fact]
    public void NestedJsonPayload_MapsAField()
    {
        const string topic = "tele/plug_desk/SENSOR";

        var view = Map(new MqttMapping(topic, PayloadFormat.Json), topic, TasmotaSensor, new ValueTemplateSpec(ValueTemplateKind.Number, "$.ENERGY.Power") { Unit = "W" });

        Assert.Equal("45 W", view.DisplayText);
    }

    [Fact]
    public void JsonEncodedStringPayload_IsItsText()
    {
        const string topic = "homeassistant/sensor/nas_cpu_temperature/unit_of_measurement";

        var view = Map(new MqttMapping(topic), topic, HomeAssistantStatestreamUnit, new ValueTemplateSpec(ValueTemplateKind.Text));

        Assert.Equal("°C", view.Text);
    }

    [Theory]
    [InlineData("ON", ValueLevel.Normal)]
    [InlineData("OFF", ValueLevel.Critical)]
    [InlineData("on", ValueLevel.Normal)]
    [InlineData("home", ValueLevel.Unknown)]
    public void BinarySensorPayload_MapsToAStatusDot(string payload, ValueLevel level)
    {
        const string topic = "homeassistant/binary_sensor/nas_online/state";

        var view = Map(new MqttMapping(topic), topic, payload, new ValueTemplateSpec(ValueTemplateKind.StatusDot));

        Assert.Equal(level, view.Level);
        Assert.Equal(payload, view.Text);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    [InlineData(" Unavailable ")]
    [InlineData("\"unavailable\"")]
    [InlineData("")]
    [InlineData("   ")]
    public void NoValueStates_ShowAsUnknown(string payload)
    {
        var mapping = new MqttMapping(CpuTopic);

        Assert.True(mapping.TryMap(CpuTopic, Utf8(payload), out var read));
        var view = Template(new ValueTemplateSpec(ValueTemplateKind.Number)).Render(read);

        Assert.Equal(PayloadStatus.Empty, read.Status);
        Assert.Equal(ValueProblem.NoValue, view.Problem);
        Assert.Equal(ValueLevel.Unknown, view.Level);
    }

    [Fact]
    public void BadPayload_ShowsAsUnknown()
    {
        var view = Map(new MqttMapping(CpuTopic), CpuTopic, "hot", new ValueTemplateSpec(ValueTemplateKind.Number) { Thresholds = new ThresholdSpec(Warning: 70) });

        Assert.Equal(ValueProblem.NotANumber, view.Problem);
        Assert.Equal(ValueLevel.Unknown, view.Level);
    }

    [Fact]
    public void JsonFormat_RefusesPlainText()
    {
        var mapping = new MqttMapping(CpuTopic, PayloadFormat.Json);

        Assert.True(mapping.TryMap(CpuTopic, Utf8("on"), out var read));

        Assert.Equal(PayloadStatus.InvalidJson, read.Status);
        Assert.Equal(ValueProblem.InvalidJson, Template(new ValueTemplateSpec(ValueTemplateKind.Text)).Render(read).Problem);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData(" Unknown ")]
    [InlineData("\"unavailable\"")]
    public void JsonFormat_NoValueStatesShowAsNoValue(string payload)
    {
        // A plain word is caught before parsing, so it is not reported as bad JSON.
        var mapping = new MqttMapping(CpuTopic, PayloadFormat.Json);

        Assert.True(mapping.TryMap(CpuTopic, Utf8(payload), out var read));

        Assert.Equal(PayloadStatus.Empty, read.Status);
        Assert.Equal(PayloadStatus.Empty, mapping.Read(payload).Status);
        Assert.Equal(ValueProblem.NoValue, Template(new ValueTemplateSpec(ValueTemplateKind.Number)).Render(read).Problem);
    }

    [Fact]
    public void JsonFieldWithANoValueState_ShowsAsNoValueWithNoValueWords()
    {
        const string payload = """{ "state": "unavailable", "attributes": { "unit_of_measurement": "°C" } }""";
        var mapping = new MqttMapping(CpuTopic, PayloadFormat.Json);
        var spec = new ValueTemplateSpec(ValueTemplateKind.Number, "$.state");

        Assert.Equal(ValueProblem.NotANumber, Map(mapping, CpuTopic, payload, spec).Problem);
        Assert.Equal(ValueProblem.NoValue, Map(mapping, CpuTopic, payload, spec with { NoValueWords = StatusWords.DefaultNoValue }).Problem);
    }

    [Fact]
    public void JsonFormat_AcceptsABareNumber()
    {
        var view = Map(new MqttMapping(CpuTopic, PayloadFormat.Json), CpuTopic, "47.0", new ValueTemplateSpec(ValueTemplateKind.Number));

        Assert.Equal(47, view.Number);
    }

    [Fact]
    public void PlainFormat_KeepsJsonAsText()
    {
        const string topic = "zigbee2mqtt/living_room_sensor";
        var mapping = new MqttMapping(topic, PayloadFormat.Plain);

        var field = Map(mapping, topic, Zigbee2MqttSensor, new ValueTemplateSpec(ValueTemplateKind.Number, "$.temperature"));
        var whole = Map(mapping, topic, Zigbee2MqttSensor, new ValueTemplateSpec(ValueTemplateKind.Text));

        Assert.Equal(ValueProblem.NotFound, field.Problem);
        Assert.Equal(Zigbee2MqttSensor, whole.Text);
    }

    [Fact]
    public void AutoFormat_FallsBackToTextForBrokenJson()
    {
        var view = Map(new MqttMapping(CpuTopic), CpuTopic, "{\"temp\": ", new ValueTemplateSpec(ValueTemplateKind.Text));

        Assert.Equal("{\"temp\":", view.Text);
    }

    [Fact]
    public void OtherTopic_IsNotMapped()
    {
        var mapping = new MqttMapping(CpuTopic);

        Assert.False(mapping.TryMap("homeassistant/sensor/nas_cpu_usage/state", Utf8("12"), out var read));
        Assert.Equal(default, read);
    }

    [Fact]
    public void WildcardTopic_MapsEveryMatchingDevice()
    {
        var mapping = new MqttMapping("zigbee2mqtt/+");

        Assert.True(mapping.Matches("zigbee2mqtt/living_room_sensor"));
        Assert.False(mapping.Matches("zigbee2mqtt/bridge/state"));
        Assert.Equal(MqttTopicProblem.None, mapping.Validate());
    }

    [Fact]
    public void Payload_OverTheCapIsRefused()
    {
        var mapping = new MqttMapping(CpuTopic);
        var big = new byte[MqttMapping.MaxPayloadBytes + 1];
        big.AsSpan().Fill((byte)'1');

        Assert.True(mapping.TryMap(CpuTopic, big, out var read));
        Assert.Equal(PayloadStatus.TooLarge, read.Status);
        Assert.Equal(PayloadStatus.Ok, mapping.Read(big.AsSpan(0, MqttMapping.MaxPayloadBytes)).Status);
    }

    [Fact]
    public void Payload_ThatIsNotUtf8IsRefused()
    {
        Assert.True(new MqttMapping(CpuTopic).TryMap(CpuTopic, [0xC3, 0x28], out var read));

        Assert.Equal(PayloadStatus.InvalidText, read.Status);
    }

    [Fact]
    public void Payload_WithByteOrderMarkIsRead()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8("47.5")).ToArray();

        var view = Template(new ValueTemplateSpec(ValueTemplateKind.Number)).Render(new MqttMapping(CpuTopic).Read(bytes), null, CultureInfo.InvariantCulture);

        Assert.Equal(47.5, view.Number);
    }

    [Fact]
    public void StringPayload_MapsLikeBytes()
    {
        var mapping = new MqttMapping(CpuTopic);

        Assert.Equal(PayloadStatus.Empty, mapping.Read("unavailable").Status);
        Assert.Equal("47.0", mapping.Read(HomeAssistantPlainState).Root.GetString());
    }

    [Fact]
    public void StringPayload_WithLoneSurrogateDoesNotThrow()
    {
        // Raw characters, not JSON escapes. Theory data cannot carry lone surrogates, so this is a fact.
        var auto = new MqttMapping("a/b").Read("[\"\uDC00\"]");
        var json = new MqttMapping("a/b", PayloadFormat.Json).Read("{\"t\":\"x\uD800\"}");
        var plain = new MqttMapping("a/b", PayloadFormat.Plain).Read("\uD800");

        Assert.Equal("\uFFFD", auto.Root[0].GetString());
        Assert.Equal("x\uFFFD", json.Root.GetProperty("t").GetString());
        Assert.Equal("\uFFFD", plain.Root.GetString());
    }

    [Fact]
    public void UnavailableBlip_DoesNotResetHysteresis()
    {
        var mapping = new MqttMapping(CpuTopic);
        var template = Template(new ValueTemplateSpec(ValueTemplateKind.Number) { Thresholds = new ThresholdSpec(Warning: 80) { Hysteresis = 2 } });
        var levels = new List<ValueLevel>();
        ValueView? last = null;

        foreach (var payload in new[] { "80", "unavailable", "79", "unknown", "80", "79", "77.5" })
        {
            Assert.True(mapping.TryMap(CpuTopic, Utf8(payload), out var read));
            last = template.Render(read, last, CultureInfo.InvariantCulture);
            levels.Add(last.Level);
        }

        Assert.Equal(
            [
                ValueLevel.Warning, ValueLevel.Unknown, ValueLevel.Warning, ValueLevel.Unknown,
                ValueLevel.Warning, ValueLevel.Warning, ValueLevel.Normal,
            ],
            levels);
    }

    // Topic rules

    [Theory]
    [InlineData("homeassistant/sensor/nas/state", MqttTopicProblem.None)]
    [InlineData("#", MqttTopicProblem.None)]
    [InlineData("+", MqttTopicProblem.None)]
    [InlineData("a/#", MqttTopicProblem.None)]
    [InlineData("a/+/c", MqttTopicProblem.None)]
    [InlineData("+/+", MqttTopicProblem.None)]
    [InlineData("/", MqttTopicProblem.None)]
    [InlineData("$SYS/#", MqttTopicProblem.None)]
    [InlineData("rumah/bilik tidur/suhu", MqttTopicProblem.None)]
    [InlineData("", MqttTopicProblem.Empty)]
    [InlineData(null, MqttTopicProblem.Empty)]
    [InlineData("a/b#", MqttTopicProblem.BadMultiLevelWildcard)]
    [InlineData("a/#/b", MqttTopicProblem.BadMultiLevelWildcard)]
    [InlineData("##", MqttTopicProblem.BadMultiLevelWildcard)]
    [InlineData("a/b+", MqttTopicProblem.BadSingleLevelWildcard)]
    [InlineData("+a", MqttTopicProblem.BadSingleLevelWildcard)]
    [InlineData("a/++", MqttTopicProblem.BadSingleLevelWildcard)]
    [InlineData("a\u0000b", MqttTopicProblem.InvalidCharacter)]
    [InlineData("a\nb", MqttTopicProblem.InvalidCharacter)]
    public void ValidateFilter_FollowsTheSpecification(string? filter, MqttTopicProblem expected)
    {
        Assert.Equal(expected, MqttTopic.ValidateFilter(filter));
    }

    [Fact]
    public void ValidateFilter_RefusesLoneSurrogates()
    {
        // Theory data cannot carry lone surrogates, so this is a fact.
        Assert.Equal(MqttTopicProblem.InvalidCharacter, MqttTopic.ValidateFilter("a\uD800b"));
        Assert.Equal(MqttTopicProblem.InvalidCharacter, MqttTopic.ValidateFilter("a\uDC00"));
        Assert.Equal(MqttTopicProblem.None, MqttTopic.ValidateFilter("rumah/\uD83C\uDFE0"));
    }

    [Fact]
    public void JsonStringPayloadWithBrokenEscape_DoesNotThrow()
    {
        var read = new MqttMapping(CpuTopic).Read("\"\\uD800\"");

        var view = Template(new ValueTemplateSpec(ValueTemplateKind.Text)).Render(read);

        Assert.Equal("\uFFFD", view.Text);
    }

    [Theory]
    [InlineData("homeassistant/sensor/nas/state", MqttTopicProblem.None)]
    [InlineData("a/+", MqttTopicProblem.WildcardInName)]
    [InlineData("a/#", MqttTopicProblem.WildcardInName)]
    [InlineData("", MqttTopicProblem.Empty)]
    public void ValidateName_RefusesWildcards(string topic, MqttTopicProblem expected)
    {
        Assert.Equal(expected, MqttTopic.ValidateName(topic));
    }

    [Fact]
    public void Validate_RefusesTopicsOverTheLimit()
    {
        Assert.Equal(MqttTopicProblem.TooLong, MqttTopic.ValidateFilter(new string('a', MqttTopic.MaxBytes + 1)));
        Assert.Equal(MqttTopicProblem.None, MqttTopic.ValidateFilter(new string('a', MqttTopic.MaxBytes)));
        Assert.Equal(MqttTopicProblem.TooLong, MqttTopic.ValidateFilter(new string('é', MqttTopic.MaxBytes / 2 + 1)));
    }

    [Theory]
    [InlineData("sport/tennis/player1/#", "sport/tennis/player1", true)]
    [InlineData("sport/tennis/player1/#", "sport/tennis/player1/ranking", true)]
    [InlineData("sport/tennis/player1/#", "sport/tennis/player1/score/wimbledon", true)]
    [InlineData("sport/#", "sport", true)]
    [InlineData("#", "sport/tennis", true)]
    [InlineData("sport/tennis/+", "sport/tennis/player1", true)]
    [InlineData("sport/tennis/+", "sport/tennis/player1/ranking", false)]
    [InlineData("sport/+", "sport", false)]
    [InlineData("sport/+", "sport/", true)]
    [InlineData("+/+", "/finance", true)]
    [InlineData("/+", "/finance", true)]
    [InlineData("+", "/finance", false)]
    [InlineData("#", "$SYS/broker/uptime", false)]
    [InlineData("+/monitor/Clients", "$SYS/monitor/Clients", false)]
    [InlineData("$SYS/#", "$SYS/broker/uptime", true)]
    [InlineData("$SYS/monitor/+", "$SYS/monitor/Clients", true)]
    [InlineData("homeassistant/sensor/nas/state", "homeassistant/sensor/nas/state", true)]
    [InlineData("homeassistant/sensor/nas/state", "HomeAssistant/sensor/nas/state", false)]
    [InlineData("homeassistant/sensor/nas/state", "homeassistant/sensor/nas/state/", false)]
    [InlineData("a/b#", "a/b#", false)]
    [InlineData("a/+", "a/+", false)]
    [InlineData("", "a", false)]
    public void Matches_FollowsTheSpecification(string filter, string topic, bool expected)
    {
        Assert.Equal(expected, MqttTopic.Matches(filter, topic));
    }
}
