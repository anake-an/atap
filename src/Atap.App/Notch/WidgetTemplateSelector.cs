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

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not PlacementViewModel placement || container is not FrameworkElement element)
            return base.SelectTemplate(item, container);

        foreach (var key in placement.TemplateKeys())
        {
            if (element.TryFindResource(key) is DataTemplate template)
                return template;
        }

        return element.TryFindResource(MissingTemplateKey) as DataTemplate;
    }
}
