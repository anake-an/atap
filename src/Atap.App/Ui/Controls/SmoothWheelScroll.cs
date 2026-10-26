using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Atap.Ui.Controls;

/// <summary>
/// Smooth, quick wheel scrolling for a page: <c>&lt;ScrollViewer controls:SmoothWheelScroll.IsEnabled="True"&gt;</c>.
/// Each wheel turn adds to a target and the page glides there, so fast turns travel far and never lag behind.
/// The wheel scrolls the page unless the pointer is over something inside that can still scroll that way,
/// such as an open drop-down list or a long text box. A closed drop-down never changes its choice on the wheel.
/// With <see cref="LessMotionProperty"/> set, or Windows animations off, the page jumps instead of gliding.
/// </summary>
internal static class SmoothWheelScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothWheelScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Set on a window, it reaches every scroll area inside.</summary>
    public static readonly DependencyProperty LessMotionProperty = DependencyProperty.RegisterAttached(
        "LessMotion", typeof(bool), typeof(SmoothWheelScroll), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    private static readonly DependencyProperty GlideProperty = DependencyProperty.RegisterAttached(
        "Glide", typeof(Glide), typeof(SmoothWheelScroll), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetLessMotion(DependencyObject element) => (bool)element.GetValue(LessMotionProperty);

    public static void SetLessMotion(DependencyObject element, bool value) => element.SetValue(LessMotionProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ScrollViewer viewer)
            return;
        if ((bool)e.NewValue)
        {
            var glide = new Glide(viewer);
            viewer.SetValue(GlideProperty, glide);
            viewer.PreviewMouseWheel += glide.OnPreviewMouseWheel;
            viewer.ScrollChanged += glide.OnScrollChanged;
            viewer.Unloaded += glide.OnUnloaded;
        }
        else if (viewer.GetValue(GlideProperty) is Glide glide)
        {
            glide.Stop();
            viewer.PreviewMouseWheel -= glide.OnPreviewMouseWheel;
            viewer.ScrollChanged -= glide.OnScrollChanged;
            viewer.Unloaded -= glide.OnUnloaded;
            viewer.ClearValue(GlideProperty);
        }
    }

    /// <summary>
    /// True when the wheel belongs to something under the pointer instead of the page: a scroll area inside
    /// that can still move that way, an open drop-down, or a pop-up outside the page.
    /// </summary>
    private static bool InnerTakesWheel(ScrollViewer page, DependencyObject? source, int delta)
    {
        var node = source;
        while (node is not null && !ReferenceEquals(node, page))
        {
            if (node is ComboBox { IsDropDownOpen: true })
                return true;
            if (node is ScrollViewer inner && WheelScrollMath.CanMove(inner.VerticalOffset, inner.ScrollableHeight, delta))
                return true;
            // The edge of a text box sits outside its scroll area, and the wheel there still scrolls the text.
            if (node is TextBoxBase box && box.Template?.FindName("PART_ContentHost", box) is ScrollViewer text
                && WheelScrollMath.CanMove(text.VerticalOffset, text.ScrollableHeight, delta))
                return true;
            node = node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        // Not inside the page at all, so it is in a pop-up such as an open drop-down list.
        return node is null;
    }

    /// <summary>The target and the glide toward it, for one scroll area.</summary>
    private sealed class Glide(ScrollViewer viewer)
    {
        private double _target;
        private double _from;
        private double _expected;
        private long _startedAt;
        private bool _gliding;
        private bool _jumpPending;

        public void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || InnerTakesWheel(viewer, e.OriginalSource as DependencyObject, e.Delta))
                return;
            // Handled even when the page cannot move, so nothing under the pointer changes its value instead.
            e.Handled = true;
            if (viewer.ScrollableHeight <= 0)
                return;

            // Turns that come before the page caught up add to the same target.
            var from = _gliding || _jumpPending ? _target : viewer.VerticalOffset;
            _target = WheelScrollMath.AddDelta(from, e.Delta, viewer.ScrollableHeight);

            if (GetLessMotion(viewer) || !SystemParameters.ClientAreaAnimation)
            {
                Stop();
                _jumpPending = true;
                viewer.ScrollToVerticalOffset(_target);
                return;
            }

            _from = _gliding ? _expected : viewer.VerticalOffset;
            _expected = _from;
            _startedAt = Stopwatch.GetTimestamp();
            if (!_gliding)
            {
                _gliding = true;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        public void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, viewer))
                _jumpPending = false;
        }

        public void OnUnloaded(object sender, RoutedEventArgs e) => Stop();

        public void Stop()
        {
            if (!_gliding)
                return;
            _gliding = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            // The scroll bar, the keyboard or a new page moved it, so that wins over the glide.
            if (Math.Abs(viewer.VerticalOffset - _expected) > 1)
            {
                Stop();
                return;
            }
            var elapsed = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;
            _expected = WheelScrollMath.Ease(_from, _target, elapsed);
            viewer.ScrollToVerticalOffset(_expected);
            if (elapsed >= WheelScrollMath.GlideMilliseconds)
                Stop();
        }
    }
}

/// <summary>The sums behind <see cref="SmoothWheelScroll"/>, kept apart so they can be tested without a window.</summary>
internal static class WheelScrollMath
{
    /// <summary>Pixels per wheel unit. One mouse notch is 120 units.</summary>
    public const double PixelsPerDelta = 1;

    /// <summary>How long the page takes to reach a new target.</summary>
    public const double GlideMilliseconds = 150;

    /// <summary>The new target after a wheel turn, inside 0 to <paramref name="max"/>. A positive delta moves up.</summary>
    public static double AddDelta(double target, int delta, double max) =>
        Math.Clamp(target - delta * PixelsPerDelta, 0, Math.Max(0, max));

    /// <summary>Where the glide is after <paramref name="elapsedMs"/>. Fast at first, then slowing into the target.</summary>
    public static double Ease(double from, double target, double elapsedMs)
    {
        var t = Math.Clamp(elapsedMs / GlideMilliseconds, 0, 1);
        var left = 1 - t;
        return from + (target - from) * (1 - left * left * left);
    }

    /// <summary>True when a scroll area at <paramref name="offset"/> can still move the way the wheel turned.</summary>
    public static bool CanMove(double offset, double max, int delta) =>
        delta > 0 ? offset > 0.5 : delta < 0 && offset < max - 0.5;
}
