using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Atap.Core.Custom;

namespace Atap.Ui.Controls;

/// <summary>
/// A thin rounded bar, horizontal only: <c>&lt;controls:ProgressPill Value="{Binding Used}" Maximum="{Binding Size}" Accent="{DynamicResource SystemAccentBrush}" /&gt;</c>.
/// <see cref="Accent"/> fills it at the Normal level. Warning and Critical use the amber and rose tokens,
/// and Unknown is grey. Indeterminate shows a dim full bar, never a moving one.
/// Screen readers get the value as a range, the level as the item status, and "Progress" when it has no name.
/// </summary>
public class ProgressPill : ProgressBar
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(ValueLevel), typeof(ProgressPill), new PropertyMetadata(ValueLevel.Normal));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(ProgressPill), new PropertyMetadata(null));

    static ProgressPill()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ProgressPill), new FrameworkPropertyMetadata(typeof(ProgressPill)));
    }

    public ValueLevel Level
    {
        get => (ValueLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>The fill at the Normal level, usually the widget group's accent. The text colour when not set.</summary>
    public Brush? Accent
    {
        get => (Brush?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ProgressPillAutomationPeer(this);

    private sealed class ProgressPillAutomationPeer(ProgressPill owner) : ProgressBarAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(ProgressPill);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrWhiteSpace(name) ? KitText.Get(KitText.Progress) : name;
        }

        protected override string GetItemStatusCore()
        {
            var status = AutomationProperties.GetItemStatus(Owner);
            return string.IsNullOrWhiteSpace(status) ? KitText.Status(((ProgressPill)Owner).Level) ?? string.Empty : status;
        }
    }
}
