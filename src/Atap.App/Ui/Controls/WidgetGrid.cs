using System.Windows;
using System.Windows.Controls;
using Atap.Core.Notch;
using Atap.Core.Widgets;

namespace Atap.Ui.Controls;

/// <summary>
/// The 4-column grid of the Expanded and Panel surfaces, as an items panel. Each child sets
/// <c>controls:WidgetGrid.Size</c>: Small takes 1 column, Medium 2 and Large 4. <see cref="GridPacker.Flow"/>
/// places them by their heights, so a tall card sits beside short ones, cards grow into free space beside and
/// under them, and the edges line up with no empty area. The grid is only as wide as the columns it uses, so one
/// medium widget gives a 2-column notch.
/// </summary>
public class WidgetGrid : Panel
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
        "Size", typeof(WidgetSize), typeof(WidgetGrid),
        new FrameworkPropertyMetadata(WidgetSize.Small, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(
        nameof(ColumnWidth), typeof(double), typeof(WidgetGrid),
        new FrameworkPropertyMetadata(156.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(WidgetGrid),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private GridFlow _flow = GridPacker.Flow([], [], 0);

    public static WidgetSize GetSize(UIElement element) => (WidgetSize)element.GetValue(SizeProperty);

    public static void SetSize(UIElement element, WidgetSize value) => element.SetValue(SizeProperty, value);

    /// <summary>One column, without the gap. Two columns and a gap make a Medium widget.</summary>
    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    /// <summary>Space between columns and between rows.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>The width of a widget that takes <paramref name="span"/> columns.</summary>
    public double WidthOf(int span) => span * ColumnWidth + (span - 1) * Gap;

    /// <summary>Where each child goes, in child order. Tests read it.</summary>
    internal GridFlow Flow => _flow;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var sizes = children.Select(GetSize).ToList();
        foreach (var (child, size) in children.Zip(sizes))
            child.Measure(new Size(WidthOf(GridPacker.SpanOf(size)), double.PositiveInfinity));

        _flow = GridPacker.Flow(sizes, children.Select(c => c.DesiredSize.Height).ToList(), Gap);

        // A card that grew sideways lays out again at its new width, so its text uses the room.
        for (var i = 0; i < children.Count; i++)
        {
            if (_flow.Boxes[i].Span != GridPacker.SpanOf(sizes[i]))
                children[i].Measure(new Size(WidthOf(_flow.Boxes[i].Span), double.PositiveInfinity));
        }

        return children.Count == 0 ? default : new Size(WidthOf(_flow.Columns), _flow.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        for (var i = 0; i < children.Count && i < _flow.Boxes.Count; i++)
        {
            var box = _flow.Boxes[i];
            children[i].Arrange(new Rect(box.Column * (ColumnWidth + Gap), box.Top, WidthOf(box.Span), box.Height));
        }

        return finalSize;
    }
}
