using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncScopeTests
{
    [Theory]
    [InlineData("settings:rules")]
    [InlineData("layout:enabledWidgets")]
    [InlineData("layout:states")]
    [InlineData("layout:compactPriority")]
    [InlineData("layout:profiles")]
    [InlineData("settings:malaysia.state")]
    [InlineData("layout:widgetSettings:clock")]
    [InlineData("layout:widgetSettings:json-url#nas")]
    [InlineData("layout:widgetSettings:world-clock#kl-2")]
    [InlineData("layout:widgetSettings:json-url#2")]
    [InlineData("layout:widgetSettings:ab")]
    [InlineData("layout:widgetSettings:a2345678901234567890123456789012#b2345678901234567890123456789012")]
    public void Classify_SyncedUnits(string name)
    {
        Assert.Equal(SyncUnitKind.Synced, Scope().Classify(name));
    }

    [Fact]
    public void Classify_Placement_IsExcluded()
    {
        Assert.Equal(SyncUnitKind.Excluded, Scope().Classify(SyncUnitNames.Placement));
    }

    [Fact]
    public void Classify_RegisteredMachineKey_IsExcluded()
    {
        Assert.Equal(SyncUnitKind.Excluded, Scope().Classify("settings:display.monitor"));
    }

    [Theory]
    [MemberData(nameof(AlwaysMachineKeys))]
    public void Classify_AlwaysMachineKey_IsExcludedEvenWhenRegisteredAsSynced(string key)
    {
        // A wrong definition must still not let a bundle turn on the local API, PATH or startup.
        var scope = new SyncScope([key]);

        Assert.Equal(SyncUnitKind.Excluded, scope.Classify("settings:" + key));
    }

    public static TheoryData<string> AlwaysMachineKeys() => new(SyncScope.AlwaysMachineKeys);

    [Theory]
    [InlineData("localApi.port")]
    [InlineData("startup.minimized")]
    [InlineData("sync.lastExport")]
    [InlineData("onboarding.step")]
    [InlineData("updates.channel")]
    public void Classify_AnyKeyOfAMachineFeature_IsExcluded(string key)
    {
        Assert.Equal(SyncUnitKind.Excluded, new SyncScope([key]).Classify("settings:" + key));
    }

    [Fact]
    public void Classify_SecretKeyAlsoListedAsSynced_IsExcluded()
    {
        var scope = new SyncScope(["lyrics.translateKey"], secretSettingKeys: ["lyrics.translateKey"]);

        Assert.Equal(SyncUnitKind.Excluded, scope.Classify("settings:lyrics.translateKey"));
    }

    [Fact]
    public void Classify_MachineKeyAlsoListedAsSynced_IsExcluded()
    {
        var scope = new SyncScope(["clock.style"], ["clock.style"]);

        Assert.Equal(SyncUnitKind.Excluded, scope.Classify("settings:clock.style"));
    }

    [Theory]
    [InlineData("settings:newfeature.x")]
    [InlineData("layout:panelTheme")]
    [InlineData("lists:todo")]
    public void Classify_UnregisteredButWellFormed_IsUnknown(string name)
    {
        Assert.Equal(SyncUnitKind.Unknown, Scope().Classify(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("settings")]
    [InlineData(":x")]
    [InlineData("settings:")]
    [InlineData("Settings:clock.style")]
    [InlineData("settings:clock style")]
    [InlineData("settings:noDot")]
    [InlineData("settings:.style")]
    [InlineData("settings:clock.")]
    [InlineData("layout:widgetSettings:")]
    [InlineData("layout:widgetSettings:Clock")]
    [InlineData("layout:widgetSettings:json-url#")]
    [InlineData("layout:widgetSettings:json-url#.hidden")]
    [InlineData("layout:widgetSettings:a#b#c")]
    [InlineData("layout:widgetSettings:..\\evil")]
    [InlineData("layout:widgetSettings:a:b")]
    // Instance ids the rest of Atap rejects.
    [InlineData("layout:widgetSettings:json-url#NAS")]
    [InlineData("layout:widgetSettings:world-clock#kl_2.b")]
    [InlineData("layout:widgetSettings:json-url#a.")]
    [InlineData("layout:widgetSettings:json-url#-a")]
    [InlineData("layout:widgetSettings:1clock")]
    [InlineData("layout:widgetSettings:-clock")]
    [InlineData("layout:widgetSettings:a")]
    [InlineData("layout:widgetSettings:a23456789012345678901234567890123")]
    [InlineData("layout:widgetSettings:json-url#b23456789012345678901234567890123")]
    public void Classify_MalformedName_IsExcluded(string name)
    {
        Assert.Equal(SyncUnitKind.Excluded, Scope().Classify(name));
    }

    [Fact]
    public void Classify_NameOverTheLengthLimit_IsExcluded()
    {
        var name = "settings:clock." + new string('a', SyncLimits.MaxUnitNameLength);

        Assert.Equal(SyncUnitKind.Excluded, new SyncScope([name["settings:".Length..]]).Classify(name));
    }

    [Fact]
    public void IsMachineField_MatchesTheWidgetTypeOfAnyInstance()
    {
        var scope = Scope();

        Assert.True(scope.IsMachineField("layout:widgetSettings:file-shelf", "folder"));
        Assert.True(scope.IsMachineField("layout:widgetSettings:file-shelf#work", "folder"));
        Assert.False(scope.IsMachineField("layout:widgetSettings:file-shelf", "maxItems"));
        Assert.False(scope.IsMachineField("layout:widgetSettings:clock", "folder"));
        Assert.False(scope.IsMachineField("settings:clock.style", "folder"));
    }

    [Fact]
    public void UnitNames_BuildAndParse()
    {
        Assert.Equal("settings:malaysia.state", SyncUnitNames.Setting("malaysia.state"));
        Assert.Equal("layout:widgetSettings:json-url#nas", SyncUnitNames.WidgetSettings("json-url#nas"));
        Assert.Throws<ArgumentException>(() => SyncUnitNames.Setting("rules"));
        Assert.Throws<ArgumentException>(() => SyncUnitNames.WidgetSettings("json:url"));

        Assert.True(SyncUnitNames.TryGetSettingKey("settings:malaysia.state", out var key));
        Assert.Equal("malaysia.state", key);
        Assert.False(SyncUnitNames.TryGetSettingKey(SyncUnitNames.Rules, out _));
        Assert.True(SyncUnitNames.TryGetInstanceId("layout:widgetSettings:json-url#nas", out var id));
        Assert.Equal("json-url#nas", id);
        Assert.Equal("json-url", SyncUnitNames.TypeIdOf(id));

        Assert.Equal(SyncFamily.Settings, SyncUnitNames.FamilyOf(SyncUnitNames.Rules));
        Assert.Equal(SyncFamily.Layout, SyncUnitNames.FamilyOf("layout:widgetSettings:clock"));
        Assert.Equal(SyncFamily.Other, SyncUnitNames.FamilyOf("lists:todo"));
    }
}
