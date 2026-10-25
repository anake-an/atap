using Atap.Core.Notch;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Notch;

public class GridPackerTests
{
    private const WidgetSize S = WidgetSize.Small;
    private const WidgetSize M = WidgetSize.Medium;
    private const WidgetSize L = WidgetSize.Large;

    [Theory]
    [InlineData(S, 1)]
    [InlineData(M, 2)]
    [InlineData(L, 4)]
    public void SpanOf_GivesTheColumnsForEachSize(WidgetSize size, int span)
    {
        Assert.Equal(span, GridPacker.SpanOf(size));
    }

    [Fact]
    public void Nothing_PacksToAnEmptyGrid()
    {
        var packing = GridPacker.Pack([]);

        Assert.Empty(packing.Cells);
        Assert.Equal(0, packing.Rows);
        Assert.Equal(0, packing.Columns);
    }

    [Fact]
    public void FourSmall_FillOneRow()
    {
        var packing = GridPacker.Pack([S, S, S, S]);

        Assert.Equal([new(0, 0, 1), new(0, 1, 1), new(0, 2, 1), new(0, 3, 1)], packing.Cells);
        Assert.Equal(1, packing.Rows);
        Assert.Equal(4, packing.Columns);
    }

    [Fact]
    public void OneMedium_IsTwoColumnsWide()
    {
        var packing = GridPacker.Pack([M]);

        Assert.Equal([new GridCell(0, 0, 2)], packing.Cells);
        Assert.Equal(2, packing.Columns);
    }

    [Fact]
    public void AWidgetThatDoesNotFit_StartsANewRow()
    {
        var packing = GridPacker.Pack([S, S, S, M]);

        Assert.Equal(new GridCell(1, 0, 2), packing.Cells[3]);
        Assert.Equal(2, packing.Rows);
        Assert.Equal(3, packing.Columns);
    }

    [Fact]
    public void Large_TakesAWholeRow()
    {
        var packing = GridPacker.Pack([S, L, S]);

        Assert.Equal([new(0, 0, 1), new(1, 0, 4), new(2, 0, 1)], packing.Cells);
        Assert.Equal(3, packing.Rows);
        Assert.Equal(4, packing.Columns);
    }

    [Fact]
    public void ALaterSmallWidget_NeverFillsAnEarlierGap()
    {
        // The medium leaves one column free in row 0, but the small after it stays in order on row 1.
        var packing = GridPacker.Pack([S, M, M, S]);

        Assert.Equal([new(0, 0, 1), new(0, 1, 2), new(1, 0, 2), new(1, 2, 1)], packing.Cells);
    }

    [Fact]
    public void MediumPairs_ShareARow()
    {
        var packing = GridPacker.Pack([M, M, M]);

        Assert.Equal([new(0, 0, 2), new(0, 2, 2), new(1, 0, 2)], packing.Cells);
        Assert.Equal(2, packing.Rows);
    }
}
