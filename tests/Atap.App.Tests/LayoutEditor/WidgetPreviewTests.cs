using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Atap.Composition;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Widgets;
using Atap.Tests.Composition;
using Atap.Tests.Infrastructure;
using Atap.Ui.Preview;

namespace Atap.Tests.LayoutEditor;

/// <summary>Widget previews: sample content for every widget, a plain card for the rest, and plain state names.</summary>
public class WidgetPreviewTests
{
    private static readonly WidgetCatalog Catalog = Discovery.Widgets(typeof(App).Assembly);

    public static TheoryData<string> TypeIds()
    {
        var data = new TheoryData<string>();
        foreach (var descriptor in Catalog.Descriptors)
            data.Add(descriptor.TypeId);
        return data;
    }

    [Theory]
    [MemberData(nameof(TypeIds))]
    public void EveryWidget_HasSampleContent_InEnglishAndMalay(string typeId)
    {
        Assert.True(WidgetSamples.TryGet(typeId, out var sample), $"{typeId} has no sample content for its preview.");

        foreach (var language in new[] { Localizer.English, Localizer.Malay })
        {
            var text = Text(language, out var missing);
            Assert.NotEmpty(text[WidgetSample.ValueKey(typeId)]);
            Assert.NotEmpty(text[WidgetSample.DetailKey(typeId)]);
            Assert.NotEmpty(text[WidgetSample.LineKey(typeId)]);
            Assert.Empty(missing);
        }

        Assert.True(Sta.Run(() => Application.Current.TryFindResource(sample.IconKey) is Geometry), $"{sample.IconKey} is not an icon.");
    }

    [Fact]
    public void EverySample_BelongsToAWidget() =>
        Assert.All(WidgetSamples.TypeIds, id => Assert.True(Catalog.TryGet(id, out _), $"{id} has sample content but no widget."));

    [Fact]
    public void ATile_ShowsSampleContent()
    {
        var text = Text(Localizer.English, out _);

        var tile = NotchPreviews.Tile(Catalog, "media", WidgetSize.Medium, text, 2);

        Assert.True(tile.IsSample);
        Assert.Equal("Now playing", tile.Name);
        Assert.Equal("Sample Song", tile.Value);
        Assert.Equal("Sample Artist", tile.Detail);
        Assert.True(tile.HasProgress);
        Assert.Equal(2, tile.Index);
        Assert.Equal("Now playing, Medium", tile.AutomationName);
        Assert.Equal(320, tile.Width);
    }

    [Fact]
    public void ATileInMalay_UsesMalaySamples()
    {
        var tile = NotchPreviews.Tile(Catalog, "media", WidgetSize.Small, Text(Localizer.Malay, out _));

        Assert.Equal("Lagu Contoh", tile.Value);
        Assert.False(tile.HasProgress);
    }

    [Fact]
    public void AWidgetWithoutSampleContent_GetsAPlainCard()
    {
        var catalog = WidgetCatalog.FromDescriptors([new TestDescriptor("made-up")]);
        var text = Text(Localizer.English, out _);

        var tile = NotchPreviews.Tile(catalog, "made-up", WidgetSize.Small, text);

        Assert.False(tile.IsSample);
        Assert.True(tile.IsPlain);
        Assert.Equal("made-up.name", tile.Name);
        Assert.Empty(tile.Value);
        Assert.False(tile.HasDetail);
        Assert.False(tile.HasProgress);
    }

    [Fact]
    public void AnUnknownWidget_GetsAPlainCardWithItsId()
    {
        var tile = NotchPreviews.Tile(Catalog, "gone#2", WidgetSize.Small, Text(Localizer.English, out _));

        Assert.False(tile.IsSample);
        Assert.Equal("gone", tile.TypeId);
        Assert.Equal("gone#2", tile.Name);
    }

    [Fact]
    public void States_HavePlainNames()
    {
        var english = Text(Localizer.English, out _);
        var malay = Text(Localizer.Malay, out var missing);

        Assert.Equal("Small notch", english[NotchPreviews.LabelKey(NotchState.Idle)]);
        Assert.Equal("When something is live", english[NotchPreviews.LabelKey(NotchState.Compact)]);
        Assert.Equal("When you point at it", english[NotchPreviews.LabelKey(NotchState.Expanded)]);
        Assert.Equal("When you click it", english[NotchPreviews.LabelKey(NotchState.Panel)]);

        var states = Enum.GetValues<NotchState>();
        Assert.Equal(states.Length, states.Select(s => malay[NotchPreviews.LabelKey(s)]).Distinct().Count());
        Assert.All(states, s => Assert.DoesNotContain(s.ToString(), malay[NotchPreviews.LabelKey(s)], StringComparison.OrdinalIgnoreCase));
        Assert.All(states, s => Assert.NotEmpty(malay[NotchPreviews.HintKey(s)]));
        Assert.Empty(missing);
    }

    [Fact]
    public void APicture_KeepsTheLayoutOrder()
    {
        var placements = new[]
        {
            new WidgetPlacement { WidgetId = "clock", Size = WidgetSize.Small, Order = 1 },
            new WidgetPlacement { WidgetId = "media", Size = WidgetSize.Medium, Order = 0 },
        };

        var picture = NotchPreviews.Picture(Catalog, NotchState.Expanded, placements, Text(Localizer.English, out _));

        Assert.Equal(["media", "clock"], picture.Tiles.Select(t => t.InstanceId));
        Assert.Equal([0, 1], picture.Tiles.Select(t => t.Index));
        Assert.True(picture.IsGrid);
        Assert.Equal("When you point at it. Now playing, Medium, Clock, Small", picture.AutomationName);
    }

    [Fact]
    public void Templates_BuildInAWindow() => Sta.Run(() =>
    {
        // Resource lookups inside templates only fail when the template is applied, so apply them.
        var views = (ResourceDictionary)Application.LoadComponent(new Uri("/Atap;component/Ui/Preview/PreviewViews.xaml", UriKind.Relative));
        var text = Text(Localizer.English, out _);
        var layout = DefaultLayout.Create();
        foreach (var state in new[] { NotchState.Idle, NotchState.Panel })
        {
            var picture = NotchPreviews.Picture(Catalog, state, layout.States.PlacementsFor(state), text);
            var presenter = new ContentPresenter { Content = picture };
            var window = new Window { Content = presenter, ShowActivated = false, WindowStyle = WindowStyle.None, Width = 1, Height = 1 };
            window.Resources.MergedDictionaries.Add(views);
            try
            {
                window.Measure(new Size(800, 400));
                presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(presenter.DesiredSize.Width > 0);
            }
            finally
            {
                window.Close();
            }
        }
    });

    private static Localizer Text(string language, out List<string> missing)
    {
        var text = new Localizer(new AssemblyStringResources(typeof(App).Assembly), language);
        var list = new List<string>();
        text.MissingKey += (_, key) => list.Add(key);
        missing = list;
        return text;
    }
}
