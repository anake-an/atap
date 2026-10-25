using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncBundleWriterTests
{
    [Fact]
    public void Write_ThenRead_RoundTripsTheRecordedBundle()
    {
        var original = SyncBundleReader.Read(SyncBundleReaderTests.RecordedBundle).Bundle!;

        var reread = SyncBundleReader.Read(SyncBundleWriter.Write(original));

        Assert.Equal(SyncBundleReadOutcome.Loaded, reread.Outcome);
        var bundle = reread.Bundle!;
        Assert.Equal(original.DeviceId, bundle.DeviceId);
        Assert.Equal(original.Units.Keys.Order(StringComparer.Ordinal), bundle.Units.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, unit) in original.Units)
        {
            Assert.Equal(unit.Version, bundle.Units[name].Version);
            Assert.Equal(unit.IsDeleted, bundle.Units[name].IsDeleted);
            if (unit.Value is { } value)
                Assert.True(SameJson(value.GetRawText(), bundle.Units[name].Value), name);
        }
    }

    [Fact]
    public void Write_ProducesCompactSortedText()
    {
        var bundle = Bundle(
            DeviceA,
            ("settings:malaysia.state", V(T0, DeviceA), "\"SGR\""),
            ("layout:widgetSettings:json-url#old", V(T0.AddMinutes(-5), DeviceB), null),
            ("layout:enabledWidgets", V(T0.AddMilliseconds(250), DeviceB), """["media","clock"]"""));

        var json = SyncBundleWriter.Write(bundle);

        const string expected = """
            {"format":1,"deviceId":"1b4e28ba-2fa1-11d2-883f-0016d3cca427","units":{
            "layout:enabledWidgets":{"stamp":"2026-10-25T08:00:00.250Z","device":"9f8c3a52-7d1e-4c6b-a0e2-5b3f1d2c4e6a","value":["media","clock"]},
            "layout:widgetSettings:json-url#old":{"stamp":"2026-10-25T07:55:00.000Z","device":"9f8c3a52-7d1e-4c6b-a0e2-5b3f1d2c4e6a","deleted":true},
            "settings:malaysia.state":{"stamp":"2026-10-25T08:00:00.000Z","value":"SGR"}}}
            """;
        Assert.Equal(expected.ReplaceLineEndings(string.Empty), json);
    }

    [Fact]
    public void Write_SameUnitsInAnyOrder_GiveTheSameText()
    {
        var first = Bundle(DeviceA, ("settings:a.b", V(T0, DeviceA), "1"), ("settings:c.d", V(T0, DeviceA), "2"));
        var second = Bundle(DeviceA, ("settings:c.d", V(T0, DeviceA), "2"), ("settings:a.b", V(T0, DeviceA), "1"));

        Assert.Equal(SyncBundleWriter.Write(first), SyncBundleWriter.Write(second));
    }

    [Fact]
    public void TryWrite_OverTheLimit_WritesNothing()
    {
        var big = Bundle(DeviceA, ("settings:clock.style", V(T0, DeviceA), "\"" + new string('a', SyncLimits.MaxBundleBytes) + "\""));
        var small = Bundle(DeviceA, ("settings:clock.style", V(T0, DeviceA), "\"bold\""));

        Assert.False(SyncBundleWriter.TryWrite(big, out var nothing));
        Assert.Equal(string.Empty, nothing);
        Assert.True(SyncBundleWriter.TryWrite(small, out var json));
        Assert.Equal(SyncBundleWriter.Write(small), json);
    }
}
