using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

/// <summary>The icon geometry in Ui/Controls/Icons.xaml and the StrokeIcon that draws it.</summary>
public partial class IconsTests
{
    private static readonly Uri IconsSource = new("/Atap;component/Ui/Controls/Icons.xaml", UriKind.Relative);

    [Fact]
    public void EveryIcon_IsAFrozenGeometryInsideTheGrid() => Sta.Run(() =>
    {
        var icons = (ResourceDictionary)Application.LoadComponent(IconsSource);
        Assert.True(icons.Count >= 40, $"Only {icons.Count} icons.");

        foreach (var key in icons.Keys)
        {
            var name = Assert.IsType<string>(key);
            Assert.Matches(KeyPattern(), name);
            var geometry = Assert.IsAssignableFrom<Geometry>(icons[key]);
            Assert.True(geometry.IsFrozen, $"{name} is not frozen.");
            var bounds = geometry.Bounds;
            Assert.False(bounds.IsEmpty, $"{name} is empty.");
            // Inside 1 to 23, so a stroke of 2 stays on the 24 grid.
            Assert.True(
                bounds.Left >= 1 && bounds.Top >= 1 && bounds.Right <= 23 && bounds.Bottom <= 23,
                $"{name} reaches {bounds}, outside 1 to 23.");
        }
    });

    [Fact]
    public void TheAppResources_HaveTheIcons() => Sta.Run(() =>
    {
        foreach (var key in new[] { "icon.play", "icon.pause", "icon.refresh", "icon.moon", "icon.warning" })
            Assert.IsAssignableFrom<Geometry>(Application.Current.TryFindResource(key));
    });

    [Fact]
    public void StrokeIcon_IsSixteenPixelsUnlessSized() => Sta.Run(() =>
    {
        var icon = new StrokeIcon { Data = KitHost.Icon("icon.play") };
        icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(16, 16), icon.DesiredSize);

        icon.Width = 24;
        icon.Height = 24;
        icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(24, 24), icon.DesiredSize);
    });

    [Fact]
    public void StrokeIcon_TakesTheForegroundAroundIt() => Sta.Run(() =>
    {
        var icon = new StrokeIcon();
        var parent = new Border { Child = icon };
        parent.SetValue(TextElement.ForegroundProperty, Brushes.Orange);

        Assert.Same(Brushes.Orange, icon.Foreground);
    });

    [Fact]
    public void StrokeIcon_DrawsTheLineScaledToItsSize() => Sta.Run(() =>
    {
        // A minus sign on a 48 pixel icon: a line across the middle, nothing at the top.
        var icon = new StrokeIcon { Data = KitHost.Icon("icon.minus"), Foreground = Brushes.White, Width = 48, Height = 48 };
        icon.Measure(new Size(48, 48));
        icon.Arrange(new Rect(0, 0, 48, 48));

        var bitmap = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(icon);
        var pixels = new byte[48 * 48 * 4];
        bitmap.CopyPixels(pixels, 48 * 4, 0);
        byte Alpha(int x, int y) => pixels[((y * 48) + x) * 4 + 3];

        Assert.Equal(255, Alpha(24, 24));
        // The stroke is 2 grid units, 4 pixels at this size.
        Assert.Equal(255, Alpha(24, 22));
        Assert.Equal(0, Alpha(24, 19));
        Assert.Equal(0, Alpha(24, 4));
        // The line runs from 5 to 19 on the grid, 10 to 38 pixels.
        Assert.Equal(0, Alpha(4, 24));
        Assert.Equal(255, Alpha(12, 24));
    });

    [Fact]
    public void StrokeIcon_IsDecorationUntilItHasAName() => Sta.Run(() =>
    {
        var icon = new StrokeIcon { Data = KitHost.Icon("icon.warning") };
        using var host = new KitHost(icon);
        var peer = KitHost.Peer(icon);

        Assert.Equal(AutomationControlType.Image, peer.GetAutomationControlType());
        Assert.False(peer.IsControlElement());

        System.Windows.Automation.AutomationProperties.SetName(icon, "Warning");
        Assert.True(peer.IsControlElement());
        Assert.Equal("Warning", peer.GetName());
    });

    [GeneratedRegex(@"^icon\.[a-z][A-Za-z]*$")]
    private static partial Regex KeyPattern();
}
