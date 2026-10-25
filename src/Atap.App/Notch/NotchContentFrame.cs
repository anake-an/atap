using System.Windows;
using System.Windows.Controls;

namespace Atap.Notch;

/// <summary>
/// Holds the surface content at its own size, whatever the size of the pill around it. The pill springs and clips,
/// but the content is laid out once, so text does not re-wrap and widgets do not lay out again on every frame.
/// When the content's own size changes, such as a widget that loaded its data, it says so and the notch fits it.
/// </summary>
internal sealed class NotchContentFrame : Decorator
{
    private static readonly Size Unbounded = new(double.PositiveInfinity, double.PositiveInfinity);

    private Size _natural;

    /// <summary>Raised during layout when the content wants a different size. Handlers must not lay out again at once.</summary>
    public event EventHandler? NaturalSizeChanged;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
            return default;
        Child.Measure(Unbounded);
        var size = Child.DesiredSize;
        if (size != _natural)
        {
            _natural = size;
            NaturalSizeChanged?.Invoke(this, EventArgs.Empty);
        }
        return size;
    }

    // Centred at its own size. Whatever is outside the pill is clipped by the pill.
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is { } child)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect((arrangeSize.Width - size.Width) / 2, (arrangeSize.Height - size.Height) / 2, size.Width, size.Height));
        }
        return arrangeSize;
    }
}
