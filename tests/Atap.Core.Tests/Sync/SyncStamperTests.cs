using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncStamperTests
{
    private static readonly SyncScope Scope = SyncTestData.Scope();

    [Fact]
    public void Stamp_NewUnit_GetsNowAndThisDevice()
    {
        var state = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(("settings:clock.style", "\"bold\"")), Scope, T0);

        var unit = state.Units["settings:clock.style"];
        Assert.Equal(V(T0, DeviceA), unit.Version);
        Assert.Equal(SyncValues.Hash(Json("\"bold\"")), unit.Hash);
        Assert.Equal(T0, state.Units[SyncUnitNames.Rules].Version.Stamp);
    }

    [Fact]
    public void Stamp_UnchangedValues_KeepStampsAndTheSameState()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(("layout:widgetSettings:clock", """{"use24Hour":true,"style":"bold"}""")), Scope, T0);

        // Same value, other property order and spacing.
        var second = SyncStamper.Stamp(first, Documents(("layout:widgetSettings:clock", """{ "style": "bold", "use24Hour": true }""")), Scope, T0.AddHours(1));

        Assert.Same(first, second);
    }

    [Fact]
    public void Stamp_ChangedValue_GetsTheNewTime()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(("settings:clock.style", "\"bold\"")), Scope, T0);

        var second = SyncStamper.Stamp(first, Documents(("settings:clock.style", "\"plain\"")), Scope, T0.AddMinutes(3));

        Assert.Equal(T0.AddMinutes(3), second.Units["settings:clock.style"].Version.Stamp);
        Assert.Equal(T0, second.Units[SyncUnitNames.Rules].Version.Stamp);
    }

    [Fact]
    public void Stamp_ChangeAfterANewerRemoteVersion_StillComesLater()
    {
        // The unit came from a PC whose clock is an hour ahead. An edit here must still win.
        var state = SyncState.Create(GuidA) with
        {
            Units = new Dictionary<string, SyncStateUnit>
            {
                ["settings:clock.style"] = new(V(T0.AddHours(1), DeviceB), SyncValues.Hash(Json("\"bold\""))),
            },
        };

        var stamped = SyncStamper.Stamp(state, Documents(("settings:clock.style", "\"plain\"")), Scope, T0);

        Assert.Equal(V(T0.AddHours(1).AddMilliseconds(1), DeviceA), stamped.Units["settings:clock.style"].Version);
    }

    [Fact]
    public void Stamp_RemovedUnit_IsStampedAsDeleted()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(
            ("layout:widgetSettings:json-url#nas", """{"url":"http://192.168.1.10/api"}"""),
            ("settings:clock.style", "\"bold\"")), Scope, T0);

        var second = SyncStamper.Stamp(first, Documents(), Scope, T0.AddMinutes(1));

        Assert.True(second.Units["layout:widgetSettings:json-url#nas"].IsDeleted);
        Assert.Equal(V(T0.AddMinutes(1), DeviceA), second.Units["layout:widgetSettings:json-url#nas"].Version);
        Assert.True(second.Units["settings:clock.style"].IsDeleted);
    }

    [Fact]
    public void Stamp_MissingFixedUnit_IsNeverDeleted()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents((SyncUnitNames.States, """{"idle":{}}""")), Scope, T0);

        var second = SyncStamper.Stamp(first, Documents(), Scope, T0.AddMinutes(1));

        Assert.Same(first, second);
    }

    [Fact]
    public void Stamp_DocumentNotRead_DeletesNothingFromIt()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(
            ("settings:clock.style", "\"bold\""),
            ("layout:widgetSettings:clock", "{}")), Scope, T0);

        // settings.json failed to load: no rules sentinel and no keys. Layout was read and still has its units.
        var layoutOnly = Units(
            (SyncUnitNames.EnabledWidgets, """["media","clock"]"""),
            ("layout:widgetSettings:clock", "{}"));
        var second = SyncStamper.Stamp(first, layoutOnly, Scope, T0.AddMinutes(1));

        Assert.Same(first, second);
    }

    [Fact]
    public void Stamp_ExcludedAndUnknownUnits_AreNeverStamped()
    {
        var state = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(
            (SyncUnitNames.Placement, """{"anchor":"topCenter"}"""),
            ("settings:localApi.enabled", "true"),
            ("settings:startup.enabled", "true"),
            ("settings:display.monitor", "2"),
            ("settings:unregistered.key", "1")), Scope, T0);

        Assert.Equal([SyncUnitNames.EnabledWidgets, SyncUnitNames.Rules], state.Units.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Stamp_SecretReferenceChange_DoesNotRestamp()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(("layout:widgetSettings:next-meeting", """{"icsUrl":"secret:7f2a","minutesBefore":5}""")), Scope, T0);

        // The user typed the URL again on this PC, so it has a new reference.
        var second = SyncStamper.Stamp(first, Documents(("layout:widgetSettings:next-meeting", """{"icsUrl":"secret:9b1c","minutesBefore":5}""")), Scope, T0.AddMinutes(1));

        Assert.Same(first, second);
    }

    [Fact]
    public void Stamp_MachineFieldChange_DoesNotRestamp()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(("layout:widgetSettings:file-shelf", """{"folder":"C:\\a","maxItems":10}""")), Scope, T0);

        var second = SyncStamper.Stamp(first, Documents(("layout:widgetSettings:file-shelf", """{"folder":"D:\\b","maxItems":10}""")), Scope, T0.AddMinutes(1));

        Assert.Same(first, second);
    }

    [Fact]
    public void Stamp_UnitThatIsASecretReference_StaysOffTheState()
    {
        var scope = new SyncScope(["lyrics.translateKey"]);
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(("settings:lyrics.translateKey", "\"plain-old-key\"")), scope, T0);

        var second = SyncStamper.Stamp(first, Documents(("settings:lyrics.translateKey", "\"secret:abcd\"")), scope, T0.AddMinutes(1));

        Assert.True(first.Units.ContainsKey("settings:lyrics.translateKey"));
        // Not deleted either: other PCs keep their own value.
        Assert.False(second.Units.ContainsKey("settings:lyrics.translateKey"));
    }

    [Fact]
    public void Stamp_KeyThatIsNoLongerSynced_LeavesTheState()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(("settings:clock.style", "\"bold\"")), Scope, T0);

        var second = SyncStamper.Stamp(first, Documents(("settings:clock.style", "\"bold\"")), new SyncScope([], ["clock.style"]), T0.AddMinutes(1));

        Assert.False(second.Units.ContainsKey("settings:clock.style"));
    }

    [Fact]
    public void YieldToOneDrive_ResetsOneDocumentAndKeepsTheOther()
    {
        var first = SyncStamper.Stamp(SyncState.Create(GuidA), Documents(
            ("settings:clock.style", "\"bold\""),
            ("layout:widgetSettings:clock", """{"use24Hour":true}"""),
            (SyncUnitNames.States, """{"idle":{}}""")), Scope, T0);

        // layout.json was corrupt and was replaced by the defaults.
        var reset = Documents(("settings:clock.style", "\"bold\""));
        reset[SyncUnitNames.EnabledWidgets] = Json("""["clock"]""");
        var yielded = SyncStamper.YieldToOneDrive(first, SyncFamily.Layout, reset, Scope);

        Assert.Equal(new SyncStateUnit(V(SyncVersion.InitialStamp, DeviceA), SyncValues.Hash(Json("""["clock"]"""))), yielded.Units[SyncUnitNames.EnabledWidgets]);
        Assert.False(yielded.Units.ContainsKey("layout:widgetSettings:clock"));
        Assert.False(yielded.Units.ContainsKey(SyncUnitNames.States));
        Assert.Equal(first.Units["settings:clock.style"], yielded.Units["settings:clock.style"]);
        Assert.Equal(first.Units[SyncUnitNames.Rules], yielded.Units[SyncUnitNames.Rules]);
        // The defaults are not a new change, and the missing units are not deletions.
        Assert.Same(yielded, SyncStamper.Stamp(yielded, reset, Scope, T0.AddMinutes(1)));
    }

    [Fact]
    public void YieldToOneDrive_UnitsOfNoDocument_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SyncStamper.YieldToOneDrive(SyncState.Create(GuidA), SyncFamily.Other, Documents(), Scope));
    }

    [Fact]
    public void Initialize_KeepThisPc_WinsEveryUnitAndDeletesUnitsOnlyTheBundleHas()
    {
        var existing = Bundle(
            DeviceB,
            ("settings:clock.style", V(T0.AddMinutes(30), DeviceB), "\"plain\""),
            ("layout:widgetSettings:json-url#nas", V(T0.AddMinutes(-10), DeviceB), "{}"),
            (SyncUnitNames.Profiles, V(T0.AddMinutes(-10), DeviceB), "[]"),
            ("settings:malaysia.state", V(T0.AddDays(5), DeviceB), "\"JHR\""));

        var state = SyncStamper.Initialize(GuidA, Documents(("settings:clock.style", "\"bold\"")), Scope, SyncStartChoice.KeepThisPc, existing, T0);

        Assert.Equal(DeviceA, state.DeviceId);
        // Stamped after the bundle's copy, even though that copy is half an hour ahead.
        Assert.Equal(V(T0.AddMinutes(30).AddMilliseconds(1), DeviceA), state.Units["settings:clock.style"].Version);
        Assert.Equal(V(T0, DeviceA), state.Units[SyncUnitNames.Rules].Version);
        Assert.True(state.Units["layout:widgetSettings:json-url#nas"].IsDeleted);
        // A fixed unit is never deleted.
        Assert.False(state.Units.ContainsKey(SyncUnitNames.Profiles));
        // A unit stamped days ahead is still beaten, so this PC really wins.
        Assert.True(state.Units["settings:malaysia.state"].IsDeleted);
        Assert.Equal(V(T0.AddDays(5).AddMilliseconds(1), DeviceA), state.Units["settings:malaysia.state"].Version);
    }

    [Fact]
    public void Initialize_KeepThisPc_AlsoBeatsNewerStampsInConflictCopies()
    {
        var main = Bundle(DeviceB, ("settings:clock.style", V(T0.AddMinutes(30), DeviceB), "\"plain\""));
        var copy = Bundle(
            DeviceB,
            ("settings:clock.style", V(T0.AddHours(2), DeviceB), "\"compact\""),
            ("layout:widgetSettings:json-url#nas", V(T0.AddHours(1), DeviceB), "{}"));
        var local = Documents(("settings:clock.style", "\"bold\""));

        var state = SyncStamper.Initialize(GuidA, local, Scope, SyncStartChoice.KeepThisPc, main, T0, [copy]);

        Assert.Equal(V(T0.AddHours(2).AddMilliseconds(1), DeviceA), state.Units["settings:clock.style"].Version);
        Assert.True(state.Units["layout:widgetSettings:json-url#nas"].IsDeleted);
        Assert.Equal(V(T0.AddHours(1).AddMilliseconds(1), DeviceA), state.Units["layout:widgetSettings:json-url#nas"].Version);

        // The first merge reads the same copy, and this PC's settings still win.
        var result = SyncMergerTests.Merge(state, local, main, [copy]);

        Assert.Empty(result.Changes);
        Assert.True(SameJson("\"bold\"", result.Bundle.Units["settings:clock.style"].Value));
        Assert.True(result.Bundle.Units["layout:widgetSettings:json-url#nas"].IsDeleted);
    }

    [Fact]
    public void Initialize_KeepThisPc_TakesAFixedUnitOnlyTheBundleHas()
    {
        var main = Bundle(DeviceB, (SyncUnitNames.Profiles, V(T0.AddMinutes(-10), DeviceB), """[{"id":"gaming"}]"""));
        var local = Documents();

        var state = SyncStamper.Initialize(GuidA, local, Scope, SyncStartChoice.KeepThisPc, main, T0);
        var result = SyncMergerTests.Merge(state, local, main);

        // A fixed unit is never deleted, so there is nothing of this PC's to keep.
        Assert.False(state.Units.ContainsKey(SyncUnitNames.Profiles));
        var change = Assert.Single(result.Changes);
        Assert.Equal(SyncUnitNames.Profiles, change.UnitName);
        Assert.True(SameJson("""[{"id":"gaming"}]""", change.Value));
    }

    [Fact]
    public void Initialize_UseOneDrive_StampsLocalUnitsSoAnyRealStampBeatsThem()
    {
        var state = SyncStamper.Initialize(GuidA, Documents(("settings:clock.style", "\"bold\"")), Scope, SyncStartChoice.UseOneDrive, null, T0);

        Assert.All(state.Units.Values, unit => Assert.Equal(V(SyncVersion.InitialStamp, DeviceA), unit.Version));
        Assert.Equal(3, state.Units.Count);
    }
}
