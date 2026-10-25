using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace Atap.Notch.Surfaces.Alerts;

/// <summary>
/// Tells a screen reader that a new alert is on the notch, through the element's live region. Set
/// <c>Announce="True"</c> on an element with AutomationProperties.LiveSetting. It speaks when the element
/// loads and when a new alert takes its place. Nothing happens while no screen reader listens.
/// </summary>
public static class AlertLiveRegion
{
    public static readonly DependencyProperty AnnounceProperty = DependencyProperty.RegisterAttached(
        "Announce",
        typeof(bool),
        typeof(AlertLiveRegion),
        new PropertyMetadata(false, OnAnnounceChanged));

    public static bool GetAnnounce(DependencyObject element) => (bool)element.GetValue(AnnounceProperty);

    public static void SetAnnounce(DependencyObject element, bool value) => element.SetValue(AnnounceProperty, value);

    private static void OnAnnounceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        element.Loaded -= OnLoaded;
        element.DataContextChanged -= OnDataContextChanged;
        if (e.NewValue is true)
        {
            element.Loaded += OnLoaded;
            element.DataContextChanged += OnDataContextChanged;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Announce((FrameworkElement)sender);

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement { IsLoaded: true } element && e.NewValue is not null)
            Announce(element);
    }

    // After the bindings have the new alert's text.
    private static void Announce(FrameworkElement element) =>
        element.Dispatcher.InvokeAsync(
            () =>
            {
                if (!element.IsLoaded || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                    return;
                var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            },
            DispatcherPriority.Background);
}
