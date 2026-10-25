using System.Text.Json;
using Atap.Core.Sync;

namespace Atap.Core.Tests.Sync;

internal static class SyncTestData
{
    public const string DeviceA = "1b4e28ba-2fa1-11d2-883f-0016d3cca427";

    public const string DeviceB = "9f8c3a52-7d1e-4c6b-a0e2-5b3f1d2c4e6a";

    public static readonly Guid GuidA = Guid.Parse(DeviceA);

    public static readonly Guid GuidB = Guid.Parse(DeviceB);

    public static readonly DateTimeOffset T0 = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Synced keys as the setting definitions would register them.</summary>
    public static SyncScope Scope() => new(
        ["language.choice", "gestures.wheelVolume", "malaysia.state", "clock.style"],
        ["display.monitor"],
        [("file-shelf", "folder")]);

    public static JsonElement Json(string json) => JsonElement.Parse(json);

    public static Dictionary<string, JsonElement> Units(params (string Name, string Json)[] units) =>
        units.ToDictionary(unit => unit.Name, unit => Json(unit.Json), StringComparer.Ordinal);

    /// <summary>Units of a PC that has read both documents, plus <paramref name="extra"/>.</summary>
    public static Dictionary<string, JsonElement> Documents(params (string Name, string Json)[] extra)
    {
        var units = Units(
            (SyncUnitNames.Rules, "[]"),
            (SyncUnitNames.EnabledWidgets, """["media","clock"]"""));
        foreach (var (name, json) in extra)
            units[name] = Json(json);
        return units;
    }

    public static SyncVersion V(DateTimeOffset stamp, string device) => new(stamp, device);

    public static SyncBundle Bundle(string device, params (string Name, SyncVersion Version, string? Json)[] units) =>
        new(device, units.ToDictionary(
            unit => unit.Name,
            unit => new SyncBundleUnit(unit.Version, unit.Json is null ? null : Json(unit.Json)),
            StringComparer.Ordinal));

    public static string Raw(JsonElement? value) => value is { } element ? element.GetRawText() : "(deleted)";

    public static bool SameJson(string expected, JsonElement? actual) =>
        actual is { } element && JsonElement.DeepEquals(Json(expected), element);
}
