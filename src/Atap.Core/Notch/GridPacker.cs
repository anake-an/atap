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

    /// <summary>
    /// Flows widgets of known heights into the columns with no holes, as the open notch and the panel show them.
    /// Each widget, in order, goes where its columns are free highest up, the leftmost on a tie, so a tall card
    /// sits beside two short ones instead of pushing the next row down. Then the holes are closed: a card with free
    /// columns beside it for its whole height grows sideways into them, and every card grows down to the card
    /// under it, or to the bottom. The edges of neighbouring cards line up and no empty area is left.
    /// </summary>
    /// <param name="sizes">The widget sizes, in layout order.</param>
    /// <param name="heights">The height each widget wants at its width, in the same order.</param>
    /// <param name="gap">Space between cards, across and down.</param>
    public static GridFlow Flow(IReadOnlyList<WidgetSize> sizes, IReadOnlyList<double> heights, double gap)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(heights);
        if (sizes.Count != heights.Count)
            throw new ArgumentException("Every widget needs a height.", nameof(heights));
        if (sizes.Count == 0)
            return new GridFlow([], 0, 0);

        // Where the next card can start in each column.
        var next = new double[Columns];
        var boxes = new List<GridBox>(sizes.Count);
        for (var i = 0; i < sizes.Count; i++)
        {
            var span = SpanOf(sizes[i]);
            var height = Math.Max(0, heights[i]);
            var best = 0;
            var bestTop = double.MaxValue;
            for (var column = 0; column + span <= Columns; column++)
            {
                var top = Enumerable.Range(column, span).Max(c => next[c]);
                if (top < bestTop - Epsilon)
                {
                    best = column;
                    bestTop = top;
                }
            }
            boxes.Add(new GridBox(best, span, bestTop, height));
            for (var c = best; c < best + span; c++)
                next[c] = bestTop + height + gap;
        }

        var columns = boxes.Max(b => b.Column + b.Span);
        Widen(boxes, columns);
        var bottom = boxes.Max(b => b.Bottom);
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            var below = boxes
                .Where(other => other != box && Shares(box, other) && other.Top > box.Top + Epsilon)
                .Select(other => other.Top - gap)
                .DefaultIfEmpty(bottom)
                .Min();
            boxes[i] = box with { Height = Math.Max(box.Height, below - box.Top) };
        }

        return new GridFlow(boxes, columns, bottom);
    }

    private const double Epsilon = 0.01;

    // A card grows into a column beside it that nothing uses for the card's whole height.
    private static void Widen(List<GridBox> boxes, int columns)
    {
        for (var i = 0; i < boxes.Count; i++)
        {
            while (boxes[i].Column + boxes[i].Span < columns && IsFree(boxes, i, boxes[i].Column + boxes[i].Span))
                boxes[i] = boxes[i] with { Span = boxes[i].Span + 1 };
            while (boxes[i].Column > 0 && IsFree(boxes, i, boxes[i].Column - 1))
                boxes[i] = boxes[i] with { Column = boxes[i].Column - 1, Span = boxes[i].Span + 1 };
        }
    }

    private static bool IsFree(List<GridBox> boxes, int index, int column)
    {
        var box = boxes[index];
        return !boxes.Where((other, i) => i != index
            && column >= other.Column && column < other.Column + other.Span
            && other.Top < box.Bottom - Epsilon && box.Top < other.Bottom - Epsilon).Any();
    }

    private static bool Shares(GridBox a, GridBox b) => a.Column < b.Column + b.Span && b.Column < a.Column + a.Span;
}

/// <summary>Where one widget sits in the flowed grid: its first column, the columns it takes, and its top and height.</summary>
public readonly record struct GridBox(int Column, int Span, double Top, double Height)
{
    public double Bottom => Top + Height;
}

/// <summary>The flowed grid: one box per widget in the order given, the columns used and the total height.</summary>
public sealed record GridFlow(IReadOnlyList<GridBox> Boxes, int Columns, double Height);
