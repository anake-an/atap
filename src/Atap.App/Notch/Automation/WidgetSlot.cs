using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using Atap.Core.Localization;

namespace Atap.Notch.Automation;

/// <summary>
/// On while the panel has the keyboard. It flows down to everything in the notch, and buttons and widget slots
/// become focusable only then, so a click on the notch never takes focus from the app in front.
/// </summary>
public static class KeyboardMode
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.RegisterAttached(
        "IsOn", typeof(bool), typeof(KeyboardMode), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsOn(DependencyObject element) => (bool)element.GetValue(IsOnProperty);

    public static void SetIsOn(DependencyObject element, bool value) => element.SetValue(IsOnProperty, value);
}

/// <summary>What a screen reader says for a widget, such as "Now playing: Song by Artist". Widget view models implement it.</summary>
public interface IAccessibleSummary
{
    string AccessibleSummary { get; }
}

/// <summary>
/// The open notch's grid: like an ItemsControl, but each widget sits in a <see cref="WidgetSlot"/>, so the
/// keyboard can reach it in the panel and a screen reader can name it.
/// </summary>
public sealed class WidgetSlots : ItemsControl
{
    protected override DependencyObject GetContainerForItemOverride() => new WidgetSlot();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is WidgetSlot;
}

/// <summary>
/// One widget on the grid. In the panel Tab and the arrow keys move between slots, and Enter moves into the
/// slot's own buttons. A screen reader reads the widget's summary, or its name when it has none.
/// </summary>
public sealed class WidgetSlot : ContentControl
{
    static WidgetSlot()
    {
        // Focusable follows the keyboard mode, see KeyboardMode.
        FocusableProperty.OverrideMetadata(typeof(WidgetSlot), new FrameworkPropertyMetadata(false));
        KeyboardMode.IsOnProperty.OverrideMetadata(
            typeof(WidgetSlot),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, (d, e) => ((WidgetSlot)d).Focusable = (bool)e.NewValue));
    }

    /// <summary>The words a screen reader says for this slot.</summary>
    public string AccessibleName
    {
        get
        {
            var set = System.Windows.Automation.AutomationProperties.GetName(this);
            if (!string.IsNullOrWhiteSpace(set))
                return set;
            if (Content is not PlacementViewModel placement)
                return string.Empty;
            if (placement.ViewModel is IAccessibleSummary { AccessibleSummary: { Length: > 0 } summary })
                return summary;
            return placement.NameKey is { } key ? Localizer.Instance[key] : string.Empty;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsKeyboardFocused || e.Key is not (Key.Enter or Key.Space))
            return;
        // The first button inside comes right after the slot in the tab order.
        e.Handled = MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new WidgetSlotAutomationPeer(this);

    private sealed class WidgetSlotAutomationPeer(WidgetSlot owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(WidgetSlot);

        protected override string GetNameCore() => ((WidgetSlot)Owner).AccessibleName;

        protected override bool IsKeyboardFocusableCore() => true;
    }
}
