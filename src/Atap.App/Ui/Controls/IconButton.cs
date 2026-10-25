using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Atap.Ui.Controls;

/// <summary>
/// A round button with one stroke icon, for widget actions such as play or refresh:
/// <c>&lt;controls:IconButton Icon="{DynamicResource icon.next}" Label="{loc:T media.next}" Command="{Binding NextCommand}" /&gt;</c>.
/// <see cref="Label"/> is its screen reader name and tooltip. It takes keyboard focus with a visible ring.
/// On the notch this never activates the window: WPF does not move Windows focus to a no-activate window.
/// </summary>
public class IconButton : Button
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(IconButton), new PropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(IconButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(IconButton), new PropertyMetadata(StrokeIcon.DefaultSize));

    static IconButton()
    {
        // The style comes from Controls.xaml only, never from the Windows button theme.
        DefaultStyleKeyProperty.OverrideMetadata(typeof(IconButton), new FrameworkPropertyMetadata(typeof(IconButton)));
    }

    /// <summary>An "icon.&lt;name&gt;" geometry.</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>What the button does, in a few words. The screen reader name and the tooltip.</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new IconButtonAutomationPeer(this);

    private sealed class IconButtonAutomationPeer(IconButton owner) : ButtonAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(IconButton);

        protected override string GetNameCore()
        {
            var name = AutomationProperties.GetName(Owner);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            var label = ((IconButton)Owner).Label;
            return string.IsNullOrWhiteSpace(label) ? base.GetNameCore() : label;
        }
    }
}
