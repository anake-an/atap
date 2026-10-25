using Atap.Core.Notch;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Notch;

/// <summary>The open notch and the panel flow cards by height, with no holes and lined-up edges.</summary>
public class GridFlowTests
{
    private const WidgetSize S = WidgetSize.Small;
    private const WidgetSize M = WidgetSize.Medium;
    private const WidgetSize L = WidgetSize.Large;
    private const double Gap = 8;

    [Fact]
    public void Nothing_FlowsToAnEmptyGrid()
    {
        var flow = GridPacker.Flow([], [], Gap);

        Assert.Empty(flow.Boxes);
        Assert.Equal(0, flow.Columns);
        Assert.Equal(0, flow.Height);
    }

    [Fact]
    public void ATallCard_SitsBesideTwoShortOnes_WithNoHole()
    {
        // As reported: waktu solat (wide), a tall focus timer, then alarms. The alarms go under waktu solat.
        var flow = GridPacker.Flow([M, S, M], [100, 220, 100], Gap);

        Assert.Equal(new GridBox(0, 2, 0, 100), flow.Boxes[0]);
        Assert.Equal(new GridBox(2, 1, 0, 220), flow.Boxes[1]);
        // It grows down to line up with the bottom of the tall card.
        Assert.Equal(new GridBox(0, 2, 108, 112), flow.Boxes[2]);
        Assert.Equal(3, flow.Columns);
        Assert.Equal(220, flow.Height);
    }

    [Fact]
    public void ACardAloneOnTheLastRow_GrowsAcrossIt()
    {
        // As reported: volume mixer (tall) and lyrics, then sound output. It goes under the shorter lyrics.
        var flow = GridPacker.Flow([M, M, M], [300, 100, 120], Gap);

        Assert.Equal(new GridBox(2, 2, 108, 192), flow.Boxes[2]);
        Assert.Equal(300, flow.Height);

        // With even heights the third card has a row to itself and takes all of it.
        var even = GridPacker.Flow([M, M, M], [100, 100, 100], Gap);
        Assert.Equal(new GridBox(0, 4, 108, 100), even.Boxes[2]);
    }

    [Fact]
    public void CardsInARow_LineUp()
    {
        var flow = GridPacker.Flow([S, S, M], [60, 90, 40], Gap);

        Assert.All(flow.Boxes, box => Assert.Equal(90, box.Bottom));
    }

    [Theory]
    [InlineData(new[] { S, S, S, M }, new double[] { 50, 70, 60, 80 })]
    [InlineData(new[] { M, L, S }, new double[] { 40, 30, 90 })]
    [InlineData(new[] { S, M, M, S, L, S }, new double[] { 30, 120, 45, 60, 20, 70 })]
    [InlineData(new[] { M, S, S, S, M }, new double[] { 200, 50, 50, 50, 50 })]
    public void EveryFlow_CoversTheWholeGrid_WithoutOverlaps(WidgetSize[] sizes, double[] heights)
    {
        var flow = GridPacker.Flow(sizes, heights, Gap);

        for (var i = 0; i < flow.Boxes.Count; i++)
        {
            var box = flow.Boxes[i];
            Assert.True(box.Height >= heights[i], "A card never gets shorter than it wants.");
            Assert.True(box.Column >= 0 && box.Column + box.Span <= flow.Columns);
            for (var j = i + 1; j < flow.Boxes.Count; j++)
            {
                var other = flow.Boxes[j];
                var sideBySide = box.Column + box.Span <= other.Column || other.Column + other.Span <= box.Column;
                var aboveOrBelow = box.Bottom + Gap <= other.Top + 0.01 || other.Bottom + Gap <= box.Top + 0.01;
                Assert.True(sideBySide || aboveOrBelow, $"Cards {i} and {j} overlap.");
            }
        }

        // Every column is covered from the top to the bottom, apart from the gaps between cards.
        for (var column = 0; column < flow.Columns; column++)
        {
            var stack = flow.Boxes.Where(b => column >= b.Column && column < b.Column + b.Span).OrderBy(b => b.Top).ToList();
            Assert.NotEmpty(stack);
            Assert.Equal(0, stack[0].Top);
            Assert.Equal(flow.Height, stack[^1].Bottom, 2);
            for (var k = 1; k < stack.Count; k++)
                Assert.Equal(stack[k - 1].Bottom + Gap, stack[k].Top, 2);
        }
    }

    [Fact]
    public void Flow_NeedsAHeightForEveryWidget()
    {
        Assert.Throws<ArgumentException>(() => GridPacker.Flow([S, M], [10], Gap));
    }
}
