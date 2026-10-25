using Atap.Core.Layout;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Layout;

public class PanelContentTests
{
    private static readonly Dictionary<string, IWidgetDescriptor> Running = new(StringComparer.Ordinal)
    {
        ["media"] = new Descriptor("media", [WidgetSize.Medium], [NotchState.Compact, NotchState.Expanded, NotchState.Panel]),
        ["focus-timer"] = new Descriptor("focus-timer", [WidgetSize.Small, WidgetSize.Medium], [NotchState.Expanded, NotchState.Panel]),
        ["alarm"] = new Descriptor("alarm", [WidgetSize.Small], [NotchState.Expanded, NotchState.Panel]),
        ["clock"] = new Descriptor("clock", [WidgetSize.Small], [NotchState.Idle, NotchState.Expanded]),
        ["missed-alerts"] = new Descriptor("missed-alerts", [WidgetSize.Medium], [NotchState.Panel]),
        ["weather"] = new Descriptor("weather", [WidgetSize.Medium], [NotchState.Expanded, NotchState.Panel]),
    };

    [Fact]
    public void NewInstall_ClickShowsTheHoverWidgets_NotADefaultPage()
    {
        var layout = Resolve(DefaultLayout.Create());

        var page = Assert.Single(PanelContent.Pages(layout, Find, NothingMissed));

        Assert.Equal(["media"], page.Select(p => p.WidgetId));
    }

    [Fact]
    public void UnfilledPanel_ShowsTheHoverWidgetsASizeUp_ThenTheMissedAlerts()
    {
        var document = DefaultLayout.Create();
        document.States.Expanded.Placements =
        [
            new WidgetPlacement { WidgetId = "media", Size = WidgetSize.Medium },
            new WidgetPlacement { WidgetId = "focus-timer", Size = WidgetSize.Small, Order = 1 },
            new WidgetPlacement { WidgetId = "alarm", Size = WidgetSize.Small, Order = 2 },
            new WidgetPlacement { WidgetId = "clock", Size = WidgetSize.Small, Order = 3 },
        ];

        var page = Assert.Single(PanelContent.Pages(Resolve(document), Find, _ => true));

        // The clock has no panel view, the alarm has no medium size.
        Assert.Equal(["media", "focus-timer", "alarm", "missed-alerts"], page.Select(p => p.WidgetId));
        Assert.Equal([WidgetSize.Medium, WidgetSize.Medium, WidgetSize.Small, WidgetSize.Medium], page.Select(p => p.Size));
        Assert.Equal([0, 1, 2, 3], page.Select(p => p.Order));
    }

    [Fact]
    public void MissedAlerts_TakeNoSlot_WhileNothingWasMissed()
    {
        var page = Assert.Single(PanelContent.Pages(Resolve(DefaultLayout.Create()), Find, NothingMissed));

        Assert.DoesNotContain(page, p => p.WidgetId == "missed-alerts");
    }

    [Fact]
    public void PanelTheUserFilled_IsShownAsArranged()
    {
        var document = DefaultLayout.Create();
        document.States.Panel.Pages[0].Placements =
        [
            new WidgetPlacement { WidgetId = "weather", Size = WidgetSize.Medium, Order = 1 },
            new WidgetPlacement { WidgetId = "alarm", Size = WidgetSize.Small, Order = 0 },
        ];
        var layout = Resolve(document);

        Assert.False(PanelContent.FollowsHover(layout.PanelPages));
        var page = Assert.Single(PanelContent.Pages(layout, Find, _ => true));
        Assert.Equal(["alarm", "weather"], page.Select(p => p.WidgetId));
    }

    [Fact]
    public void EmptyPages_AreSkipped_AndAnEmptyHoverViewLeavesNothing()
    {
        var document = DefaultLayout.Create();
        document.States.Expanded.Placements = [];
        document.States.Panel.Pages.Add(new PanelPage { Id = "two", Placements = [new WidgetPlacement { WidgetId = "gone" }] });

        Assert.Empty(PanelContent.Pages(Resolve(document), Find, NothingMissed));
    }

    private static IWidgetDescriptor? Find(string id) => Running.GetValueOrDefault(id);

    private static bool NothingMissed(string id) => id != "missed-alerts";

    private static EffectiveLayout Resolve(LayoutDocument document) => LayoutResolver.Resolve(document, null);

    private sealed class Descriptor(string typeId, IReadOnlyList<WidgetSize> sizes, IReadOnlyList<NotchState> states) : IWidgetDescriptor
    {
        public string TypeId => typeId;

        public string NameKey => $"{typeId}.name";

        public string DescriptionKey => $"{typeId}.description";

        public WidgetGroup Group => WidgetGroup.Custom;

        public IReadOnlyList<WidgetSize> SupportedSizes => sizes;

        public IReadOnlyList<NotchState> SupportedStates => states;

        public IReadOnlyList<SettingDefinition> Settings => [];

        public WidgetCapabilities Capabilities => WidgetCapabilities.None;

        public bool AllowsMultipleInstances => false;

        public bool WorksWithoutPlacement => false;

        public Uri ViewsSource => new($"/Tests;component/{typeId}.xaml", UriKind.Relative);

        public IWidget Create(WidgetContext context) => throw new NotSupportedException();
    }
}
