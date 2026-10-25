using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using Atap.Core.Custom;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

public class ProgressPillTests
{
    [Fact]
    public void Template_LoadsAndFillsToTheValue() => Sta.Run(() =>
    {
        var pill = new ProgressPill { Width = 200, Minimum = 0, Maximum = 1, Value = 0.25 };
        using var host = new KitHost(pill);

        var track = KitHost.Named<Border>(pill, "PART_Track");
        var indicator = KitHost.Named<Border>(pill, "PART_Indicator");
        Assert.Equal(200, track.ActualWidth);
        Assert.Equal(50, indicator.ActualWidth, 1);
        Assert.Equal(6, pill.ActualHeight);
        // Round ends at any height.
        Assert.Equal(new CornerRadius(3), track.CornerRadius);
    });

    [Fact]
    public void Value_Binds() => Sta.Run(() =>
    {
        var source = new ProgressPill { Maximum = 100 };
        var pill = new ProgressPill { Width = 100, Maximum = 100 };
        pill.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding(nameof(ProgressPill.Value)) { Source = source });
        using var host = new KitHost(pill);
        var indicator = KitHost.Named<Border>(pill, "PART_Indicator");

        source.Value = 80;
        host.Layout();

        Assert.Equal(80, pill.Value);
        Assert.Equal(80, indicator.ActualWidth, 1);
    });

    [Theory]
    [InlineData(ValueLevel.Normal, "MediaAccentColor")]
    [InlineData(ValueLevel.Warning, "ProductivityAccentColor")]
    [InlineData(ValueLevel.Critical, "SystemAccentColor")]
    [InlineData(ValueLevel.Unknown, "TextMutedColor")]
    public void Level_SetsTheFill(ValueLevel level, string colour) => Sta.Run(() =>
    {
        var pill = new ProgressPill { Width = 100, Value = 50, Level = level };
        pill.SetResourceReference(ProgressPill.AccentProperty, "MediaAccentBrush");
        using var host = new KitHost(pill);

        Assert.Equal(KitHost.Token(colour), KitHost.ColorOf(KitHost.Named<Border>(pill, "PART_Indicator").Background));
    });

    [Fact]
    public void ScreenReader_GetsANameTheValueAndTheLevel() => Sta.Run(() =>
    {
        var pill = new ProgressPill { Width = 100, Value = 40, Level = ValueLevel.Warning };
        using var host = new KitHost(pill);
        var peer = KitHost.Peer(pill);

        Assert.Equal(AutomationControlType.ProgressBar, peer.GetAutomationControlType());
        Assert.Equal("Progress", peer.GetName());
        Assert.Equal("Warning", peer.GetItemStatus());
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer.GetPattern(PatternInterface.RangeValue));
        Assert.Equal(40, range.Value);
        Assert.True(range.IsReadOnly);

        AutomationProperties.SetName(pill, "Disk C");
        pill.Level = ValueLevel.Normal;
        Assert.Equal("Disk C", peer.GetName());
        Assert.Equal(string.Empty, peer.GetItemStatus());
    });

    [Fact]
    public void Indeterminate_IsAStillDimBar() => Sta.Run(() =>
    {
        var pill = new ProgressPill { Width = 120, IsIndeterminate = true };
        using var host = new KitHost(pill);
        var indicator = KitHost.Named<Border>(pill, "PART_Indicator");

        Assert.Equal(120, indicator.ActualWidth, 1);
        Assert.Equal(0.4, indicator.Opacity, 3);
    });

    [Fact]
    public void HighContrast_UsesSystemColours() => Sta.Run(() =>
    {
        var pill = new ProgressPill { Width = 100, Value = 50, Level = ValueLevel.Critical };
        using var host = new KitHost(pill);
        var track = KitHost.Named<Border>(pill, "PART_Track");
        var indicator = KitHost.Named<Border>(pill, "PART_Indicator");

        using (KitHost.HighContrast())
        {
            Assert.Equal(SystemColors.HighlightColor, KitHost.ColorOf(indicator.Background));
            Assert.Equal(SystemColors.WindowColor, KitHost.ColorOf(track.Background));
            Assert.Equal(new Thickness(1), track.BorderThickness);
        }

        Assert.Equal(KitHost.Token("SystemAccentColor"), KitHost.ColorOf(indicator.Background));
    });

    [Fact]
    public void NeverTakesFocus() => Sta.Run(() =>
    {
        var pill = new ProgressPill();
        using var host = new KitHost(pill);

        Assert.False(pill.Focusable);
        Assert.False(pill.IsTabStop);
    });
}
