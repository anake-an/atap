using Atap.Core.Widgets;

namespace Atap.Core.Layout;

/// <summary>The layout used on first run and when layout.json is corrupt.</summary>
public static class DefaultLayout
{
    // Must match the type ids of the built-in widgets in the app.
    public const string MediaWidgetId = "media";
    public const string ClockWidgetId = "clock";
    public const string MissedAlertsWidgetId = "missed-alerts";

    /// <summary>
    /// Who wins the compact view on a new install. Ids that are not enabled are skipped,
    /// so later widgets slot in without a layout change.
    /// </summary>
    public static IReadOnlyList<string> CompactPriority { get; } =
        ["focus-timer", MediaWidgetId, "battery", "next-meeting", "push", "waktu-solat", "next-class"];

    public static LayoutDocument Create() => new()
    {
        EnabledWidgets = [MediaWidgetId, ClockWidgetId, MissedAlertsWidgetId],
        States = new StateLayouts
        {
            Idle = new StateLayout
            {
                Placements = [new WidgetPlacement { WidgetId = ClockWidgetId, Size = WidgetSize.Small }],
            },
            Compact = new StateLayout
            {
                Placements =
                [
                    new WidgetPlacement { WidgetId = MediaWidgetId, Size = WidgetSize.Small, Slot = PlacementSlot.Left },
                    new WidgetPlacement { WidgetId = MediaWidgetId, Size = WidgetSize.Small, Slot = PlacementSlot.Right },
                ],
            },
            Expanded = new StateLayout
            {
                Placements = [new WidgetPlacement { WidgetId = MediaWidgetId, Size = WidgetSize.Medium }],
            },
            // A click opens the panel, so it starts with something in it: music and the alerts missed while quiet.
            Panel = new PanelLayout
            {
                Pages =
                [
                    new PanelPage
                    {
                        Id = PanelLayout.MainPageId,
                        Name = PanelLayout.MainPageName,
                        Placements =
                        [
                            new WidgetPlacement { WidgetId = MediaWidgetId, Size = WidgetSize.Medium },
                            new WidgetPlacement { WidgetId = MissedAlertsWidgetId, Size = WidgetSize.Medium, Order = 1 },
                        ],
                    },
                ],
            },
        },
        CompactPriority = [.. CompactPriority],
    };
}
