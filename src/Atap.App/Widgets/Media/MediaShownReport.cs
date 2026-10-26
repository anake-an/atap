using System.Windows;

namespace Atap.Widgets.Media;

/// <summary>
/// Tells the music view model whether the middle of the small notch is on screen, so the widget ticks the progress
/// line and follows the lyric line only then. On a side edge that part is left out, and so is the work for it.
/// Set <c>media:MediaShownReport.IsOn="True"</c> on the element whose DataContext is the <see cref="MediaViewModel"/>.
/// </summary>
public static class MediaShownReport
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.RegisterAttached(
        "IsOn", typeof(bool), typeof(MediaShownReport), new PropertyMetadata(false, OnIsOnChanged));

    public static bool GetIsOn(DependencyObject element) => (bool)element.GetValue(IsOnProperty);

    public static void SetIsOn(DependencyObject element, bool value) => element.SetValue(IsOnProperty, value);

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        element.IsVisibleChanged -= OnVisibleChanged;
        element.DataContextChanged -= OnDataContextChanged;
        if (e.NewValue is not true)
            return;
        element.IsVisibleChanged += OnVisibleChanged;
        element.DataContextChanged += OnDataContextChanged;
        Report(element);
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Report((FrameworkElement)sender);

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // The view model it leaves is no longer shown by this element.
        if (e.OldValue is MediaViewModel old)
            old.IsCompactMiddleShown = false;
        Report((FrameworkElement)sender);
    }

    private static void Report(FrameworkElement element)
    {
        if (element.DataContext is MediaViewModel model)
            model.IsCompactMiddleShown = element.IsVisible;
    }
}
