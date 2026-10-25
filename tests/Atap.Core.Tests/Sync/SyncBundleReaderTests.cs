using System.Text;
using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncBundleReaderTests
{
    /// <summary>A bundle as PC A writes it after picking up one change from PC B.</summary>
    internal const string RecordedBundle = """
        {
          "format": 1,
          "deviceId": "1B4E28BA-2FA1-11D2-883F-0016D3CCA427",
          "units": {
            "settings:language.choice": { "stamp": "2026-10-25T08:00:00.000Z", "value": "ms" },
            "settings:malaysia.state": { "stamp": "2026-10-24T22:15:03.250Z", "value": "SGR" },
            "settings:rules": {
              "stamp": "2026-10-20T01:00:00.000Z",
              "value": [{ "id": "r1", "name": "Gaming", "enabled": true,
                          "when": [{ "kind": "foregroundApp", "apps": ["valorant.exe"] }],
                          "then": { "profile": "gaming", "slim": true, "quietAlerts": true } }]
            },
            "layout:enabledWidgets": {
              "stamp": "2026-10-25T07:59:00.000Z",
              "device": "9f8c3a52-7d1e-4c6b-a0e2-5b3f1d2c4e6a",
              "value": ["media", "clock", "json-url#nas"]
            },
            "layout:compactPriority": {
              "stamp": "2026-10-25T07:59:00.000Z",
              "value": ["focus-timer", "media", "battery", "next-meeting", "push", "waktu-solat", "next-class"]
            },
            "layout:widgetSettings:clock": { "stamp": "2026-10-23T12:00:00.000Z", "value": { "use24Hour": true } },
            "layout:widgetSettings:json-url#old": { "stamp": "2026-10-22T09:30:00.000Z", "deleted": true }
          }
        }
        """;

    [Fact]
    public void Read_RecordedBundle_LoadsEveryUnitWithItsVersion()
    {
        var result = SyncBundleReader.Read(RecordedBundle);

        Assert.Equal(SyncBundleReadOutcome.Loaded, result.Outcome);
        Assert.Empty(result.Skipped);
        var bundle = Assert.IsType<SyncBundle>(result.Bundle);
        Assert.Equal(DeviceA, bundle.DeviceId);
        Assert.Equal(7, bundle.Units.Count);

        var state = bundle.Units["settings:malaysia.state"];
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 15, 3, 250, TimeSpan.Zero), state.Version.Stamp);
        Assert.Equal(DeviceA, state.Version.Device);
        Assert.True(SameJson("\"SGR\"", state.Value));

        // A unit changed on another PC keeps that PC as its device.
        Assert.Equal(DeviceB, bundle.Units[SyncUnitNames.EnabledWidgets].Version.Device);
        Assert.True(SameJson("""["media","clock","json-url#nas"]""", bundle.Units[SyncUnitNames.EnabledWidgets].Value));

        var deleted = bundle.Units["layout:widgetSettings:json-url#old"];
        Assert.True(deleted.IsDeleted);
        Assert.Null(deleted.Value);
    }

    [Theory]
    [InlineData("1970-01-01T00:00:00.000Z")]
    [InlineData("1969-12-31T23:59:59.000Z")]
    public void Read_UnitAtOrBeforeTheInitialStamp_IsMalformed(string stamp)
    {
        var result = SyncBundleReader.Read($$"""
            { "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427",
              "units": { "settings:clock.style": { "stamp": "{{stamp}}", "value": "plain" } } }
            """);

        Assert.Equal(SyncBundleReadOutcome.Loaded, result.Outcome);
        Assert.Empty(result.Bundle!.Units);
        Assert.Contains(result.Skipped, s => s.UnitName == "settings:clock.style" && s.Reason == SyncSkipReason.MalformedUnit);
    }

    [Fact]
    public void Read_HigherFormat_IsIgnoredAndNeverReplaced()
    {
        var result = SyncBundleReader.Read("""
            { "format": 2, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} }
            """);

        Assert.Equal(SyncBundleReadOutcome.NewerFormat, result.Outcome);
        Assert.Null(result.Bundle);
        Assert.False(result.CanBeReplaced);
    }

    [Fact]
    public void Read_HigherFormatWithAnotherShape_IsStillNewerNotInvalid()
    {
        var result = SyncBundleReader.Read("""{ "format": 7, "devices": ["a", "b"], "units": [1, 2, 3] }""");

        Assert.Equal(SyncBundleReadOutcome.NewerFormat, result.Outcome);
    }

    [Fact]
    public void Read_OverOneMegabyte_IsTooLarge()
    {
        var text = BundleWithValue("\"" + new string('a', SyncLimits.MaxBundleBytes) + "\"");

        var result = SyncBundleReader.Read(text);

        Assert.Equal(SyncBundleReadOutcome.TooLarge, result.Outcome);
        Assert.Null(result.Bundle);
        Assert.False(result.CanBeReplaced);
    }

    [Fact]
    public void Read_ExactlyOneMegabyte_IsLoaded()
    {
        var text = BundleWithValue("\"x\"");
        text += new string(' ', SyncLimits.MaxBundleBytes - Encoding.UTF8.GetByteCount(text));

        Assert.Equal(SyncLimits.MaxBundleBytes, Encoding.UTF8.GetByteCount(text));
        Assert.Equal(SyncBundleReadOutcome.Loaded, SyncBundleReader.Read(text).Outcome);
        Assert.Equal(SyncBundleReadOutcome.TooLarge, SyncBundleReader.Read(text + " ").Outcome);
    }

    [Fact]
    public void Read_SizeIsCountedInUtf8Bytes()
    {
        // 600,000 chars, but 1.2 MB once encoded.
        var text = BundleWithValue("\"" + new string('é', 600_000) + "\"");

        Assert.True(text.Length < SyncLimits.MaxBundleBytes);
        Assert.Equal(SyncBundleReadOutcome.TooLarge, SyncBundleReader.Read(text).Outcome);
    }

    [Fact]
    public void Read_ThirtyTwoLevels_IsLoaded()
    {
        // Root, units and the unit entry are three levels, so 29 nested arrays make 32.
        var result = SyncBundleReader.Read(BundleWithValue(Nested(29)));

        Assert.Equal(SyncBundleReadOutcome.Loaded, result.Outcome);
    }

    [Fact]
    public void Read_ThirtyThreeLevels_IsTooDeep()
    {
        var result = SyncBundleReader.Read(BundleWithValue(Nested(30)));

        Assert.Equal(SyncBundleReadOutcome.TooDeep, result.Outcome);
        Assert.Null(result.Bundle);
        Assert.False(result.CanBeReplaced);
    }

    [Fact]
    public void Read_VeryDeepNesting_IsTooDeepWithoutThrowing()
    {
        var text = new string('[', 100_000) + new string(']', 100_000);

        Assert.Equal(SyncBundleReadOutcome.TooDeep, SyncBundleReader.Read(text).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} }""")]
    [InlineData("""{ "format": "1", "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} }""")]
    [InlineData("""{ "format": 0, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} }""")]
    [InlineData("""{ "format": 1.5, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} }""")]
    [InlineData("""{ "format": 1, "units": {} }""")]
    [InlineData("""{ "format": 1, "deviceId": "PC-1", "units": {} }""")]
    [InlineData("""{ "format": 1, "deviceId": 42, "units": {} }""")]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427" }""")]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": [] }""")]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} } trailing""")]
    [InlineData("""{ "format": 1, "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} }""")]
    [InlineData("""{ "format": 1, /* note */ "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} }""")]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {}, }""")]
    public void Read_BrokenBundle_IsInvalidAndMayBeReplaced(string? text)
    {
        var result = SyncBundleReader.Read(text);

        Assert.Equal(SyncBundleReadOutcome.Invalid, result.Outcome);
        Assert.Null(result.Bundle);
        Assert.True(result.CanBeReplaced);
    }

    [Fact]
    public void Read_DuplicateUnitName_IsInvalid()
    {
        // Otherwise the second copy could hide behind the first in a review of the file.
        var result = SyncBundleReader.Read("""
            { "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {
                "settings:clock.style": { "stamp": "2026-10-25T08:00:00.000Z", "value": "plain" },
                "settings:clock.style": { "stamp": "2026-10-25T09:00:00.000Z", "value": "bold" } } }
            """);

        Assert.Equal(SyncBundleReadOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Read_MalformedUnits_AreSkippedAndTheRestLoads()
    {
        var result = SyncBundleReader.Read("""
            { "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {
                "settings:clock.style": { "stamp": "2026-10-25T08:00:00.000Z", "value": "bold" },
                "settings:bad.stamp": { "stamp": "25/10/2026 08:00", "value": 1 },
                "settings:no.stamp": { "value": 1 },
                "settings:no.value": { "stamp": "2026-10-25T08:00:00.000Z" },
                "settings:bad.device": { "stamp": "2026-10-25T08:00:00.000Z", "device": "PC-2", "value": 1 },
                "settings:bad.deleted": { "stamp": "2026-10-25T08:00:00.000Z", "deleted": "yes" },
                "settings:not.object": "bold",
                "settings:has space.x": { "stamp": "2026-10-25T08:00:00.000Z", "value": 1 },
                "nocolon": { "stamp": "2026-10-25T08:00:00.000Z", "value": 1 },
                "Settings:upper.area": { "stamp": "2026-10-25T08:00:00.000Z", "value": 1 },
                "settings:ctl\u0007.x": { "stamp": "2026-10-25T08:00:00.000Z", "value": 1 } } }
            """);

        Assert.Equal(SyncBundleReadOutcome.Loaded, result.Outcome);
        Assert.Equal(["settings:clock.style"], result.Bundle!.Units.Keys);
        Assert.Equal(10, result.Skipped.Count);
        Assert.All(result.Skipped, skip => Assert.Equal(SyncSkipReason.MalformedUnit, skip.Reason));
        Assert.Contains(result.Skipped, skip => skip.UnitName == "settings:ctl?.x");
    }

    [Fact]
    public void Read_ValueOnDeletedUnit_IsIgnored()
    {
        var result = SyncBundleReader.Read("""
            { "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {
                "layout:widgetSettings:clock": { "stamp": "2026-10-25T08:00:00.000Z", "deleted": true, "value": { "use24Hour": false } },
                "layout:widgetSettings:media": { "stamp": "2026-10-25T08:00:00.000Z", "deleted": false, "value": {} } } }
            """);

        Assert.True(result.Bundle!.Units["layout:widgetSettings:clock"].IsDeleted);
        Assert.False(result.Bundle.Units["layout:widgetSettings:media"].IsDeleted);
    }

    [Theory]
    [InlineData("2026-10-25T16:30:00+08:00", "2026-10-25T08:30:00.000Z")]
    [InlineData("2026-10-25T08:30:00Z", "2026-10-25T08:30:00.000Z")]
    [InlineData("2026-10-25T08:30:00.1234567Z", "2026-10-25T08:30:00.123Z")]
    [InlineData("2026-10-25T08:30:00.5-01:00", "2026-10-25T09:30:00.500Z")]
    public void Read_Stamps_AreUtcToTheMillisecond(string stamp, string expected)
    {
        var result = SyncBundleReader.Read($$"""
            { "format": 1, "deviceId": "{{DeviceA}}", "units": { "settings:clock.style": { "stamp": "{{stamp}}", "value": "bold" } } }
            """);

        var unit = result.Bundle!.Units["settings:clock.style"];
        Assert.Equal(expected, SyncVersion.FormatStamp(unit.Version.Stamp));
        Assert.Equal(TimeSpan.Zero, unit.Version.Stamp.Offset);
    }

    [Theory]
    [InlineData("2026-10-25T08:30:00")]
    [InlineData("2026-10-25")]
    [InlineData("10/25/2026 08:30:00")]
    [InlineData("Sun, 25 Oct 2026 08:30:00 GMT")]
    [InlineData("1793176200")]
    public void Read_StampWithoutZoneOrInAnotherFormat_SkipsTheUnit(string stamp)
    {
        var result = SyncBundleReader.Read($$"""
            { "format": 1, "deviceId": "{{DeviceA}}", "units": { "settings:clock.style": { "stamp": "{{stamp}}", "value": "bold" } } }
            """);

        Assert.Empty(result.Bundle!.Units);
        Assert.Equal(SyncSkipReason.MalformedUnit, Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public void Read_ByteOrderMark_IsAccepted()
    {
        var result = SyncBundleReader.Read("\uFEFF" + RecordedBundle);

        Assert.Equal(SyncBundleReadOutcome.Loaded, result.Outcome);
    }

    [Fact]
    public void Read_Bytes_GiveTheSameResultAsText()
    {
        byte[] withMark = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(RecordedBundle)];

        var fromBytes = SyncBundleReader.Read(withMark);
        var fromText = SyncBundleReader.Read(RecordedBundle);

        Assert.Equal(SyncBundleReadOutcome.Loaded, fromBytes.Outcome);
        Assert.Equal(fromText.Bundle!.Units.Keys.Order(StringComparer.Ordinal), fromBytes.Bundle!.Units.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(fromText.Bundle.Units[SyncUnitNames.EnabledWidgets].Version, fromBytes.Bundle.Units[SyncUnitNames.EnabledWidgets].Version);
    }

    [Fact]
    public void Read_Bytes_OverOneMegabyteAreTooLarge_AndTheMarkDoesNotCount()
    {
        var text = BundleWithValue("\"x\"");
        text += new string(' ', SyncLimits.MaxBundleBytes - Encoding.UTF8.GetByteCount(text));
        byte[] withMark = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(text)];

        Assert.Equal(SyncLimits.MaxBundleFileBytes, withMark.Length);
        Assert.Equal(SyncBundleReadOutcome.Loaded, SyncBundleReader.Read(withMark).Outcome);
        Assert.Equal(SyncBundleReadOutcome.TooLarge, SyncBundleReader.Read(Encoding.UTF8.GetBytes(text + " ")).Outcome);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x20, 0x0A })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF })]
    [InlineData(new byte[] { 0x7B, 0xFF, 0x7D })]
    public void Read_BrokenBytes_AreInvalid(byte[] utf8)
    {
        Assert.Equal(SyncBundleReadOutcome.Invalid, SyncBundleReader.Read(utf8).Outcome);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("stamp")]
    [InlineData("device")]
    [InlineData("deviceId")]
    [InlineData("value")]
    public void Read_FileSavedAsAnsi_IsInvalidAndMayBeReplaced(string where)
    {
        var result = SyncBundleReader.Read(AnsiBundle(where));

        Assert.Equal(SyncBundleReadOutcome.Invalid, result.Outcome);
        Assert.Null(result.Bundle);
        Assert.True(result.CanBeReplaced);
    }

    [Fact]
    public void Read_FileSavedAsAnsi_LoadsOnceSavedAsUtf8()
    {
        var text = Encoding.Latin1.GetString(AnsiBundle("value"));

        var result = SyncBundleReader.Read(Encoding.UTF8.GetBytes(text));

        Assert.Equal(SyncBundleReadOutcome.Loaded, result.Outcome);
        Assert.True(SameJson("""{"title":"Café"}""", result.Bundle!.Units["layout:widgetSettings:json-url#nas"].Value));
    }

    [Theory]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": { "settings:clock.style\uD800": { "stamp": "2026-10-25T08:00:00.000Z", "value": "bold" } } }""")]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": { "settings:clock.style": { "stamp": "2026-10-25T08:00:00.000Z\uDC00", "value": "bold" } } }""")]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": { "settings:clock.style": { "stamp": "2026-10-25T08:00:00.000Z", "value": "bold\uD800" } } }""")]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": { "settings:clock.style": { "stamp": "2026-10-25T08:00:00.000Z", "value": { "x\uDC00": 1 } } } }""")]
    [InlineData("""{ "format": 1, "deviceId": "\uD800", "units": {} }""")]
    public void Read_EscapedLoneSurrogate_IsInvalid(string text)
    {
        Assert.Equal(SyncBundleReadOutcome.Invalid, SyncBundleReader.Read(text).Outcome);
        Assert.Equal(SyncBundleReadOutcome.Invalid, SyncBundleReader.Read(Encoding.UTF8.GetBytes(text)).Outcome);
    }

    [Fact]
    public void Read_EscapedText_IsStillLoaded()
    {
        var result = SyncBundleReader.Read(BundleWithValue("\"Caf\\u00e9 \\ud83d\\ude00\""));

        Assert.Equal(SyncBundleReadOutcome.Loaded, result.Outcome);
        Assert.Equal("Café 😀", result.Bundle!.Units["settings:clock.style"].Value!.Value.GetString());
    }

    /// <summary>
    /// A bundle saved as ANSI in Notepad, with "Café" at <paramref name="where"/>. The é becomes the
    /// single byte 0xE9, which is not UTF-8.
    /// </summary>
    internal static byte[] AnsiBundle(string where)
    {
        const string Cafe = "Café";
        var text = $$"""
            { "format": 1, "deviceId": "{{(where == "deviceId" ? Cafe : DeviceA)}}", "units": {
                "layout:widgetSettings:json-url#{{(where == "name" ? Cafe : "nas")}}": {
                    "stamp": "{{(where == "stamp" ? Cafe : "2026-10-25T08:00:00.000Z")}}",
                    "device": "{{(where == "device" ? Cafe : DeviceB)}}",
                    "value": { "title": "{{(where == "value" ? Cafe : "NAS")}}" } } } }
            """;
        var bytes = Encoding.Latin1.GetBytes(text);
        Assert.Contains((byte)0xE9, bytes);
        return bytes;
    }

    private static string BundleWithValue(string valueJson) =>
        """{"format":1,"deviceId":"1b4e28ba-2fa1-11d2-883f-0016d3cca427","units":{"settings:clock.style":{"stamp":"2026-10-25T08:00:00.000Z","value":"""
        + valueJson + "}}}";

    private static string Nested(int levels) => new string('[', levels) + new string(']', levels);
}
