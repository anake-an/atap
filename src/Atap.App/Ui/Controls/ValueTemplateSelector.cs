using System.Windows;
using System.Windows.Controls;
using Atap.Core.Custom;

namespace Atap.Ui.Controls;

/// <summary>
/// Picks the view for a custom widget's <see cref="ValueView"/> by its kind, from the "value.&lt;Kind&gt;" templates
/// in Values.xaml: <c>&lt;ContentPresenter Content="{Binding Value}" ContentTemplateSelector="{DynamicResource value.selector}" /&gt;</c>.
/// </summary>
public sealed class ValueTemplateSelector : DataTemplateSelector
{
    public const string KeyPrefix = "value.";

    public static string KeyFor(ValueTemplateKind kind) => KeyPrefix + kind;

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ValueView view)
            return base.SelectTemplate(item, container);

        var key = KeyFor(view.Kind);
        return (container as FrameworkElement)?.TryFindResource(key) as DataTemplate
            ?? Application.Current?.TryFindResource(key) as DataTemplate;
    }
}
