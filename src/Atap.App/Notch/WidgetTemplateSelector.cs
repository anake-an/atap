using System.Windows;
using System.Windows.Controls;

namespace Atap.Notch;

/// <summary>
/// Picks a widget view from resources using the placement's template keys,
/// so each widget can provide a view per state, slot and size.
/// </summary>
public sealed class WidgetTemplateSelector : DataTemplateSelector
{
    public const string MissingTemplateKey = "widget.missing";

    // The middle of the small notch stays empty for a widget without a view for it.
    private static readonly DataTemplate Nothing = CreateNothing();

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not PlacementViewModel placement || container is not FrameworkElement element)
            return base.SelectTemplate(item, container);

        foreach (var key in placement.TemplateKeys())
        {
            if (element.TryFindResource(key) is DataTemplate template)
                return template;
        }

        return placement.IsGap ? Nothing : element.TryFindResource(MissingTemplateKey) as DataTemplate;
    }

    private static DataTemplate CreateNothing()
    {
        var template = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        template.Seal();
        return template;
    }
}
