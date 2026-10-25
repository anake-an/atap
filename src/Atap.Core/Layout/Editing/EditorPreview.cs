using Atap.Core.Notch;
using Atap.Core.Widgets;

namespace Atap.Core.Layout.Editing;

/// <summary>How a state lays out its widgets: one row in the small notch, the 4-column grid when it opens.</summary>
public enum PreviewShape
{
    Row,
    Grid,
}

/// <summary>Sizes of the notch preview in device-independent pixels, before it is scaled to fit.</summary>
/// <param name="ColumnWidth">One grid column. Two columns and a gap make a Medium widget.</param>
/// <param name="Gap">Space between columns and between rows.</param>
/// <param name="RowHeight">Every preview tile is this tall.</param>
public readonly record struct PreviewMetrics(double ColumnWidth, double Gap, double RowHeight)
{
    /// <summary>The same column and gap as the notch's own grid.</summary>
    public static PreviewMetrics Notch { get; } = new(156, 8, 72);

    /// <summary>The width of a widget that takes <paramref name="span"/> columns.</summary>
    public double WidthOf(int span) => span * ColumnWidth + (span - 1) * Gap;

    public double WidthOf(WidgetSize size) => WidthOf(GridPacker.SpanOf(size));
}

/// <summary>A tile's rectangle in the preview.</summary>
public readonly record struct PreviewRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CenterX => Left + Width / 2;
}

/// <summary>Pure maths of the editor's notch preview: where each tile is, and where a dropped tile goes.</summary>
public static class PreviewLayout
{
    public static PreviewShape ShapeOf(NotchState state) =>
        state is NotchState.Expanded or NotchState.Panel ? PreviewShape.Grid : PreviewShape.Row;

    /// <summary>
    /// One rectangle per tile, in order. The grid packs like the notch does (<see cref="GridPacker"/>), the row
    /// puts every tile side by side.
    /// </summary>
    public static IReadOnlyList<PreviewRect> Cells(PreviewShape shape, IReadOnlyList<WidgetSize> sizes, PreviewMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var cells = new List<PreviewRect>(sizes.Count);
        if (shape == PreviewShape.Row)
        {
            var left = 0.0;
            foreach (var size in sizes)
            {
                var width = metrics.WidthOf(size);
                cells.Add(new PreviewRect(left, 0, width, metrics.RowHeight));
                left += width + metrics.Gap;
            }
            return cells;
        }

        var packing = GridPacker.Pack(sizes);
        foreach (var cell in packing.Cells)
        {
            cells.Add(new PreviewRect(
                cell.Column * (metrics.ColumnWidth + metrics.Gap),
                cell.Row * (metrics.RowHeight + metrics.Gap),
                metrics.WidthOf(cell.Span),
                metrics.RowHeight));
        }
        return cells;
    }

    /// <summary>
    /// Where a tile dropped at (<paramref name="x"/>, <paramref name="y"/>) goes: before the nearest tile when the
    /// point is on its left half, after it when on its right half. 0 when there are no tiles.
    /// </summary>
    public static int DropIndex(IReadOnlyList<PreviewRect> cells, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Count == 0)
            return 0;

        var nearest = 0;
        var best = double.MaxValue;
        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            // The row counts first, so a point beside a short row never jumps to a wider row below it.
            var dy = Math.Max(0, Math.Max(c.Top - y, y - c.Bottom));
            var dx = Math.Max(0, Math.Max(c.Left - x, x - c.Right));
            var distance = dy * 10_000 + dx;
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }
        return x < cells[nearest].CenterX ? nearest : nearest + 1;
    }
}

/// <summary>What is being dragged in the editor: a widget from the gallery, or a tile already on the notch.</summary>
public sealed record EditorDrag
{
    private EditorDrag(string? typeId, EditSpot from, int index)
    {
        NewTypeId = typeId;
        From = from;
        FromIndex = index;
    }

    /// <summary>The type to add, for a drag from the gallery. Null for a placed tile.</summary>
    public string? NewTypeId { get; }

    public EditSpot From { get; }

    public int FromIndex { get; }

    public bool IsNew => NewTypeId is not null;

    public static EditorDrag FromGallery(string typeId) => new(typeId, default, -1);

    public static EditorDrag FromPreview(EditSpot spot, int index) => new(null, spot, index);
}

/// <summary>Turns a drop into one <see cref="LayoutEditSession"/> change, so Undo takes it back in one step.</summary>
public static class EditorDrop
{
    /// <summary>
    /// Where the tile ends up in <paramref name="to"/>. <paramref name="index"/> counts the tiles as they are before
    /// the drop, so a tile moved later in its own place lands one earlier once it has left its old place.
    /// </summary>
    public static int FinalIndex(EditorDrag drag, EditSpot to, int index)
    {
        ArgumentNullException.ThrowIfNull(drag);
        return !drag.IsNew && drag.From == to && index > drag.FromIndex ? index - 1 : index;
    }

    /// <summary>
    /// Applies a drop. <paramref name="to"/> is null when the tile was dropped outside the notch: a new widget is
    /// then left out, and a placed tile is removed.
    /// </summary>
    public static EditResult Apply(LayoutEditSession session, EditorDrag drag, EditSpot? to, int index)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(drag);
        if (to is not { } spot)
            return drag.IsNew ? EditResult.Unchanged : session.Remove(drag.From, drag.FromIndex);
        if (drag.NewTypeId is { } typeId)
            return session.Add(typeId, spot, null, Math.Max(0, index));
        return session.MoveTo(drag.From, drag.FromIndex, spot, Math.Max(0, FinalIndex(drag, spot, index)));
    }
}
