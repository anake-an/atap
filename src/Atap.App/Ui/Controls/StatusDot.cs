using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Atap.Core.Custom;

namespace Atap.Ui.Controls;

/// <summary>
/// A small dot for a level: <c>&lt;controls:StatusDot Level="{Binding Level}" /&gt;</c>. Normal is <see cref="Accent"/>
/// (teal unless set), Warning amber, Critical rose, and Unknown an empty grey ring. In high contrast the levels
/// get their own shapes as well, a circle, triangle, square and ring, so they never rely on colour alone.
/// Its screen reader name is the level, such as "Warning", unless AutomationProperties.Name is set.
/// </summary>
public class StatusDot : Control
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(ValueLevel), typeof(StatusDot), new PropertyMetadata(ValueLevel.Unknown));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(StatusDot), new PropertyMetadata(null));

    static StatusDot()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusDot), new FrameworkPropertyMetadata(typeof(StatusDot)));
    }

    public ValueLevel Level
    {
        get => (ValueLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>The colour at the Normal level.</summary>
    public Brush? Accent
    {
        get => (Brush?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StatusDotAutomationPeer(this);

    private sealed class StatusDotAutomationPeer(StatusDot owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(StatusDot);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrWhiteSpace(name) ? KitText.Level(((StatusDot)Owner).Level) : name;
        }

        protected override string GetItemStatusCore()
        {
            var status = AutomationProperties.GetItemStatus(Owner);
            return string.IsNullOrWhiteSpace(status) ? KitText.Level(((StatusDot)Owner).Level) : status;
        }
    }
}
