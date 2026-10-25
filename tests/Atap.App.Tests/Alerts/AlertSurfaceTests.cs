using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Notch.Surfaces.Alerts;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Alerts;

/// <summary>The Alert surface template and its view model.</summary>
public class AlertSurfaceTests
{
    private static readonly Localizer Text = new(new AssemblyStringResources(typeof(App).Assembly), Localizer.English);

    private static AlertViewModel Create(Alert alert, Action? stop = null, Action? close = null) =>
        new(new ShownAlert("test", alert, DateTimeOffset.UnixEpoch), Text, stop ?? (() => { }), close ?? (() => { }));

    [Fact]
    public void ScreenReaderText_SaysWhatTheAlertIs()
    {
        Assert.Equal("Alert: Battery full", Create(new Alert("a", AlertPriority.Normal, "Battery full")).AccessibleSummary);
        Assert.Equal(
            "Alert: Battery low. 15% left",
            Create(new Alert("a", AlertPriority.High, "Battery low") { Subtitle = "15% left" }).AccessibleSummary);
        Assert.Equal(
            "Alert: Volume, 40%",
            Create(new Alert("a", AlertPriority.Overlay, "Volume") { Level = 0.4 }).AccessibleSummary);
    }

    [Fact]
    public void Critical_InterruptsTheScreenReaderAndHasClose()
    {
        var critical = Create(new Alert("a", AlertPriority.Critical, "Alarm"));
        var normal = Create(new Alert("a", AlertPriority.Normal, "Notice"));

        Assert.Equal(AutomationLiveSetting.Assertive, critical.LiveSetting);
        Assert.Equal(AutomationLiveSetting.Polite, normal.LiveSetting);
        Assert.True(critical.ShowClose);
        Assert.False(normal.ShowClose);

        critical.CanStop = true;
        Assert.False(critical.ShowClose);
    }

    [Fact]
    public void Level_IsKeptBetweenNoneAndFull()
    {
        var over = Create(new Alert("a", AlertPriority.Overlay, "Volume") { Level = 1.7 });
        var under = Create(new Alert("a", AlertPriority.Overlay, "Volume") { Level = -1 });

        Assert.Equal(100, over.LevelPercent);
        Assert.Equal(0, under.LevelPercent);
        Assert.Equal("100%", over.LevelText);
        Assert.False(over.HasSubtitle);
    }

    [Theory]
    [InlineData("volume", AlertTone.Neutral, "")]
    [InlineData("VOLUME", AlertTone.Neutral, "")]
    [InlineData(null, AlertTone.Warning, "")]
    [InlineData("no-such-icon", AlertTone.Positive, "")]
    [InlineData(null, AlertTone.Neutral, "")]
    public void Glyph_ComesFromTheIconKeyOrTheTone(string? iconKey, AlertTone tone, string glyph)
    {
        Assert.Equal(glyph, AlertIcons.GlyphFor(iconKey, tone));
    }

    [Fact]
    public void Template_ShowsTheAlertAndItsStopButton() => Sta.Run(() =>
    {
        var stopped = 0;
        var alert = Create(new Alert("a", AlertPriority.High, "Zohor") { Subtitle = "Kuala Lumpur" }, stop: () => stopped++);
        alert.CanStop = true;

        var root = Build(alert, vertical: false);
        try
        {
            var texts = Descendants<TextBlock>(root).Where(t => t.IsVisible || t.Visibility == Visibility.Visible).Select(t => t.Text).ToList();
            Assert.Contains("Zohor", texts);
            Assert.Contains("Kuala Lumpur", texts);

            var buttons = Descendants<Button>(root).ToList();
            var stop = Assert.Single(buttons, b => b.Visibility == Visibility.Visible);
            Assert.Equal("Stop", stop.Content);
            Assert.False(stop.Focusable);
            stop.Command.Execute(null);
            Assert.Equal(1, stopped);

            var host = Assert.Single(Descendants<Grid>(root), g => AutomationProperties.GetName(g) == "Alert: Zohor. Kuala Lumpur");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(host));
            Assert.Equal(260, host.Width);
        }
        finally
        {
            Window.GetWindow(root)?.Close();
        }
    });

    [Fact]
    public void Template_OnASideEdgeIsNarrowAndTall() => Sta.Run(() =>
    {
        var alert = Create(new Alert("a", AlertPriority.Critical, "Timer done"));

        var root = Build(alert, vertical: true);
        try
        {
            var panel = Assert.Single(Descendants<DockPanel>(root), p => AutomationProperties.GetName(p) == "Alert: Timer done");
            Assert.True(panel.Width < 260);
            var close = Assert.Single(Descendants<Button>(root), b => b.Visibility == Visibility.Visible);
            Assert.Equal("Close", close.Content);
        }
        finally
        {
            Window.GetWindow(root)?.Close();
        }
    });

    [Fact]
    public void Template_WithNoAlert_IsEmpty() => Sta.Run(() =>
    {
        var root = Build(null, vertical: false);
        try
        {
            Assert.Empty(Descendants<TextBlock>(root));
        }
        finally
        {
            Window.GetWindow(root)?.Close();
        }
    });

    private static FrameworkElement Build(AlertViewModel? alert, bool vertical)
    {
        var presenter = new ContentPresenter
        {
            Content = new SurfaceStandIn(alert, vertical),
            ContentTemplate = (DataTemplate)Application.Current.FindResource("surface.Alert"),
        };
        var window = new Window { Content = presenter, ShowActivated = false, WindowStyle = WindowStyle.None, Width = 400, Height = 200 };
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        presenter.Arrange(new Rect(presenter.DesiredSize));
        presenter.UpdateLayout();
        _ = window;
        return presenter;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var deeper in Descendants<T>(child))
                yield return deeper;
        }
    }

    /// <summary>What the surface template binds to on NotchViewModel.</summary>
    private sealed class SurfaceStandIn(object? content, bool vertical)
    {
        public object? SurfaceContent { get; } = content;

        public bool IsVertical { get; } = vertical;
    }
}
