using Atap.Core.Widgets;

namespace Atap.Core.Notch;

/// <summary>Where one widget sits in the grid: its row, first column and how many columns it takes.</summary>
public readonly record struct GridCell(int Row, int Column, int Span);

/// <summary>The packed grid: one cell per widget in the order given, the rows used and the widest row in columns.</summary>
public sealed record GridPacking(IReadOnlyList<GridCell> Cells, int Rows, int Columns);

/// <summary>
/// Lays out the Expanded and Panel widgets on a 4-column grid. Small takes 1 column, Medium 2 and Large 4.
/// Widgets keep their order: each goes after the one before it, and starts a new row when the rest of the
/// row is too narrow. Gaps are never filled by a later widget, so the order on screen is the order in the layout.
/// </summary>
public static class GridPacker
{
    public const int Columns = 4;

    public static int SpanOf(WidgetSize size) => size switch
    {
        WidgetSize.Small => 1,
        WidgetSize.Medium => 2,
        _ => Columns,
    };

    public static GridPacking Pack(IEnumerable<WidgetSize> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);

        var cells = new List<GridCell>();
        var row = 0;
        var column = 0;
        var widest = 0;
        foreach (var size in sizes)
        {
            var span = SpanOf(size);
            if (column + span > Columns)
            {
                row++;
                column = 0;
            }
            cells.Add(new GridCell(row, column, span));
            column += span;
            widest = Math.Max(widest, column);
        }

        return new GridPacking(cells, cells.Count == 0 ? 0 : row + 1, widest);
    }
}
