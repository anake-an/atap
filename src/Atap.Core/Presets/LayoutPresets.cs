using Atap.Core.Layout;
using Atap.Core.Layout.Editing;
using Atap.Core.Widgets;

namespace Atap.Core.Presets;

/// <summary>A starting layout picked during setup.</summary>
public enum LayoutPreset
{
    /// <summary>The clock and what is playing. Nothing goes online.</summary>
    Minimal,

    /// <summary>Focus timer, stopwatch, alarms and water.</summary>
    Student,

    /// <summary>Focus timer, world clock, alarms and missed alerts.</summary>
    Work,

    /// <summary>Waktu solat, Hijri date, weather, rain alerts and public holidays.</summary>
    Malaysia,
}

/// <summary>One widget a preset places.</summary>
public readonly record struct PresetPlacement(string TypeId, EditSpot Spot, WidgetSize Size);

/// <summary>
/// The presets, pure. A preset is applied through <see cref="LayoutEditSession"/>, so it follows the same rules as
/// the editor: compact tiles and priority for widgets that can be live, and nothing a widget does not support.
/// Where the notch sits and every widget's settings are kept.
/// </summary>
public static class LayoutPresets
{
    private static readonly EditSpot Idle = new(NotchState.Idle);
    private static readonly EditSpot Open = new(NotchState.Expanded);
    private static readonly EditSpot Panel = new(NotchState.Panel);

    public static IReadOnlyList<LayoutPreset> All { get; } = Enum.GetValues<LayoutPreset>();

    /// <summary>True when the preset has a widget that fetches from the internet.</summary>
    public static bool UsesInternet(LayoutPreset preset) => preset == LayoutPreset.Malaysia;

    /// <summary>
    /// The panel is the bigger hover view: it starts with the hover widgets, then has more. The missed alerts come
    /// last and show only while something was missed. Minimal puts nothing else there, so its panel follows the hover view.
    /// </summary>
    public static IReadOnlyList<PresetPlacement> PlacementsOf(LayoutPreset preset) => preset switch
    {
        LayoutPreset.Student =>
        [
            new("clock", Idle, WidgetSize.Small),
            new("media", Open, WidgetSize.Medium),
            new("focus-timer", Open, WidgetSize.Medium),
            new("media", Panel, WidgetSize.Medium),
            new("focus-timer", Panel, WidgetSize.Medium),
            new("stopwatch", Panel, WidgetSize.Small),
            new("alarm", Panel, WidgetSize.Small),
            new("water", Panel, WidgetSize.Small),
        ],
        LayoutPreset.Work =>
        [
            new("clock", Idle, WidgetSize.Small),
            new("media", Open, WidgetSize.Medium),
            new("focus-timer", Open, WidgetSize.Small),
            new("media", Panel, WidgetSize.Medium),
            new("focus-timer", Panel, WidgetSize.Medium),
            new("world-clock", Panel, WidgetSize.Medium),
            new("alarm", Panel, WidgetSize.Small),
            new("missed-alerts", Panel, WidgetSize.Medium),
        ],
        LayoutPreset.Malaysia =>
        [
            new("clock", Idle, WidgetSize.Small),
            new("waktu-solat", Open, WidgetSize.Medium),
            new("media", Open, WidgetSize.Medium),
            new("waktu-solat", Panel, WidgetSize.Medium),
            new("media", Panel, WidgetSize.Medium),
            new("weather", Panel, WidgetSize.Medium),
            new("hijri", Panel, WidgetSize.Small),
            new("holidays", Panel, WidgetSize.Small),
            new("rain-alerts", Panel, WidgetSize.Medium),
        ],
        _ =>
        [
            new("clock", Idle, WidgetSize.Small),
            new("media", Open, WidgetSize.Medium),
            new("missed-alerts", Panel, WidgetSize.Medium),
        ],
    };

    /// <summary>
    /// Replaces what the Default profile shows with the preset. Widgets this build does not have are skipped,
    /// so a preset never fails.
    /// </summary>
    public static void Apply(LayoutDocument document, LayoutPreset preset, WidgetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        var blank = new LayoutDocument
        {
            EnabledWidgets = [],
            States = new StateLayouts(),
            CompactPriority = [.. DefaultLayout.CompactPriority],
        };
        var session = new LayoutEditSession(blank, EffectiveLayout.DefaultProfileId, catalog);
        foreach (var placement in PlacementsOf(preset))
            session.Add(placement.TypeId, placement.Spot, placement.Size);
        session.ApplyTo(document);
    }
}
