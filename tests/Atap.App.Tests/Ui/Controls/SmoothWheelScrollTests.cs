using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

/// <summary>The wheel sums in WheelScrollMath and the page scrolling in SmoothWheelScroll.</summary>
public sealed class SmoothWheelScrollTests
{
    [Fact]
    public void OneNotch_MovesAbout120Pixels()
    {
        Assert.Equal(120, WheelScrollMath.AddDelta(0, -120, 1000));
        Assert.Equal(380, WheelScrollMath.AddDelta(500, 120, 1000));
    }

    [Fact]
    public void QuickTurns_AddUpOnTheSameTarget()
    {
        var target = 0.0;
        for (var i = 0; i < 5; i++)
            target = WheelScrollMath.AddDelta(target, -120, 10_000);
        Assert.Equal(600, target);

        // A touchpad sends small steps, and they add up the same way.
        for (var i = 0; i < 10; i++)
            target = WheelScrollMath.AddDelta(target, 12, 10_000);
        Assert.Equal(480, target);
    }

    [Fact]
    public void TheTarget_StaysInsideThePage()
    {
        Assert.Equal(0, WheelScrollMath.AddDelta(50, 120, 1000));
        Assert.Equal(1000, WheelScrollMath.AddDelta(950, -120, 1000));
        Assert.Equal(0, WheelScrollMath.AddDelta(0, -120, 0));
        Assert.Equal(0, WheelScrollMath.AddDelta(0, -120, -5));
    }

    [Fact]
    public void TheGlide_StartsAtTheStart_AndEndsOnTheTarget()
    {
        Assert.Equal(100, WheelScrollMath.Ease(100, 220, 0));
        Assert.Equal(220, WheelScrollMath.Ease(100, 220, WheelScrollMath.GlideMilliseconds));
        Assert.Equal(220, WheelScrollMath.Ease(100, 220, WheelScrollMath.GlideMilliseconds * 3));

        // Most of the way in the first half, so it feels quick.
        var half = WheelScrollMath.Ease(0, 100, WheelScrollMath.GlideMilliseconds / 2);
        Assert.InRange(half, 80, 95);

        // Never past the target on the way.
        var last = 0.0;
        for (var ms = 0; ms <= WheelScrollMath.GlideMilliseconds; ms += 8)
        {
            var at = WheelScrollMath.Ease(0, 100, ms);
            Assert.InRange(at, last, 100);
            last = at;
        }
    }

    [Fact]
    public void AnInnerArea_KeepsTheWheel_OnlyWhileItCanMoveThatWay()
    {
        Assert.True(WheelScrollMath.CanMove(50, 100, 120));
        Assert.True(WheelScrollMath.CanMove(50, 100, -120));
        Assert.False(WheelScrollMath.CanMove(0, 100, 120));
        Assert.False(WheelScrollMath.CanMove(100, 100, -120));
        Assert.False(WheelScrollMath.CanMove(0, 0, -120));
        Assert.False(WheelScrollMath.CanMove(10, 100, 0));
    }

    [Fact]
    public void TheWheelOverAClosedDropDown_ScrollsThePage() => Sta.Run(() =>
    {
        var combo = new ComboBox { ItemsSource = new[] { "One", "Two", "Three" }, SelectedIndex = 0 };
        var content = new StackPanel();
        content.Children.Add(combo);
        content.Children.Add(new Border { Height = 1000 });
        var page = new ScrollViewer { Content = content, Width = 300, Height = 200 };
        SmoothWheelScroll.SetIsEnabled(page, true);
        SmoothWheelScroll.SetLessMotion(page, true);
        Layout(page);

        var wheel = Wheel(combo, -120);

        Assert.True(wheel.Handled);
        Layout(page);
        Assert.Equal(120, page.VerticalOffset);
        Assert.Equal(0, combo.SelectedIndex);
    });

    [Fact]
    public void TheWheelOverATallTextBox_ScrollsTheTextFirst() => Sta.Run(() =>
    {
        var box = new TextBox
        {
            Text = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"Line {i}")),
            AcceptsReturn = true,
            Height = 80,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var content = new StackPanel();
        content.Children.Add(box);
        content.Children.Add(new Border { Height = 1000 });
        var page = new ScrollViewer { Content = content, Width = 300, Height = 200 };
        SmoothWheelScroll.SetIsEnabled(page, true);
        SmoothWheelScroll.SetLessMotion(page, true);
        Layout(page);

        var inner = Assert.IsType<ScrollViewer>(FindScrollViewer(box));
        Assert.True(inner.ScrollableHeight > 0);

        // The text can move down, so the page leaves the wheel to it, over the text and over the box's edge.
        Assert.False(Wheel(Assert.IsAssignableFrom<UIElement>(inner.Content), -120).Handled);
        Assert.False(Wheel(box, -120).Handled);
        Assert.Equal(0, page.VerticalOffset);

        // At the top of the text, turning up goes to the page, which is at its top too.
        Assert.True(Wheel(box, 120).Handled);
    });

    private static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        target.RaiseEvent(args);
        return args;
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if ((child as ScrollViewer ?? FindScrollViewer(child)) is { } found)
                return found;
        }
        return null;
    }
}
