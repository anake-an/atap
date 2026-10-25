using Atap.Core.Layout;
using Atap.Core.Presets;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Presets;

public class LayoutPresetsTests
{
    private static readonly WidgetCatalog Catalog = WidgetCatalog.FromDescriptors(
    [
        new Descriptor("clock", [WidgetSize.Small], [NotchState.Idle, NotchState.Expanded]),
        new Descriptor("media", [WidgetSize.Medium], [NotchState.Compact, NotchState.Expanded, NotchState.Panel]),
        new Descriptor("focus-timer", [WidgetSize.Small, WidgetSize.Medium], [NotchState.Compact, NotchState.Expanded, NotchState.Panel]),
        new Descriptor("missed-alerts", [WidgetSize.Medium], [NotchState.Panel]),
    ]);

    [Fact]
    public void Apply_PlacesThePreset_WithTheEditorsRules()
    {
        var document = DefaultLayout.Create();

        LayoutPresets.Apply(document, LayoutPreset.Work, Catalog);

        Assert.Equal(["media", "focus-timer"], document.States.Expanded.Placements.Select(p => p.WidgetId));
        Assert.Equal(WidgetSize.Small, document.States.Expanded.Placements[1].Size);
        Assert.Contains("focus-timer", document.EnabledWidgets);
        Assert.Contains(document.States.Compact.Placements, p => p.WidgetId == "focus-timer" && p.Slot == PlacementSlot.Left);
        Assert.Equal("focus-timer", document.CompactPriority[0]);
    }

    [Fact]
    public void Apply_SkipsWidgetsThisBuildDoesNotHave()
    {
        var document = DefaultLayout.Create();

        LayoutPresets.Apply(document, LayoutPreset.Malaysia, Catalog);

        Assert.DoesNotContain("waktu-solat", document.EnabledWidgets);
        Assert.Equal(["media"], document.States.Expanded.Placements.Select(p => p.WidgetId));
        Assert.Equal(["clock"], document.States.Idle.Placements.Select(p => p.WidgetId));
    }

    [Fact]
    public void Apply_KeepsPlacementAndWidgetSettings()
    {
        var document = DefaultLayout.Create();
        document.Placement.Anchor = Core.Placement.NotchAnchor.LeftCenter;
        document.SetWidgetSetting("media", "x", 1);

        LayoutPresets.Apply(document, LayoutPreset.Minimal, Catalog);

        Assert.Equal(Core.Placement.NotchAnchor.LeftCenter, document.Placement.Anchor);
        Assert.True(document.GetWidgetSettings("media").ContainsKey("x"));
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryPresetPanel_StartsWithTheHoverWidgets_AndEndsWithTheMissedAlertsIfAny(LayoutPreset preset)
    {
        var placements = LayoutPresets.PlacementsOf(preset);
        var hover = placements.Where(p => p.Spot.State == NotchState.Expanded).Select(p => p.TypeId).ToList();
        var panel = placements.Where(p => p.Spot.State == NotchState.Panel).Select(p => p.TypeId).ToList();

        if (panel.All(id => id == "missed-alerts"))
            return; // Follows the hover view.
        Assert.Equal(hover, panel.Take(hover.Count));
        Assert.DoesNotContain("missed-alerts", panel.SkipLast(1));
    }

    [Fact]
    public void Minimal_PanelFollowsTheHoverView()
    {
        var document = DefaultLayout.Create();

        LayoutPresets.Apply(document, LayoutPreset.Minimal, Catalog);

        Assert.True(PanelContent.FollowsHover(LayoutResolver.Resolve(document, null).PanelPages));
        Assert.Contains("missed-alerts", document.EnabledWidgets);
    }

    public static TheoryData<LayoutPreset> Presets() => new(LayoutPresets.All);

    [Fact]
    public void OnlyTheMalaysiaPreset_UsesTheInternet()
    {
        Assert.Equal([LayoutPreset.Malaysia], LayoutPresets.All.Where(LayoutPresets.UsesInternet));
        Assert.All(LayoutPresets.All, p => Assert.NotEmpty(LayoutPresets.PlacementsOf(p)));
    }

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
