namespace Atap.Ui.Preview;

/// <summary>Made-up content for a widget's preview. The text is in the "preview" strings, never real data.</summary>
/// <param name="IconKey">An "icon.*" geometry from Icons.xaml.</param>
/// <param name="Progress">A bar from 0 to 1 on the large size, or null for none.</param>
public sealed record WidgetSample(string IconKey, double? Progress = null)
{
    /// <summary>The main line, such as "9:41" or a song title.</summary>
    public static string ValueKey(string typeId) => $"preview.sample.{typeId}.value";

    /// <summary>The smaller line under it.</summary>
    public static string DetailKey(string typeId) => $"preview.sample.{typeId}.detail";

    /// <summary>One plain line about the widget for the gallery.</summary>
    public static string LineKey(string typeId) => $"preview.sample.{typeId}.line";
}

/// <summary>
/// Sample content for every built-in widget, keyed by type id. Previews show this, so they never read the
/// clipboard, the calendar, notes or what is playing. A type with no entry gets a plain card with its icon and name.
/// </summary>
public static class WidgetSamples
{
    private static readonly Dictionary<string, WidgetSample> Samples = new(StringComparer.Ordinal)
    {
        ["audio-output"] = new("icon.volume"),
        ["audio-mixer"] = new("icon.volume", 0.7),
        ["battery"] = new("icon.battery", 0.82),
        ["bluetooth"] = new("icon.bolt"),
        ["clock"] = new("icon.clock"),
        ["hijri"] = new("icon.calendar"),
        ["holidays"] = new("icon.calendar"),
        ["home-assistant"] = new("icon.home"),
        ["json-url"] = new("icon.globe"),
        ["notes"] = new("icon.note"),
        ["todo"] = new("icon.list", 0.4),
        ["lyrics"] = new("icon.music"),
        ["media"] = new("icon.music", 0.35),
        ["missed-alerts"] = new("icon.bell"),
        ["next-meeting"] = new("icon.calendar"),
        ["privacy-dot"] = new("icon.camera"),
        ["push"] = new("icon.download", 0.6),
        ["quick-toggles"] = new("icon.settings"),
        ["rain-alerts"] = new("icon.droplet"),
        ["script"] = new("icon.play"),
        ["clipboard"] = new("icon.clipboard"),
        ["file-shelf"] = new("icon.folder"),
        ["assignments"] = new("icon.clipboard"),
        ["next-class"] = new("icon.calendar"),
        ["system-monitor"] = new("icon.cpu", 0.24),
        ["alarm"] = new("icon.alarm"),
        ["focus-timer"] = new("icon.timer", 0.5),
        ["stopwatch"] = new("icon.clock"),
        ["water"] = new("icon.droplet", 0.5),
        ["world-clock"] = new("icon.globe"),
        ["waktu-solat"] = new("icon.moon"),
        ["weather"] = new("icon.cloud"),
        ["wifi"] = new("icon.wifi"),
    };

    public static IReadOnlyCollection<string> TypeIds => Samples.Keys;

    public static bool TryGet(string typeId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WidgetSample? sample) =>
        Samples.TryGetValue(typeId, out sample);
}
