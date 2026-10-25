using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Atap.Core.Custom;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

public class StatusDotTests
{
    [Theory]
    [InlineData(ValueLevel.Normal, "MediaAccentColor")]
    [InlineData(ValueLevel.Warning, "ProductivityAccentColor")]
    [InlineData(ValueLevel.Critical, "SystemAccentColor")]
    public void Level_SetsTheColour(ValueLevel level, string colour) => Sta.Run(() =>
    {
        var dot = new StatusDot { Level = level };
        using var host = new KitHost(dot);

        var ellipse = KitHost.Named<Ellipse>(dot, "Dot");
        Assert.Equal(KitHost.Token(colour), KitHost.ColorOf(ellipse.Fill));
        Assert.Equal(8, dot.ActualWidth);
    });

    [Fact]
    public void Unknown_IsAnEmptyGreyRing() => Sta.Run(() =>
    {
        var dot = new StatusDot();
        using var host = new KitHost(dot);

        var ellipse = KitHost.Named<Ellipse>(dot, "Dot");
        Assert.Equal(ValueLevel.Unknown, dot.Level);
        Assert.Equal(Colors.Transparent, KitHost.ColorOf(ellipse.Fill));
        Assert.Equal(KitHost.Token("TextMutedColor"), KitHost.ColorOf(ellipse.Stroke));
    });

    [Fact]
    public void Level_Binds() => Sta.Run(() =>
    {
        var source = new StatusDot { Level = ValueLevel.Normal };
        var dot = new StatusDot();
        dot.SetBinding(StatusDot.LevelProperty, new Binding(nameof(StatusDot.Level)) { Source = source });
        using var host = new KitHost(dot);
        var ellipse = KitHost.Named<Ellipse>(dot, "Dot");

        source.Level = ValueLevel.Critical;

        Assert.Equal(ValueLevel.Critical, dot.Level);
        Assert.Equal(KitHost.Token("SystemAccentColor"), KitHost.ColorOf(ellipse.Fill));
    });

    [Fact]
    public void ScreenReaderName_IsTheLevelUnlessSet() => Sta.Run(() =>
    {
        var dot = new StatusDot { Level = ValueLevel.Warning };
        using var host = new KitHost(dot);
        var peer = KitHost.Peer(dot);

        Assert.Equal("Warning", peer.GetName());
        Assert.Equal(AutomationControlType.Image, peer.GetAutomationControlType());

        AutomationProperties.SetName(dot, "Pi-hole");
        Assert.Equal("Pi-hole", peer.GetName());
        Assert.Equal("Warning", peer.GetItemStatus());
    });

    [Fact]
    public void HighContrast_GivesEveryLevelItsOwnShape() => Sta.Run(() =>
    {
        var dot = new StatusDot { Level = ValueLevel.Normal };
        using var host = new KitHost(dot);
        var ellipse = KitHost.Named<Ellipse>(dot, "Dot");
        var shape = KitHost.Named<System.Windows.Shapes.Path>(dot, "Shape");

        using (KitHost.HighContrast())
        {
            Assert.Equal(Visibility.Collapsed, ellipse.Visibility);
            Assert.Equal(Visibility.Visible, shape.Visibility);
            Assert.Equal(SystemColors.WindowTextColor, KitHost.ColorOf(shape.Fill));

            var shapes = new HashSet<string>();
            foreach (var level in new[] { ValueLevel.Normal, ValueLevel.Warning, ValueLevel.Critical })
            {
                dot.Level = level;
                shapes.Add(shape.Data.ToString());
            }
            Assert.Equal(3, shapes.Count);

            dot.Level = ValueLevel.Unknown;
            Assert.Equal(Colors.Transparent, KitHost.ColorOf(shape.Fill));
            Assert.Equal(SystemColors.WindowTextColor, KitHost.ColorOf(shape.Stroke));
        }

        Assert.Equal(Visibility.Visible, ellipse.Visibility);
        Assert.Equal(Visibility.Collapsed, shape.Visibility);
    });

    [Fact]
    public void NeverTakesFocus() => Sta.Run(() =>
    {
        var dot = new StatusDot();
        using var host = new KitHost(dot);

        Assert.False(dot.Focusable);
        Assert.False(dot.IsTabStop);
    });
}
