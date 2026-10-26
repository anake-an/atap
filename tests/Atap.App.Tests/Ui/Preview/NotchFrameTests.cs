using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atap.Core.Placement;
using Atap.Tests.Infrastructure;
using Atap.Ui.Preview;

namespace Atap.Tests.Ui.Preview;

/// <summary>The notch in the pictures: its size around the content, and the outline it draws.</summary>
public sealed class NotchFrameTests
{
    private const double Room = NotchShape.ShoulderRoom;

    [Fact]
    public void ItIsTheContentAndPadding_PlusTheShouldersAndTheStrip() => Sta.Run(() =>
    {
        var frame = new NotchFrame
        {
            Padding = new Thickness(10, 10, 10, 14),
            EdgeThickness = 3,
            EdgeReach = 40,
            Child = new Border { Width = 100, Height = 40 },
        };

        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(new Size(120 + 2 * Room + 80, 3 + 64), frame.DesiredSize);
    });

    [Fact]
    public void ANarrowSpace_ShortensTheStrip_NotTheNotch() => Sta.Run(() =>
    {
        var child = new Border { Width = 100, Height = 40 };
        var frame = new NotchFrame { EdgeThickness = 0, EdgeReach = 40, Child = child };

        frame.Measure(new Size(100 + 2 * Room + 10, double.PositiveInfinity));
        frame.Arrange(new Rect(frame.DesiredSize));

        Assert.Equal(100 + 2 * Room + 10, frame.DesiredSize.Width);
        Assert.Equal(new Size(100, 40), child.RenderSize);
    });

    [Fact]
    public void ItKeepsTheSmallestNotchSize() => Sta.Run(() =>
    {
        var frame = new NotchFrame { NotchMinWidth = 300, NotchMinHeight = 72, EdgeThickness = 0, EdgeReach = 0 };

        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(new Size(300 + 2 * Room, 72), frame.DesiredSize);
    });

    [Fact]
    public void ItDrawsTheShouldersAndTheRoundedCorners_UnderTheStrip() => Sta.Run(() =>
    {
        // A 120 by 60 notch under a 4 pixel strip, with nothing past the shoulders.
        var frame = new NotchFrame
        {
            NotchMinWidth = 120,
            NotchMinHeight = 60,
            EdgeThickness = 4,
            EdgeReach = 0,
            Fill = Brushes.Black,
            EdgeBrush = Brushes.Black,
        };
        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        frame.Arrange(new Rect(frame.DesiredSize));
        var width = (int)Math.Ceiling(frame.DesiredSize.Width);
        var height = (int)Math.Ceiling(frame.DesiredSize.Height);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(frame);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        byte Alpha(double x, double y) => pixels[(((int)y * width) + (int)x) * 4 + 3];

        var left = Room;
        var right = Room + 120;
        // The strip runs across the top, the body is solid.
        Assert.Equal(255, Alpha(1, 1));
        Assert.Equal(255, Alpha(width - 2, 1));
        Assert.Equal(255, Alpha(left + 60, 34));
        // Each shoulder fills the corner where the notch meets the strip, and curves away below it.
        Assert.Equal(255, Alpha(left - 2, 5));
        Assert.Equal(255, Alpha(right + 1, 5));
        Assert.Equal(0, Alpha(left - 2, 24));
        Assert.Equal(0, Alpha(right + 1, 24));
        // The bottom corners are round.
        Assert.Equal(0, Alpha(left + 1, 4 + 59));
        Assert.Equal(0, Alpha(right - 2, 4 + 59));
        Assert.Equal(255, Alpha(left + 60, 4 + 58));
    });
}
