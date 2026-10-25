using System.Text.Json;
using Atap.Core.Layout;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Layout;

public sealed class LayoutResolverTests
{
    [Fact]
    public void Default_ResolvesTheTopLevel()
    {
        var document = Document();

        var active = LayoutResolver.Resolve(document, EffectiveLayout.DefaultProfileId);

        Assert.Equal(EffectiveLayout.DefaultProfileId, active.ProfileId);
        Assert.Equal(["clock", "media"], active.EnabledWidgets);
        Assert.Equal(["media"], active.CompactPriority);
        Assert.Equal("clock", Assert.Single(active.Idle).WidgetId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("")]
    public void UnknownProfile_ResolvesTheDefault(string? profileId)
    {
        var active = LayoutResolver.Resolve(Document(), profileId);

        Assert.Equal(EffectiveLayout.DefaultProfileId, active.ProfileId);
        Assert.Equal(["clock", "media"], active.EnabledWidgets);
    }

    [Fact]
    public void Profile_InheritsWhatItLeavesOut()
    {
        var document = Document();

        var active = LayoutResolver.Resolve(document, "gaming");

        Assert.Equal("gaming", active.ProfileId);
        // Its own field.
        Assert.Equal(["media"], active.EnabledWidgets);
        // Inherited from the Default.
        Assert.Equal(["media"], active.CompactPriority);
        Assert.Equal("clock", Assert.Single(active.Idle).WidgetId);
        Assert.Equal(PanelLayout.MainPageId, Assert.Single(active.PanelPages).Id);
    }

    [Fact]
    public void ProfileWithItsOwnList_StillRunsTheDefaultsWidgetsThatWorkWithoutPlacement()
    {
        var document = Document();
        document.EnabledWidgets.AddRange(["alarm", "todo"]);
        static bool WorksWithoutPlacement(string id) => id is "alarm" or "media";

        var gaming = LayoutResolver.Resolve(document, "gaming", WorksWithoutPlacement);
        var work = LayoutResolver.Resolve(document, "work", WorksWithoutPlacement);

        // Its own list first, then the Default's alarm. Media is already there, and todo needs a placement.
        Assert.Equal(["media", "alarm"], gaming.EnabledWidgets);
        // A profile that inherits the list gets the Default's as it is.
        Assert.Equal(["clock", "media", "alarm", "todo"], work.EnabledWidgets);
        Assert.Equal(["media"], document.Profiles[0].EnabledWidgets);
    }

    [Fact]
    public void InheritedField_FollowsLaterChangesToTheDefault()
    {
        var document = Document();

        document.CompactPriority.Insert(0, "clock");

        Assert.Equal(["clock", "media"], LayoutResolver.Resolve(document, "gaming").CompactPriority);
    }

    [Fact]
    public void Resolve_IsASnapshot()
    {
        var document = Document();
        var active = LayoutResolver.Resolve(document, "gaming");

        document.Profiles[0].EnabledWidgets!.Add("clock");

        Assert.Equal(["media"], active.EnabledWidgets);
    }

    [Fact]
    public void EnabledWidgetsToEdit_OnAnInheritingProfile_CopiesThenEditsOnlyThatProfile()
    {
        var document = Document();
        Assert.False(LayoutResolver.HasOwn(document, "work", LayoutField.EnabledWidgets));

        LayoutResolver.EnabledWidgetsToEdit(document, "work").Remove("media");

        Assert.True(LayoutResolver.HasOwn(document, "work", LayoutField.EnabledWidgets));
        Assert.Equal(["clock"], LayoutResolver.Resolve(document, "work").EnabledWidgets);
        Assert.Equal(["clock", "media"], document.EnabledWidgets);
        Assert.Equal(["media"], LayoutResolver.Resolve(document, "gaming").EnabledWidgets);
    }

    [Fact]
    public void EnabledWidgetsToEdit_OnAProfileWithItsOwn_EditsItInPlace()
    {
        var document = Document();
        var own = document.Profiles[0].EnabledWidgets;

        var edited = LayoutResolver.EnabledWidgetsToEdit(document, "gaming");

        Assert.Same(own, edited);
    }

    [Fact]
    public void ToEdit_ForTheDefault_IsTheTopLevel()
    {
        var document = Document();

        Assert.Same(document.EnabledWidgets, LayoutResolver.EnabledWidgetsToEdit(document, EffectiveLayout.DefaultProfileId));
        Assert.Same(document.CompactPriority, LayoutResolver.CompactPriorityToEdit(document, EffectiveLayout.DefaultProfileId));
        Assert.Same(document.States, LayoutResolver.StatesToEdit(document, null));
        Assert.False(LayoutResolver.HasOwn(document, EffectiveLayout.DefaultProfileId, LayoutField.States));
    }

    [Fact]
    public void ToEdit_ForAProfileThatIsGone_Throws_SoTheChangeNeverLandsOnTheDefault()
    {
        var document = Document();

        Assert.Throws<InvalidOperationException>(() => LayoutResolver.EnabledWidgetsToEdit(document, "missing"));
        Assert.Throws<InvalidOperationException>(() => LayoutResolver.StatesToEdit(document, "missing"));
    }

    [Fact]
    public void CompactPriorityToEdit_CopiesOnWrite()
    {
        var document = Document();

        LayoutResolver.CompactPriorityToEdit(document, "gaming").Add("clock");

        Assert.Equal(["media", "clock"], LayoutResolver.Resolve(document, "gaming").CompactPriority);
        Assert.Equal(["media"], document.CompactPriority);
    }

    [Fact]
    public void StatesToEdit_CopiesDeeply_SoTheDefaultNeverMoves()
    {
        var document = Document();

        var states = LayoutResolver.StatesToEdit(document, "gaming");
        states.Idle.Placements.Clear();
        states.Panel.Pages.Add(new PanelPage { Id = "games", Name = "Games" });

        Assert.Equal("clock", Assert.Single(document.States.Idle.Placements).WidgetId);
        Assert.Single(document.States.Panel.Pages);
        var gaming = LayoutResolver.Resolve(document, "gaming");
        Assert.Empty(gaming.Idle);
        Assert.Equal(["main", "games"], gaming.PanelPages.Select(p => p.Id));
        // Fields this version does not know are copied with the rest.
        var copy = LayoutResolver.StatesToEdit(Document(withGlow: true), "work");
        Assert.Equal(2, copy.Idle.Placements[0].ExtensionData!["glow"].GetInt32());
    }

    [Fact]
    public void Inherit_DropsTheCopy_SoTheProfileFollowsTheDefaultAgain()
    {
        var document = Document();

        LayoutResolver.Inherit(document, "gaming", LayoutField.EnabledWidgets);
        LayoutResolver.Inherit(document, EffectiveLayout.DefaultProfileId, LayoutField.EnabledWidgets);

        Assert.Null(document.Profiles[0].EnabledWidgets);
        Assert.Equal(["clock", "media"], LayoutResolver.Resolve(document, "gaming").EnabledWidgets);
        Assert.Equal(["clock", "media"], document.EnabledWidgets);
    }

    private static LayoutDocument Document(bool withGlow = false)
    {
        var document = new LayoutDocument
        {
            EnabledWidgets = ["clock", "media"],
            CompactPriority = ["media"],
            States = new StateLayouts
            {
                Idle = new StateLayout
                {
                    Placements =
                    [
                        new WidgetPlacement
                        {
                            WidgetId = "clock",
                            Size = WidgetSize.Small,
                            ExtensionData = withGlow ? new Dictionary<string, JsonElement> { ["glow"] = JsonSerializer.SerializeToElement(2) } : null,
                        },
                    ],
                },
            },
            Profiles =
            [
                new LayoutProfile { Id = "gaming", Name = "Gaming", EnabledWidgets = ["media"] },
                new LayoutProfile { Id = "work", Name = "Work" },
            ],
        };
        document.Normalize();
        return document;
    }
}
