using System.Windows;
using System.Windows.Controls;
using Atap.Core.Notch;
using Atap.Core.Widgets;

namespace Atap.Ui.Controls;

/// <summary>
/// The 4-column grid of the Expanded and Panel surfaces, as an items panel. Each child sets
/// <c>controls:WidgetGrid.Size</c>, and <see cref="GridPacker"/> places it: Small takes 1 column, Medium 2
/// and Large 4, in order. A row is as tall as its tallest widget, and the grid is only as wide as its widest
/// row, so one medium widget gives a 2-column notch.
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

    private GridPacking _packing = GridPacker.Pack([]);
    private double[] _rowHeights = [];

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

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        _packing = GridPacker.Pack(children.Select(GetSize));
        _rowHeights = new double[_packing.Rows];

        for (var i = 0; i < children.Count; i++)
        {
            var cell = _packing.Cells[i];
            children[i].Measure(new Size(WidthOf(cell.Span), double.PositiveInfinity));
            _rowHeights[cell.Row] = Math.Max(_rowHeights[cell.Row], children[i].DesiredSize.Height);
        }

        if (_packing.Rows == 0)
            return default;
        return new Size(WidthOf(_packing.Columns), _rowHeights.Sum() + (_packing.Rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tops = new double[_rowHeights.Length];
        for (var row = 1; row < tops.Length; row++)
            tops[row] = tops[row - 1] + _rowHeights[row - 1] + Gap;

        var children = InternalChildren;
        for (var i = 0; i < children.Count && i < _packing.Cells.Count; i++)
        {
            var cell = _packing.Cells[i];
            var left = cell.Column * (ColumnWidth + Gap);
            children[i].Arrange(new Rect(left, tops[cell.Row], WidthOf(cell.Span), _rowHeights[cell.Row]));
        }

        return finalSize;
    }
}
