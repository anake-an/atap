using Atap.Core.Widgets;

namespace Atap.Core.Layout;

/// <summary>
/// What the panel shows. Until the user puts a widget of their own on it, the panel is a bigger hover view: its
/// first page shows the hover widgets, a size up where they can, followed by what the page holds. On a new install
/// that is only the missed alerts, which show only while something was missed.
/// </summary>
public static class PanelContent
{
    /// <summary>
    /// True while no panel page holds a widget the user chose, so the panel follows the hover view.
    /// The missed alerts do not count: a new install puts them there.
    /// </summary>
    public static bool FollowsHover(IReadOnlyList<EffectivePage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        return pages.All(page => page.Placements.All(p => p.WidgetId == DefaultLayout.MissedAlertsWidgetId));
    }

    /// <summary>
    /// The panel pages to show, each in order, without the widgets that cannot show and without empty pages.
    /// </summary>
    /// <param name="layout">The active layout.</param>
    /// <param name="descriptorOf">The descriptor of a running instance, or null when the instance does not run.</param>
    /// <param name="hasContent">False for an instance with nothing worth a card right now.</param>
    public static IReadOnlyList<IReadOnlyList<WidgetPlacement>> Pages(
        EffectiveLayout layout,
        Func<string, IWidgetDescriptor?> descriptorOf,
        Func<string, bool> hasContent)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(descriptorOf);
        ArgumentNullException.ThrowIfNull(hasContent);

        var pages = layout.PanelPages.Select(page => page.Placements.OrderBy(p => p.Order).ToList()).ToList();
        if (pages.Count > 0 && FollowsHover(layout.PanelPages))
        {
            var hover = layout.Expanded
                .OrderBy(p => p.Order)
                .Where(p => descriptorOf(p.WidgetId) is { } descriptor && descriptor.SupportedStates.Contains(NotchState.Panel))
                .Select(p => Bigger(p, descriptorOf(p.WidgetId)!));
            pages[0] = hover.Concat(pages[0]).Select((p, i) => p with { Order = i }).ToList();
        }

        return pages
            .Select(page => (IReadOnlyList<WidgetPlacement>)page.Where(p => CanShow(p, descriptorOf, hasContent)).ToList())
            .Where(page => page.Count > 0)
            .ToList();
    }

    /// <summary>A small hover widget is a size up in the panel when it has that size, so the panel reads as the bigger view.</summary>
    public static WidgetPlacement Bigger(WidgetPlacement placement, IWidgetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(descriptor);
        return placement.Size == WidgetSize.Small && descriptor.SupportedSizes.Contains(WidgetSize.Medium)
            ? placement with { Size = WidgetSize.Medium }
            : placement;
    }

    private static bool CanShow(WidgetPlacement placement, Func<string, IWidgetDescriptor?> descriptorOf, Func<string, bool> hasContent) =>
        descriptorOf(placement.WidgetId) is { } descriptor
        && descriptor.SupportedStates.Contains(NotchState.Panel)
        && hasContent(placement.WidgetId);
}
