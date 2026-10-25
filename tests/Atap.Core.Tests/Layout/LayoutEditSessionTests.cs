using Atap.Core.Layout;
using Atap.Core.Layout.Editing;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Layout;

public class LayoutEditSessionTests
{
    private static readonly EditSpot Open = new(NotchState.Expanded);
    private static readonly EditSpot Idle = new(NotchState.Idle);
    private static readonly EditSpot Page1 = new(NotchState.Panel, 0);

    private static readonly WidgetCatalog Catalog = WidgetCatalog.FromDescriptors(
    [
        new Descriptor("media", [WidgetSize.Medium], [NotchState.Compact, NotchState.Expanded, NotchState.Panel]),
        new Descriptor("clock", [WidgetSize.Small], [NotchState.Idle, NotchState.Expanded]),
        new Descriptor("missed-alerts", [WidgetSize.Medium], [NotchState.Panel]),
        new Descriptor("focus-timer", [WidgetSize.Small, WidgetSize.Medium], [NotchState.Compact, NotchState.Expanded, NotchState.Panel]),
        new Descriptor("json-url", [WidgetSize.Small, WidgetSize.Medium, WidgetSize.Large], [NotchState.Expanded, NotchState.Panel], multiple: true),
        new Descriptor("alarm", [WidgetSize.Small], [NotchState.Expanded, NotchState.Panel], withoutPlacement: true),
    ]);

    private static LayoutEditSession NewSession(LayoutDocument? document = null, string profile = EffectiveLayout.DefaultProfileId) =>
        new(document ?? DefaultLayout.Create(), profile, Catalog);

    [Fact]
    public void Add_PlacesAtTheEnd_EnablesIt_AndJoinsCompact()
    {
        var session = NewSession();

        Assert.Equal(EditResult.Applied, session.Add("focus-timer", Open));

        Assert.Equal(["media", "focus-timer"], session.PlacementsAt(Open).Select(p => p.WidgetId));
        Assert.Equal(WidgetSize.Small, session.PlacementsAt(Open)[1].Size);
        Assert.Contains("focus-timer", session.Draft.EnabledWidgets);
        // The default priority already names the focus timer first, and adding keeps that place.
        Assert.Equal("focus-timer", session.Draft.CompactPriority[0]);
        Assert.Equal(2, session.Draft.States.Compact.Placements.Count(p => p.WidgetId == "focus-timer"));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Add_RejectsAStateOrSizeTheWidgetDoesNotSupport()
    {
        var session = NewSession();

        Assert.Equal(EditResult.Rejected, session.Add("missed-alerts", Idle));
        Assert.Equal(EditResult.Rejected, session.Add("clock", Open, WidgetSize.Large));
        Assert.Equal(EditResult.Rejected, session.Add("unknown", Open));
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Add_ASingleInstanceWidget_OnlyOncePerPlace()
    {
        var session = NewSession();

        session.Add("focus-timer", Open);

        Assert.False(session.CanAdd("focus-timer", Open));
        Assert.Equal(EditResult.Rejected, session.Add("focus-timer", Open));
        Assert.True(session.CanAdd("focus-timer", Page1));
    }

    [Fact]
    public void Add_AMultiInstanceWidget_GetsNumberedIds()
    {
        var session = NewSession();

        session.Add("json-url", Open);
        session.Add("json-url", Open);
        session.Add("json-url", Page1);

        Assert.Equal(["json-url", "json-url#2", "json-url#3"], session.Draft.EnabledWidgets.Where(id => id.StartsWith("json-url", StringComparison.Ordinal)));
    }

    [Fact]
    public void Add_NeverReusesAnIdThatWasInUseWhenEditingStarted()
    {
        var first = NewSession();
        first.Add("json-url", Open);
        var document = new LayoutDocument();
        first.ApplyTo(document);

        var session = new LayoutEditSession(document, EffectiveLayout.DefaultProfileId, Catalog);
        var index = session.Draft.States.Expanded.Placements.FindIndex(p => p.WidgetId == "json-url");
        session.Remove(Open, index);
        session.Add("json-url", Open);

        Assert.DoesNotContain("json-url", session.Draft.EnabledWidgets);
        Assert.Contains("json-url#2", session.Draft.EnabledWidgets);
    }

    [Fact]
    public void Remove_TheLastPlace_StopsTheWidget_UnlessItWorksWithoutOne()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);
        session.Add("alarm", Open);

        session.Remove(Open, 1);
        session.Remove(Open, 1);

        Assert.DoesNotContain("focus-timer", session.Draft.EnabledWidgets);
        Assert.DoesNotContain("focus-timer", session.Draft.CompactPriority);
        Assert.DoesNotContain(session.Draft.States.Compact.Placements, p => p.WidgetId == "focus-timer");
        Assert.Contains("alarm", session.Draft.EnabledWidgets);
    }

    [Fact]
    public void Remove_KeepsAWidgetThatIsStillPlacedElsewhere()
    {
        var session = NewSession();
        session.Add("media", Page1);

        session.Remove(Open, 0);

        Assert.Contains("media", session.Draft.EnabledWidgets);
        Assert.Empty(session.PlacementsAt(Open));
    }

    [Fact]
    public void Resize_OnlyToASupportedSize()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);

        Assert.Equal(EditResult.Applied, session.Resize(Open, 1, WidgetSize.Medium));
        Assert.Equal(EditResult.Unchanged, session.Resize(Open, 1, WidgetSize.Medium));
        Assert.Equal(EditResult.Rejected, session.Resize(Open, 1, WidgetSize.Large));
        Assert.Equal(EditResult.Rejected, session.Resize(Open, 9, WidgetSize.Small));
        Assert.Equal(WidgetSize.Medium, session.PlacementsAt(Open)[1].Size);
    }

    [Fact]
    public void Move_ReordersAndRenumbers()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);
        session.Add("json-url", Open);

        Assert.Equal(EditResult.Applied, session.Move(Open, 2, -2));
        Assert.Equal(EditResult.Unchanged, session.Move(Open, 0, -1));

        Assert.Equal(["json-url", "media", "focus-timer"], session.PlacementsAt(Open).Select(p => p.WidgetId));
        Assert.Equal([0, 1, 2], session.PlacementsAt(Open).Select(p => p.Order));
    }

    [Fact]
    public void Pages_AddAndRemove_KeepAtLeastOne()
    {
        var session = NewSession();

        Assert.Equal(EditResult.Applied, session.AddPage("Page 2"));
        session.Add("json-url", new EditSpot(NotchState.Panel, 1));
        Assert.Equal(2, session.Pages.Count);

        Assert.Equal(EditResult.Applied, session.RemovePage(1));
        Assert.DoesNotContain("json-url", session.Draft.EnabledWidgets);
        Assert.Equal(EditResult.Rejected, session.RemovePage(0));
        Assert.Equal(EditResult.Rejected, session.Add("json-url", new EditSpot(NotchState.Panel, 5)));
    }

    [Fact]
    public void Pages_StopAtSix()
    {
        var session = NewSession();
        for (var i = 2; i <= LayoutEditSession.MaxPages; i++)
            Assert.Equal(EditResult.Applied, session.AddPage($"Page {i}"));

        Assert.Equal(EditResult.Rejected, session.AddPage("Page 7"));
    }

    [Fact]
    public void Priority_MovesUpAndDown()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);

        Assert.Equal(EditResult.Applied, session.MovePriority("media", -10));
        Assert.Equal("media", session.Draft.CompactPriority[0]);
        Assert.Equal(EditResult.Unchanged, session.MovePriority("media", -1));
        Assert.Equal(EditResult.Rejected, session.MovePriority("clock", 1));
    }

    [Fact]
    public void Undo_AndRedo_EveryOperation()
    {
        var session = NewSession();
        var start = Snapshot(session);
        session.Add("focus-timer", Open);
        session.Resize(Open, 1, WidgetSize.Medium);
        session.Move(Open, 1, -1);
        session.AddPage("Page 2");
        session.MovePriority("media", -1);
        session.Remove(Open, 0);
        var end = Snapshot(session);

        for (var i = 0; i < 6; i++)
            Assert.True(session.Undo());
        Assert.False(session.Undo());
        Assert.Equal(start, Snapshot(session));
        Assert.False(session.IsDirty);

        for (var i = 0; i < 6; i++)
            Assert.True(session.Redo());
        Assert.False(session.Redo());
        Assert.Equal(end, Snapshot(session));
    }

    [Fact]
    public void ANewChange_ClearsRedo()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);
        session.Undo();

        session.Add("json-url", Open);

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Reset_GoesBackToTheDefault_AndCanBeUndone()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);

        Assert.Equal(EditResult.Applied, session.ResetToDefault());
        Assert.DoesNotContain("focus-timer", session.Draft.EnabledWidgets);
        Assert.Equal(EditResult.Unchanged, session.ResetToDefault());

        session.Undo();
        Assert.Contains("focus-timer", session.Draft.EnabledWidgets);
    }

    [Fact]
    public void ApplyTo_TheDefaultProfile_WritesTheTopLevel_AndNeverSharesLists()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);
        var document = DefaultLayout.Create();

        session.ApplyTo(document);
        session.Add("json-url", Open);

        Assert.Equal(["media", "focus-timer"], document.States.Expanded.Placements.Select(p => p.WidgetId));
    }

    [Fact]
    public void ApplyTo_AnotherProfile_CopiesOnWrite_AndLeavesTheTopLevel()
    {
        var document = DefaultLayout.Create();
        document.Profiles.Add(new LayoutProfile { Id = "work", Name = "Work" });
        var session = NewSession(document, "work");
        session.Add("focus-timer", Open);

        session.ApplyTo(document);

        var work = document.FindProfile("work")!;
        Assert.Equal(["media", "focus-timer"], work.States!.Expanded.Placements.Select(p => p.WidgetId));
        Assert.Contains("focus-timer", work.EnabledWidgets!);
        Assert.Equal(["media"], document.States.Expanded.Placements.Select(p => p.WidgetId));
        Assert.DoesNotContain("focus-timer", document.EnabledWidgets);
    }

    [Fact]
    public void AnUnknownProfile_EditsTheDefault()
    {
        Assert.Equal(EffectiveLayout.DefaultProfileId, NewSession(profile: "gone").ProfileId);
    }

    private static string Snapshot(LayoutEditSession session) =>
        System.Text.Json.JsonSerializer.Serialize(session.Draft, LayoutStore.JsonOptions);

    private sealed class Descriptor(
        string typeId,
        IReadOnlyList<WidgetSize> sizes,
        IReadOnlyList<NotchState> states,
        bool multiple = false,
        bool withoutPlacement = false) : IWidgetDescriptor
    {
        public string TypeId => typeId;

        public string NameKey => $"{typeId}.name";

        public string DescriptionKey => $"{typeId}.description";

        public WidgetGroup Group => WidgetGroup.Custom;

        public IReadOnlyList<WidgetSize> SupportedSizes => sizes;

        public IReadOnlyList<NotchState> SupportedStates => states;

        public IReadOnlyList<SettingDefinition> Settings => [];

        public WidgetCapabilities Capabilities => WidgetCapabilities.None;

        public bool AllowsMultipleInstances => multiple;

        public bool WorksWithoutPlacement => withoutPlacement;

        public Uri ViewsSource => new($"/Tests;component/{typeId}.xaml", UriKind.Relative);

        public IWidget Create(WidgetContext context) => throw new NotSupportedException();
    }
}
