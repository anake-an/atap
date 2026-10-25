using System.Text;
using System.Text.Json;
using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncMergerTests
{
    private static readonly SyncScope Scope = SyncTestData.Scope();

    private static readonly DateTimeOffset Earlier = T0.AddHours(-1);

    [Fact]
    public void Merge_RemoteNewer_IsApplied()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("settings:clock.style", V(T0, DeviceB), "\"plain\""));

        var result = Merge(state, local, main);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new SyncChange("settings:clock.style", SyncChangeKind.Set, change.Value), change);
        Assert.True(SameJson("\"plain\"", change.Value));
        Assert.Equal(V(T0, DeviceB), result.State.Units["settings:clock.style"].Version);
        Assert.Equal(V(T0, DeviceB), result.Bundle.Units["settings:clock.style"].Version);
        Assert.True(result.StateChanged);
    }

    [Fact]
    public void Merge_RemoteStampedFarInTheFuture_IsIgnored_SoALowerDeviceCanStillChangeIt()
    {
        // At the latest stamp, the unit would beat every later change, and a tie goes to the higher device.
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("settings:clock.style", V(SyncVersion.LatestStamp, DeviceB), "\"plain\""));

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.Contains(result.Skipped, s => s.UnitName == "settings:clock.style" && s.Reason == SyncSkipReason.FutureStamp);
        var written = SyncBundleReader.Read(result.BundleJson).Bundle!;
        Assert.Equal(V(Earlier, DeviceA), written.Units["settings:clock.style"].Version);
    }

    [Fact]
    public void Merge_LocalNewer_IsKeptAndExported()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("settings:clock.style", V(Earlier.AddMinutes(-1), DeviceB), "\"plain\""));

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        var written = SyncBundleReader.Read(result.BundleJson).Bundle!;
        Assert.Equal(DeviceA, written.DeviceId);
        Assert.Equal(V(Earlier, DeviceA), written.Units["settings:clock.style"].Version);
        Assert.True(SameJson("\"bold\"", written.Units["settings:clock.style"].Value));
    }

    [Fact]
    public void Merge_SameStamp_HigherRemoteDeviceWins()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, T0);
        var main = Bundle(DeviceB, ("settings:clock.style", V(T0, DeviceB), "\"plain\""));

        var result = Merge(state, local, main);

        Assert.True(SameJson("\"plain\"", Assert.Single(result.Changes).Value));
        Assert.Equal(V(T0, DeviceB), result.State.Units["settings:clock.style"].Version);
    }

    [Fact]
    public void Merge_SameStamp_HigherLocalDeviceWins()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidB, local, T0);
        var main = Bundle(DeviceA, ("settings:clock.style", V(T0, DeviceA), "\"plain\""));

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.Equal(V(T0, DeviceB), result.Bundle.Units["settings:clock.style"].Version);
        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
    }

    [Fact]
    public void Merge_AppliedUnit_DoesNotBounceBack()
    {
        // PC A's bundle. PC B starts with "Use the settings from OneDrive".
        var bundleA = Bundle(
            DeviceA,
            (SyncUnitNames.Rules, V(T0, DeviceA), "[]"),
            (SyncUnitNames.EnabledWidgets, V(T0, DeviceA), """["media","clock","battery"]"""),
            ("settings:clock.style", V(T0, DeviceA), "\"bold\""));
        var localB = Documents(("settings:clock.style", "\"plain\""));
        var stateB = SyncStamper.Initialize(GuidB, localB, Scope, SyncStartChoice.UseOneDrive, bundleA, T0.AddMinutes(1));

        var first = Merge(stateB, localB, bundleA, now: T0.AddMinutes(1));
        Assert.Equal([SyncUnitNames.EnabledWidgets, "settings:clock.style"], first.Changes.Select(change => change.UnitName));
        Assert.Equal(SyncBundleOutcome.Unchanged, first.BundleOutcome);

        // The App applies the changes. The store write stamps again: nothing is new.
        var appliedB = Apply(localB, first.Changes);
        var restamped = SyncStamper.Stamp(first.State, appliedB, Scope, T0.AddMinutes(2));
        Assert.Same(first.State, restamped);

        var second = Merge(restamped, appliedB, bundleA, now: T0.AddMinutes(3));
        Assert.Empty(second.Changes);
        Assert.Equal(SyncBundleOutcome.Unchanged, second.BundleOutcome);
        Assert.False(second.StateChanged);

        // B edits something else and writes. The units it took from A keep A's version.
        appliedB["settings:malaysia.state"] = Json("\"PRK\"");
        var third = Merge(second.State, appliedB, bundleA, now: T0.AddMinutes(4));
        Assert.Equal(SyncBundleOutcome.Write, third.BundleOutcome);
        var bundleB = SyncBundleReader.Read(third.BundleJson).Bundle!;
        Assert.Equal(V(T0, DeviceA), bundleB.Units["settings:clock.style"].Version);
        Assert.Equal(V(T0.AddMinutes(4), DeviceB), bundleB.Units["settings:malaysia.state"].Version);

        // A reads B's bundle: only the new unit is applied, nothing comes back to A as a change of its own.
        var localA = Documents(("settings:clock.style", "\"bold\""));
        localA[SyncUnitNames.EnabledWidgets] = Json("""["media","clock","battery"]""");
        var stateA = new SyncState(DeviceA, bundleA.Units.ToDictionary(
            pair => pair.Key,
            pair => new SyncStateUnit(pair.Value.Version, SyncValues.Hash(pair.Value.Value!.Value))));
        var atA = Merge(stateA, localA, bundleB, now: T0.AddMinutes(5));
        Assert.Equal(["settings:malaysia.state"], atA.Changes.Select(change => change.UnitName));
        Assert.Equal(SyncBundleOutcome.Unchanged, atA.BundleOutcome);
    }

    [Fact]
    public void Merge_ConflictCopies_AreMergedLikeTheMainFile()
    {
        var local = Documents(("settings:clock.style", "\"bold\""), ("settings:malaysia.state", "\"SGR\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = SyncBundleReader.Read(SyncBundleWriter.Write(new SyncBundle(DeviceA, Exported(state, local)))).Bundle!;
        var copy = Bundle(
            DeviceB,
            ("settings:clock.style", V(Earlier.AddMinutes(-30), DeviceB), "\"plain\""),
            ("settings:malaysia.state", V(T0, DeviceB), "\"JHR\""),
            ("settings:language.choice", V(T0, DeviceB), "\"ms\""));

        var result = Merge(state, local, main, copies: [copy]);

        Assert.Equal(["settings:language.choice", "settings:malaysia.state"], result.Changes.Select(change => change.UnitName));
        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        var written = SyncBundleReader.Read(result.BundleJson).Bundle!;
        Assert.Equal(V(Earlier, DeviceA), written.Units["settings:clock.style"].Version);
        Assert.Equal(V(T0, DeviceB), written.Units["settings:malaysia.state"].Version);
        Assert.Equal(V(T0, DeviceB), written.Units["settings:language.choice"].Version);
    }

    [Fact]
    public void Merge_ConflictCopyWithNothingNewer_LeavesTheBundleUnchanged()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, T0);
        var main = new SyncBundle(DeviceA, Exported(state, local));
        var copy = Bundle(DeviceB, ("settings:clock.style", V(Earlier, DeviceB), "\"plain\""));

        var result = Merge(state, local, main, copies: [copy]);

        Assert.Empty(result.Changes);
        Assert.Equal(SyncBundleOutcome.Unchanged, result.BundleOutcome);
        Assert.Null(result.BundleJson);
    }

    [Fact]
    public void Merge_ExcludedRemoteUnits_AreNeverImportedOrWritten()
    {
        // Even with a definition that wrongly marks the local API as synced.
        var scope = new SyncScope(["clock.style", "localApi.enabled"], ["display.monitor"]);
        var local = Documents(("settings:localApi.enabled", "false"));
        var state = SyncStamper.Stamp(SyncState.Create(GuidA), local, scope, Earlier);
        var main = SyncBundleReader.Read($$"""
            { "format": 1, "deviceId": "{{DeviceB}}", "units": {
                "settings:localApi.enabled": { "stamp": "2026-10-25T08:00:00.000Z", "value": true },
                "settings:localApi.onPath": { "stamp": "2026-10-25T08:00:00.000Z", "value": true },
                "settings:startup.enabled": { "stamp": "2026-10-25T08:00:00.000Z", "value": true },
                "settings:sync.folder": { "stamp": "2026-10-25T08:00:00.000Z", "value": "\\\\server\\share" },
                "settings:display.monitor": { "stamp": "2026-10-25T08:00:00.000Z", "value": 3 },
                "layout:placement": { "stamp": "2026-10-25T08:00:00.000Z", "value": { "anchor": "leftEdge", "offset": 400 } } } }
            """).Bundle!;

        var result = Merge(state, local, main, scope: scope);

        Assert.Empty(result.Changes);
        Assert.Equal(6, result.Skipped.Count(skip => skip.Reason == SyncSkipReason.Excluded));
        Assert.DoesNotContain(result.Bundle.Units.Keys, name => name.Contains("localApi") || name.Contains("startup") || name.Contains("sync.")
            || name.Contains("display") || name.Contains("placement"));
        Assert.DoesNotContain(result.State.Units.Keys, name => name.Contains("localApi"));
        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        Assert.DoesNotContain("localApi", result.BundleJson);
        Assert.DoesNotContain("placement", result.BundleJson);
    }

    [Fact]
    public void Merge_ExcludedLocalUnits_AreNeverExported()
    {
        var local = Documents(
            (SyncUnitNames.Placement, """{"anchor":"topCenter","offset":0}"""),
            ("settings:localApi.enabled", "true"),
            ("settings:startup.enabled", "true"),
            ("settings:display.monitor", "2"),
            ("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);

        var result = Merge(state, local, main: null);

        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        Assert.Equal(
            [SyncUnitNames.EnabledWidgets, "settings:clock.style", SyncUnitNames.Rules],
            SyncBundleReader.Read(result.BundleJson).Bundle!.Units.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Merge_ExcludedUnitsInTheMainFile_DoNotForceAWrite()
    {
        // Otherwise a PC that still syncs such a unit and one that does not would rewrite the file in turn forever.
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var units = Exported(state, local);
        units["settings:display.monitor"] = new SyncBundleUnit(V(T0, DeviceB), Json("2"));
        var main = new SyncBundle(DeviceB, units);

        var result = Merge(state, local, main);

        Assert.Equal(SyncBundleOutcome.Unchanged, result.BundleOutcome);
    }

    [Fact]
    public void Merge_UnknownUnits_AreCarriedButNeverApplied()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(
            DeviceB,
            ("settings:newfeature.mode", V(T0, DeviceB), "\"fast\""),
            ("lists:todo", V(T0, DeviceB), """[{"text":"Hantar tugasan"}]"""));

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.Equal(2, result.Skipped.Count(skip => skip.Reason == SyncSkipReason.Unknown));
        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        var written = SyncBundleReader.Read(result.BundleJson).Bundle!;
        Assert.Equal(V(T0, DeviceB), written.Units["settings:newfeature.mode"].Version);
        Assert.True(SameJson("""[{"text":"Hantar tugasan"}]""", written.Units["lists:todo"].Value));
        Assert.DoesNotContain("lists:todo", result.State.Units.Keys);
    }

    [Fact]
    public void Merge_RemoteStampedFarAhead_IsAppliedAndALaterLocalEditStillWins()
    {
        // PC B's clock runs two days ahead. Skipping its stamp would make the PCs rewrite the file in turn forever.
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("settings:clock.style", V(T0.AddDays(2), DeviceB), "\"plain\""));

        var result = Merge(state, local, main);

        Assert.True(SameJson("\"plain\"", Assert.Single(result.Changes).Value));
        Assert.Equal(V(T0.AddDays(2), DeviceB), result.State.Units["settings:clock.style"].Version);
        Assert.Equal(V(T0.AddDays(2), DeviceB), result.Bundle.Units["settings:clock.style"].Version);

        var edited = Apply(local, result.Changes);
        edited["settings:clock.style"] = Json("\"compact\"");
        var next = Merge(result.State, edited, main, now: T0.AddMinutes(1));

        Assert.Empty(next.Changes);
        Assert.Equal(V(T0.AddDays(2).AddMilliseconds(1), DeviceA), next.Bundle.Units["settings:clock.style"].Version);
        Assert.True(SameJson("\"compact\"", next.Bundle.Units["settings:clock.style"].Value));
    }

    [Fact]
    public void Merge_RemoteStampedSlightlyAhead_IsApplied()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("settings:clock.style", V(T0.AddHours(3), DeviceB), "\"plain\""));

        var result = Merge(state, local, main);

        Assert.True(SameJson("\"plain\"", Assert.Single(result.Changes).Value));
    }

    [Fact]
    public void Merge_RejectedValue_IsNotAppliedAndTheNewerRemoteStaysInTheBundle()
    {
        var local = Documents(("settings:malaysia.state", "\"SGR\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("settings:malaysia.state", V(T0, DeviceB), "\"XYZ\""));
        SyncUnitValidator validator = (name, value) =>
            name == "settings:malaysia.state" && value.GetString() is not ("SGR" or "JHR" or "KUL")
                ? SyncValidation.Reject("not a state code")
                : SyncValidation.Accept(value);

        var result = Merge(state, local, main, validator: validator);

        Assert.Empty(result.Changes);
        Assert.Contains(new SyncSkip("settings:malaysia.state", SyncSkipReason.Rejected, "not a state code"), result.Skipped);
        Assert.Equal(state.Units["settings:malaysia.state"], result.State.Units["settings:malaysia.state"]);
        Assert.Equal(V(T0, DeviceB), result.Bundle.Units["settings:malaysia.state"].Version);
    }

    [Fact]
    public void Merge_NormalizedValue_IsAppliedAndTrackedAsNormalized()
    {
        var local = Documents();
        local[SyncUnitNames.EnabledWidgets] = Json("""["clock"]""");
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, (SyncUnitNames.EnabledWidgets, V(T0, DeviceB), """["media","","clock","media"]"""));
        SyncUnitValidator normalize = (name, value) => name == SyncUnitNames.EnabledWidgets
            ? SyncValidation.Accept(Json(JsonSerializer.Serialize(value.EnumerateArray().Select(item => item.GetString()).Where(id => id != "").Distinct())))
            : SyncValidation.Accept(value);

        var result = Merge(state, local, main, validator: normalize);

        var change = Assert.Single(result.Changes);
        Assert.True(SameJson("""["media","clock"]""", change.Value));
        // The store now holds the normalized list, which hashes the same as before: no new stamp, no bounce.
        Assert.Same(result.State, SyncStamper.Stamp(result.State, Apply(local, result.Changes), Scope, T0.AddMinutes(1)));
    }

    [Theory]
    [InlineData(SyncUnitNames.EnabledWidgets, """{"media":true}""")]
    [InlineData(SyncUnitNames.EnabledWidgets, """["media",3]""")]
    [InlineData(SyncUnitNames.States, "[]")]
    [InlineData(SyncUnitNames.Rules, "\"none\"")]
    [InlineData(SyncUnitNames.Profiles, """[1]""")]
    [InlineData("layout:widgetSettings:clock", "true")]
    [InlineData("settings:clock.style", "null")]
    public void Merge_ValueOfTheWrongKind_IsNotApplied(string name, string json)
    {
        var local = Documents();
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, (name, V(T0, DeviceB), json));

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.Contains(new SyncSkip(name, SyncSkipReason.WrongShape), result.Skipped);
    }

    [Fact]
    public void Merge_RemoteNullSettingValue_IsNotAppliedAndDoesNotBounce()
    {
        // A hand-edited bundle. The store would read null as unset, and the next stamp would export a deletion.
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = SyncBundleReader.Read($$"""
            { "format": 1, "deviceId": "{{DeviceB}}", "units": {
                "settings:clock.style": { "stamp": "2026-10-25T08:00:00.000Z", "value": null } } }
            """).Bundle!;

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.Contains(new SyncSkip("settings:clock.style", SyncSkipReason.WrongShape), result.Skipped);
        Assert.Equal(state.Units["settings:clock.style"], result.State.Units["settings:clock.style"]);
        Assert.Same(result.State, SyncStamper.Stamp(result.State, local, Scope, T0.AddMinutes(1)));
    }

    [Fact]
    public void Merge_RemoteSecretReferences_AreDroppedAndLocalOnesKept()
    {
        var local = Documents(
            ("layout:widgetSettings:next-meeting", """{"icsUrl":"secret:local1","minutesBefore":5}"""),
            ("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(
            DeviceB,
            ("layout:widgetSettings:next-meeting", V(T0, DeviceB), """{"icsUrl":"secret:theirs","minutesBefore":10}"""),
            ("layout:widgetSettings:json-url#nas", V(T0, DeviceB), """{"url":"https://evil.example/","headers":{"Authorization":"secret:local1"}}"""),
            ("settings:clock.style", V(T0, DeviceB), "\"secret:local1\""));

        var result = Merge(state, local, main);

        var changes = result.Changes.ToDictionary(change => change.UnitName);
        Assert.True(SameJson("""{"minutesBefore":10,"icsUrl":"secret:local1"}""", changes["layout:widgetSettings:next-meeting"].Value));
        // A new instance cannot borrow a secret of this PC.
        Assert.True(SameJson("""{"url":"https://evil.example/","headers":{}}""", changes["layout:widgetSettings:json-url#nas"].Value));
        Assert.False(changes.ContainsKey("settings:clock.style"));
        Assert.Contains(new SyncSkip("settings:clock.style", SyncSkipReason.SecretValue), result.Skipped);
    }

    [Fact]
    public void Merge_ExportedValues_NeverCarrySecretReferences()
    {
        var local = Documents(("layout:widgetSettings:next-meeting", """{"icsUrl":"secret:7f2a","minutesBefore":5}"""));
        var state = Stamped(GuidA, local, Earlier);

        var result = Merge(state, local, main: null);

        Assert.DoesNotContain("secret:", result.BundleJson);
        Assert.True(SameJson("""{"minutesBefore":5}""", result.Bundle.Units["layout:widgetSettings:next-meeting"].Value));
    }

    [Fact]
    public void Merge_MachineWidgetFields_AreNeverExportedOrImported()
    {
        var local = Documents(("layout:widgetSettings:file-shelf", """{"folder":"C:\\Users\\aniq\\Shelf","maxItems":10}"""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("layout:widgetSettings:file-shelf", V(T0, DeviceB), """{"folder":"\\\\server\\share","maxItems":20}"""));

        var result = Merge(state, local, main);

        Assert.True(SameJson("""{"maxItems":20,"folder":"C:\\Users\\aniq\\Shelf"}""", Assert.Single(result.Changes).Value));

        var back = Merge(result.State, Apply(local, result.Changes), main: null, now: T0.AddMinutes(1));
        Assert.True(SameJson("""{"maxItems":20}""", back.Bundle.Units["layout:widgetSettings:file-shelf"].Value));
    }

    [Fact]
    public void Merge_RemoteDeletion_RemovesTheUnit()
    {
        var local = Documents(("layout:widgetSettings:json-url#nas", """{"url":"http://192.168.1.10/"}"""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("layout:widgetSettings:json-url#nas", V(T0, DeviceB), null));

        var result = Merge(state, local, main);

        Assert.Equal(new SyncChange("layout:widgetSettings:json-url#nas", SyncChangeKind.Remove, null), Assert.Single(result.Changes));
        Assert.True(result.State.Units["layout:widgetSettings:json-url#nas"].IsDeleted);
        Assert.Same(result.State, SyncStamper.Stamp(result.State, Apply(local, result.Changes), Scope, T0.AddMinutes(1)));
    }

    [Fact]
    public void Merge_RemoteDeletionOfAnAbsentUnit_IsOnlyRemembered()
    {
        var local = Documents();
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("settings:clock.style", V(T0, DeviceB), null));

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.True(result.State.Units["settings:clock.style"].IsDeleted);
    }

    [Fact]
    public void Merge_DeletionOfAFixedUnit_IsRefused()
    {
        var local = Documents();
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, (SyncUnitNames.EnabledWidgets, V(T0, DeviceB), null));

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.Contains(new SyncSkip(SyncUnitNames.EnabledWidgets, SyncSkipReason.FixedUnitDeleted), result.Skipped);
    }

    [Fact]
    public void Merge_DocumentNotRead_GetsNoChanges()
    {
        // settings.json failed to load, so there is no rules sentinel.
        var local = Units((SyncUnitNames.EnabledWidgets, """["clock"]"""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("settings:clock.style", V(T0, DeviceB), "\"plain\""), (SyncUnitNames.Rules, V(T0, DeviceB), "[]"));

        var result = Merge(state, local, main);

        Assert.Empty(result.Changes);
        Assert.Contains(new SyncSkip("settings:clock.style", SyncSkipReason.DocumentMissing), result.Skipped);
        Assert.Contains(new SyncSkip(SyncUnitNames.Rules, SyncSkipReason.DocumentMissing), result.Skipped);
    }

    [Fact]
    public void Merge_AfterYieldToOneDrive_TheResetDocumentIsTakenFromTheBundle()
    {
        var before = Documents(("layout:widgetSettings:clock", """{"use24Hour":true}"""), ("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, before, Earlier);
        var main = new SyncBundle(DeviceA, Exported(state, before));

        // layout.json was corrupt and was replaced by the defaults.
        var reset = Documents(("settings:clock.style", "\"bold\""));
        reset[SyncUnitNames.EnabledWidgets] = Json("""["clock"]""");
        var yielded = SyncStamper.YieldToOneDrive(state, SyncFamily.Layout, reset, Scope);

        var result = Merge(yielded, reset, main);

        Assert.Equal([SyncUnitNames.EnabledWidgets, "layout:widgetSettings:clock"], result.Changes.Select(change => change.UnitName));
        Assert.True(SameJson("""["media","clock"]""", result.Changes[0].Value));
        Assert.Equal(SyncBundleOutcome.Unchanged, result.BundleOutcome);
    }

    [Fact]
    public void Merge_LocalSecretValuedUnit_StaysOnThisPc()
    {
        var scope = new SyncScope(["lyrics.translateKey"]);
        var local = Documents(("settings:lyrics.translateKey", "\"secret:abcd\""));
        var state = SyncStamper.Stamp(SyncState.Create(GuidA), local, scope, Earlier);
        var main = Bundle(DeviceB, ("settings:lyrics.translateKey", V(T0, DeviceB), "\"typed-by-hand\""));

        var result = Merge(state, local, main, scope: scope);

        Assert.Empty(result.Changes);
        Assert.Contains(new SyncSkip("settings:lyrics.translateKey", SyncSkipReason.LocalOnly), result.Skipped);
    }

    [Fact]
    public void Merge_SecretKeyWithAPlainValue_IsNeverExportedOrImported()
    {
        // The key is also registered as Synced. Secret still wins.
        var scope = new SyncScope(["clock.style", "lyrics.translateKey"], secretSettingKeys: ["lyrics.translateKey"]);
        var local = Documents(("settings:lyrics.translateKey", "\"typed-by-hand\""), ("settings:clock.style", "\"bold\""));
        var state = SyncStamper.Stamp(SyncState.Create(GuidA), local, scope, Earlier);
        var main = Bundle(DeviceB, ("settings:lyrics.translateKey", V(T0, DeviceB), "\"from-another-pc\""));

        var result = Merge(state, local, main, scope: scope);

        Assert.Empty(result.Changes);
        Assert.Contains(new SyncSkip("settings:lyrics.translateKey", SyncSkipReason.Excluded), result.Skipped);
        Assert.DoesNotContain("settings:lyrics.translateKey", result.State.Units.Keys);
        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        Assert.DoesNotContain("translateKey", result.BundleJson);
    }

    [Fact]
    public void Merge_SecretWidgetFieldWithAPlainValue_IsNeverExportedOrImported()
    {
        var scope = new SyncScope(["clock.style"], secretWidgetSettings: [("next-meeting", "icsUrl")]);
        var local = Documents(("layout:widgetSettings:next-meeting", """{"icsUrl":"https://calendar.example/private.ics","minutesBefore":5}"""));
        var state = SyncStamper.Stamp(SyncState.Create(GuidA), local, scope, Earlier);
        var main = Bundle(DeviceB, ("layout:widgetSettings:next-meeting", V(T0, DeviceB), """{"icsUrl":"https://evil.example/x.ics","minutesBefore":10}"""));

        var result = Merge(state, local, main, scope: scope);

        Assert.True(SameJson("""{"minutesBefore":10,"icsUrl":"https://calendar.example/private.ics"}""", Assert.Single(result.Changes).Value));
        Assert.True(scope.IsSecretField("layout:widgetSettings:next-meeting#work", "icsUrl"));
        Assert.False(scope.IsMachineField("layout:widgetSettings:next-meeting", "icsUrl"));

        var back = Merge(result.State, Apply(local, result.Changes), main: null, now: T0.AddMinutes(1), scope: scope);
        Assert.True(SameJson("""{"minutesBefore":10}""", back.Bundle.Units["layout:widgetSettings:next-meeting"].Value));
        Assert.DoesNotContain("private.ics", back.BundleJson);
    }

    [Fact]
    public void Merge_MainFileThatMustNotBeReplaced_IsBlocked()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);

        var result = Merge(state, local, main: null, mainCanBeReplaced: false);

        Assert.Equal(SyncBundleOutcome.Blocked, result.BundleOutcome);
        Assert.Null(result.BundleJson);
    }

    [Fact]
    public void Merge_UnitAtTheInitialStamp_IsWrittenAtTheTimeOfThePass()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = SyncStamper.Initialize(GuidA, local, Scope, SyncStartChoice.UseOneDrive, null, Earlier);

        var result = Merge(state, local, main: null, now: T0);

        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        var written = SyncBundleReader.Read(result.BundleJson).Bundle!;
        Assert.Equal(3, written.Units.Count);
        Assert.All(written.Units.Values, unit => Assert.Equal(V(T0, DeviceA), unit.Version));
        // The state records the written version with the same hash, so nothing is stamped again.
        Assert.All(result.State.Units, pair => Assert.Equal(new SyncStateUnit(V(T0, DeviceA), state.Units[pair.Key].Hash), pair.Value));
        Assert.Equal(written.Units.Keys.Order(), result.Bundle.Units.Keys.Order());
        Assert.All(result.Bundle.Units.Values, unit => Assert.Equal(V(T0, DeviceA), unit.Version));
        Assert.True(result.StateChanged);
        Assert.Same(result.State, SyncStamper.Stamp(result.State, local, Scope, T0.AddMinutes(1)));
    }

    [Fact]
    public void Merge_UnitAtTheInitialStamp_KeepsItWhenTheBundleIsNotWritten()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = SyncStamper.Initialize(GuidA, local, Scope, SyncStartChoice.UseOneDrive, null, Earlier);

        var blocked = Merge(state, local, main: null, now: T0, mainCanBeReplaced: false);

        Assert.Equal(SyncBundleOutcome.Blocked, blocked.BundleOutcome);
        Assert.False(blocked.StateChanged);
        Assert.All(blocked.State.Units.Values, unit => Assert.Equal(V(SyncVersion.InitialStamp, DeviceA), unit.Version));
        Assert.All(blocked.Bundle.Units.Values, unit => Assert.Equal(V(SyncVersion.InitialStamp, DeviceA), unit.Version));

        // Another PC writes the bundle meanwhile. Its stamps still win over this PC's units.
        var main = Bundle(DeviceB, ("settings:clock.style", V(Earlier, DeviceB), "\"plain\""));
        var later = Merge(blocked.State, local, main, now: T0.AddMinutes(1));

        Assert.True(SameJson("\"plain\"", Assert.Single(later.Changes).Value));
        Assert.Equal(V(Earlier, DeviceB), later.State.Units["settings:clock.style"].Version);
        Assert.Equal(V(T0.AddMinutes(1), DeviceA), later.State.Units[SyncUnitNames.Rules].Version);
    }

    [Fact]
    public void Merge_AfterYieldToOneDrive_AUnitTheBundleLacksIsWrittenAtTheTimeOfThePass()
    {
        var before = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, before, Earlier);
        var main = new SyncBundle(DeviceA, Exported(state, before));

        // layout.json was reset, and its defaults have a unit the bundle lacks.
        var reset = Documents(("settings:clock.style", "\"bold\""), ("layout:widgetSettings:clock", """{"use24Hour":false}"""));
        var yielded = SyncStamper.YieldToOneDrive(state, SyncFamily.Layout, reset, Scope);

        var result = Merge(yielded, reset, main, now: T0);

        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        var written = SyncBundleReader.Read(result.BundleJson).Bundle!;
        Assert.Equal(V(T0, DeviceA), written.Units["layout:widgetSettings:clock"].Version);
        Assert.Equal(V(T0, DeviceA), result.State.Units["layout:widgetSettings:clock"].Version);
        // Units the bundle has are taken from it as before.
        Assert.Equal(V(Earlier, DeviceA), written.Units[SyncUnitNames.EnabledWidgets].Version);
        Assert.Equal(V(Earlier, DeviceA), result.State.Units[SyncUnitNames.EnabledWidgets].Version);
        Assert.DoesNotContain("1970-01-01", result.BundleJson);
    }

    [Fact]
    public void Merge_OverTheSizeLimit_DropsCarriedUnitsFirst()
    {
        var local = Documents(("settings:clock.style", "\"" + new string('b', 60_000) + "\""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(DeviceB, ("lists:todo", V(T0, DeviceB), "\"" + new string('t', 1_000_000) + "\""));
        Assert.True(SyncBundleWriter.TryWrite(main, out _));

        var result = Merge(state, local, main);

        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        Assert.DoesNotContain("lists:todo", result.BundleJson);
        Assert.Contains("settings:clock.style", result.BundleJson);
    }

    [Fact]
    public void Merge_TooLargeEvenWithoutCarriedUnits_WritesNothing()
    {
        var local = Documents(("settings:clock.style", "\"" + new string('b', SyncLimits.MaxBundleBytes) + "\""));
        var state = Stamped(GuidA, local, Earlier);

        var result = Merge(state, local, main: null);

        Assert.Equal(SyncBundleOutcome.TooLarge, result.BundleOutcome);
        Assert.Null(result.BundleJson);
    }

    [Fact]
    public void Merge_LocalValueTooDeepForABundle_IsNotExported()
    {
        var local = Documents(("settings:clock.style", new string('[', 30) + new string(']', 30)));
        var state = Stamped(GuidA, local, Earlier);

        var result = Merge(state, local, main: null);

        Assert.Contains(new SyncSkip("settings:clock.style", SyncSkipReason.TooDeep), result.Skipped);
        Assert.False(result.Bundle.Units.ContainsKey("settings:clock.style"));
        Assert.Equal(SyncBundleReadOutcome.Loaded, SyncBundleReader.Read(result.BundleJson).Outcome);
    }

    [Fact]
    public void WithoutChangesTo_KeepsTheLocalVersionSoTheChangeIsOfferedAgain()
    {
        var local = Documents(("settings:clock.style", "\"bold\""), ("layout:widgetSettings:clock", """{"use24Hour":true}"""));
        var state = Stamped(GuidA, local, Earlier);
        var main = Bundle(
            DeviceB,
            ("settings:clock.style", V(T0, DeviceB), "\"plain\""),
            ("layout:widgetSettings:clock", V(T0, DeviceB), """{"use24Hour":false}"""));
        var result = Merge(state, local, main);

        // layout.json is read-only this session, so only the settings change was applied.
        var kept = result.WithoutChangesTo(["layout:widgetSettings:clock"]);
        var applied = Apply(local, result.Changes.Where(change => change.UnitName.StartsWith("settings:", StringComparison.Ordinal)));

        Assert.Equal(state.Units["layout:widgetSettings:clock"], kept.Units["layout:widgetSettings:clock"]);
        Assert.Equal(V(T0, DeviceB), kept.Units["settings:clock.style"].Version);
        var again = Merge(kept, applied, main, now: T0.AddMinutes(1));
        Assert.Equal(["layout:widgetSettings:clock"], again.Changes.Select(change => change.UnitName));
    }

    [Fact]
    public void Merge_NoMainFile_WritesOnlyWhenThereIsSomethingToWrite()
    {
        var state = SyncState.Create(GuidA);

        Assert.Equal(SyncBundleOutcome.Unchanged, Merge(state, Units(), main: null).BundleOutcome);
        Assert.Equal(SyncBundleOutcome.Write, Merge(state, Documents(), main: null).BundleOutcome);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("stamp")]
    [InlineData("device")]
    [InlineData("deviceId")]
    [InlineData("value")]
    public void Merge_AfterASyncFileSavedAsAnsi_DoesNotThrowAndReplacesTheFile(string where)
    {
        var local = Documents(("layout:widgetSettings:json-url#nas", """{"title":"NAS"}"""));
        var state = Stamped(GuidA, local, Earlier);
        var read = SyncBundleReader.Read(SyncBundleReaderTests.AnsiBundle(where));

        var result = Merge(state, local, read.Bundle, mainCanBeReplaced: read.CanBeReplaced);

        Assert.Empty(result.Changes);
        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        var written = SyncBundleReader.Read(Encoding.UTF8.GetBytes(result.BundleJson!));
        Assert.Equal(SyncBundleReadOutcome.Loaded, written.Outcome);
        Assert.True(SameJson("""{"title":"NAS"}""", written.Bundle!.Units["layout:widgetSettings:json-url#nas"].Value));
    }

    [Fact]
    public void Merge_AfterASyncFileWithAnEscapedLoneSurrogate_DoesNotThrowAndReplacesTheFile()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        var read = SyncBundleReader.Read($$"""
            { "format": 1, "deviceId": "{{DeviceB}}", "units": { "settings:clock.style": { "stamp": "{{SyncVersion.FormatStamp(T0)}}", "value": "secret:\uD800" } } }
            """);

        var result = Merge(state, local, read.Bundle, mainCanBeReplaced: read.CanBeReplaced);

        Assert.Empty(result.Changes);
        Assert.Equal(SyncBundleOutcome.Write, result.BundleOutcome);
        Assert.True(SameJson("\"bold\"", SyncBundleReader.Read(result.BundleJson).Bundle!.Units["settings:clock.style"].Value));
    }

    [Fact]
    public void Merge_UnitStampedAtTheEndOfTime_IsIgnored_ThenSettles()
    {
        // A PC with a broken clock, or a hostile file, stamped the unit at the very end of time. Taken as it
        // is, no later edit could beat it, so it is ignored and this PC's copy is written in its place.
        var local = Documents(("settings:clock.style", "\"bold\""));
        var main = SyncBundleReader.Read($$"""
            { "format": 1, "deviceId": "{{DeviceA}}", "units": { "settings:clock.style": { "stamp": "9999-12-31T23:59:59.999Z", "value": "plain" } } }
            """).Bundle!;
        var first = Merge(Stamped(GuidB, local, Earlier), local, main);

        Assert.Empty(first.Changes);
        Assert.Contains(first.Skipped, s => s.Reason == SyncSkipReason.FutureStamp);
        Assert.Equal(SyncBundleOutcome.Write, first.BundleOutcome);
        var written = SyncBundleReader.Read(first.BundleJson).Bundle!;
        Assert.Equal(V(Earlier, DeviceB), written.Units["settings:clock.style"].Version);

        var again = Merge(first.State, local, written, now: T0.AddMinutes(1));

        Assert.Equal(SyncBundleOutcome.Unchanged, again.BundleOutcome);
        Assert.Empty(again.Changes);
    }

    [Fact]
    public void Merge_StampsLocalChangesBeforeMerging()
    {
        var local = Documents(("settings:clock.style", "\"bold\""));
        var state = Stamped(GuidA, local, Earlier);
        local["settings:clock.style"] = Json("\"plain\"");

        var result = Merge(state, local, main: null, now: T0);

        Assert.Equal(V(T0, DeviceA), result.Bundle.Units["settings:clock.style"].Version);
        Assert.True(result.StateChanged);
    }

    internal static SyncMergeResult Merge(
        SyncState state,
        Dictionary<string, JsonElement> local,
        SyncBundle? main,
        IReadOnlyList<SyncBundle>? copies = null,
        SyncUnitValidator? validator = null,
        DateTimeOffset? now = null,
        SyncScope? scope = null,
        bool mainCanBeReplaced = true) =>
        SyncMerger.Merge(new SyncMergeRequest
        {
            State = state,
            Scope = scope ?? Scope,
            LocalUnits = local,
            Main = main,
            MainCanBeReplaced = mainCanBeReplaced,
            ConflictCopies = copies ?? [],
            Validator = validator,
            Now = now ?? T0,
        });

    internal static Dictionary<string, JsonElement> Apply(Dictionary<string, JsonElement> local, IEnumerable<SyncChange> changes)
    {
        var applied = new Dictionary<string, JsonElement>(local, StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (change.Kind == SyncChangeKind.Set)
                applied[change.UnitName] = change.Value!.Value;
            else
                applied.Remove(change.UnitName);
        }
        return applied;
    }

    private static SyncState Stamped(Guid device, Dictionary<string, JsonElement> local, DateTimeOffset at) =>
        SyncStamper.Stamp(SyncState.Create(device), local, Scope, at);

    /// <summary>The units a PC with this state and these values has written.</summary>
    private static Dictionary<string, SyncBundleUnit> Exported(SyncState state, Dictionary<string, JsonElement> local) =>
        state.Units.ToDictionary(
            pair => pair.Key,
            pair => new SyncBundleUnit(pair.Value.Version, pair.Value.IsDeleted ? null : SyncValues.Strip(local[pair.Key])),
            StringComparer.Ordinal);
}
