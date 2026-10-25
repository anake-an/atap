using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncStateFileTests
{
    [Fact]
    public void Write_ThenRead_RoundTrips()
    {
        var hash = SyncValues.Hash(Json("\"bold\""));
        var state = new SyncState(DeviceA, new Dictionary<string, SyncStateUnit>
        {
            ["settings:clock.style"] = new(V(T0, DeviceA), hash),
            [SyncUnitNames.EnabledWidgets] = new(V(T0.AddMilliseconds(-1), DeviceB), hash),
            ["layout:widgetSettings:json-url#nas"] = SyncStateUnit.Deleted(V(T0, DeviceB)),
        });

        var text = SyncStateFile.Write(state);
        var read = SyncStateFile.Read(text);

        Assert.NotNull(read);
        Assert.True(state.SameAs(read));
        Assert.DoesNotContain(DeviceA, text.Replace($"\"deviceId\": \"{DeviceA}\"", string.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("""{ "format": 2, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": {} }""")]
    [InlineData("""{ "format": 1, "deviceId": "PC-1", "units": {} }""")]
    [InlineData("""{ "format": 1, "deviceId": "1b4e28ba-2fa1-11d2-883f-0016d3cca427", "units": [] }""")]
    public void Read_BrokenOrNewerFile_GivesNull(string? text)
    {
        Assert.Null(SyncStateFile.Read(text));
    }

    [Fact]
    public void Read_BadEntries_AreDropped()
    {
        var read = SyncStateFile.Read($$"""
            { "format": 1, "deviceId": "{{DeviceA}}", "units": {
                "settings:clock.style": { "stamp": "2026-10-25T08:00:00.000Z", "hash": "{{new string('a', 64)}}" },
                "settings:short.hash": { "stamp": "2026-10-25T08:00:00.000Z", "hash": "abc" },
                "settings:no.stamp": { "hash": "{{new string('a', 64)}}" },
                "bad name": { "stamp": "2026-10-25T08:00:00.000Z", "deleted": true } } }
            """);

        Assert.Equal(["settings:clock.style"], read!.Units.Keys);
    }
}
