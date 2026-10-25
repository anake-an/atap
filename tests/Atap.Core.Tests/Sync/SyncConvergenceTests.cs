using System.Text.Json;
using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

/// <summary>Two PCs sharing one sync folder, run as pure merges over the folder's text.</summary>
public sealed class SyncConvergenceTests
{
    private DateTimeOffset _now = T0;

    [Fact]
    public void TwoPcs_ConvergeAfterEditsOnBoth_AndThenStayQuiet()
    {
        var folder = new Folder();
        var a = new Pc(GuidA, Scope(), Documents(
            ("settings:clock.style", "\"bold\""),
            ("settings:malaysia.state", "\"SGR\""),
            ("layout:widgetSettings:clock", """{"use24Hour":true}"""),
            (SyncUnitNames.Placement, """{"anchor":"topCenter","offset":0}"""),
            ("settings:localApi.enabled", "true")));
        var b = new Pc(GuidB, Scope(), Documents(
            ("settings:clock.style", "\"plain\""),
            ("layout:widgetSettings:next-meeting", """{"icsUrl":"secret:b1","minutesBefore":5}"""),
            (SyncUnitNames.Placement, """{"anchor":"leftEdge","offset":120}"""),
            ("settings:localApi.enabled", "false")));

        a.Enable(folder, SyncStartChoice.KeepThisPc, Tick());
        b.Enable(folder, SyncStartChoice.UseOneDrive, Tick());
        SyncUntilQuiet(folder, a, b);
        AssertConverged(a, b);

        // Placement, Machine keys and secret references never crossed over.
        Assert.True(SameJson("""{"anchor":"leftEdge","offset":120}""", b.Local[SyncUnitNames.Placement]));
        Assert.True(SameJson("true", a.Local["settings:localApi.enabled"]));
        Assert.True(SameJson("false", b.Local["settings:localApi.enabled"]));
        Assert.True(SameJson("""{"minutesBefore":5}""", a.Local["layout:widgetSettings:next-meeting"]));
        Assert.True(SameJson("""{"icsUrl":"secret:b1","minutesBefore":5}""", b.Local["layout:widgetSettings:next-meeting"]));
        Assert.True(SameJson("\"bold\"", b.Local["settings:clock.style"]));

        a.Edit("settings:clock.style", "\"compact\"", Tick());
        b.Edit("settings:malaysia.state", "\"JHR\"", Tick());
        b.Edit("layout:widgetSettings:clock", null, Tick());
        a.Edit("layout:widgetSettings:next-meeting", """{"minutesBefore":15}""", Tick());
        var rounds = SyncUntilQuiet(folder, b, a);
        AssertConverged(a, b);

        Assert.True(rounds <= 2, $"took {rounds} rounds");
        Assert.True(SameJson("\"compact\"", b.Local["settings:clock.style"]));
        Assert.True(SameJson("\"JHR\"", a.Local["settings:malaysia.state"]));
        Assert.False(a.Local.ContainsKey("layout:widgetSettings:clock"));
        Assert.True(SameJson("""{"minutesBefore":15,"icsUrl":"secret:b1"}""", b.Local["layout:widgetSettings:next-meeting"]));

        // No bounce: with nothing new, neither PC writes again.
        Assert.False(a.Sync(folder, Tick()));
        Assert.False(b.Sync(folder, Tick()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SecondPcThatUsesTheSettingsFromOneDrive_TakesTheFirstPcsSettings(bool firstIsA)
    {
        var folder = new Folder();
        var first = new Pc(firstIsA ? GuidA : GuidB, Scope(), Documents(
            ("settings:clock.style", "\"bold\""),
            ("layout:widgetSettings:clock", """{"use24Hour":true}""")));
        var second = new Pc(firstIsA ? GuidB : GuidA, Scope(), Documents(
            ("settings:clock.style", "\"plain\""),
            ("layout:widgetSettings:clock", """{"use24Hour":false}"""),
            ("settings:malaysia.state", "\"JHR\"")));
        second.Local[SyncUnitNames.EnabledWidgets] = Json("""["clock"]""");

        // No sync file and no sync-state.json yet, so the first PC is not asked and takes the OneDrive copy.
        first.Enable(folder, SyncStartChoice.UseOneDrive, Tick());
        var firstWrite = folder.Main;
        second.Enable(folder, SyncStartChoice.UseOneDrive, Tick());
        SyncUntilQuiet(folder, first, second);

        AssertConverged(first, second);
        Assert.True(SameJson("\"bold\"", second.Local["settings:clock.style"]));
        Assert.True(SameJson("""{"use24Hour":true}""", second.Local["layout:widgetSettings:clock"]));
        Assert.True(SameJson("""["media","clock"]""", second.Local[SyncUnitNames.EnabledWidgets]));
        // A unit only the second PC had is added to the bundle.
        Assert.True(SameJson("\"JHR\"", first.Local["settings:malaysia.state"]));
        // The initial stamp never reaches the sync file.
        Assert.DoesNotContain("1970-01-01", firstWrite);
        Assert.DoesNotContain("1970-01-01", folder.Main);
        Assert.False(first.Sync(folder, Tick()));
        Assert.False(second.Sync(folder, Tick()));
    }

    [Fact]
    public void SameEditTimeOnBothPcs_PicksTheSameWinnerEverywhere()
    {
        var folder = new Folder();
        var a = new Pc(GuidA, Scope(), Documents(("settings:clock.style", "\"bold\"")));
        var b = new Pc(GuidB, Scope(), Documents(("settings:clock.style", "\"bold\"")));
        a.Enable(folder, SyncStartChoice.KeepThisPc, Tick());
        b.Enable(folder, SyncStartChoice.UseOneDrive, Tick());
        SyncUntilQuiet(folder, a, b);

        // Both edit in the same millisecond while OneDrive is offline. B's file becomes a conflict copy.
        var at = Tick();
        a.Edit("settings:clock.style", "\"from-a\"", at);
        b.Edit("settings:clock.style", "\"from-b\"", at);
        var offlineView = folder.Snapshot();
        a.Sync(folder, Tick());
        b.Sync(offlineView, Tick());
        folder.Copies["atap-sync-DESKTOP-B.json"] = offlineView.Main!;

        SyncUntilQuiet(folder, a, b);

        AssertConverged(a, b);
        // A tie goes to the higher device id, which is B.
        Assert.True(SameJson("\"from-b\"", a.Local["settings:clock.style"]));
        Assert.Empty(folder.Copies);
    }

    [Fact]
    public void ConflictCopy_IsMergedThenDeleted()
    {
        var folder = new Folder();
        var a = new Pc(GuidA, Scope(), Documents(("settings:clock.style", "\"bold\"")));
        var b = new Pc(GuidB, Scope(), Documents());
        a.Enable(folder, SyncStartChoice.KeepThisPc, Tick());
        b.Enable(folder, SyncStartChoice.UseOneDrive, Tick());
        SyncUntilQuiet(folder, a, b);

        var offlineView = folder.Snapshot();
        b.Edit("settings:malaysia.state", "\"KDH\"", Tick());
        b.Sync(offlineView, Tick());
        a.Edit("settings:language.choice", "\"ms\"", Tick());
        a.Sync(folder, Tick());
        folder.Copies["atap-sync-DESKTOP-B.json"] = offlineView.Main!;

        a.Sync(folder, Tick());

        Assert.Empty(folder.Copies);
        Assert.True(SameJson("\"KDH\"", a.Local["settings:malaysia.state"]));
        SyncUntilQuiet(folder, a, b);
        AssertConverged(a, b);
        Assert.True(SameJson("\"ms\"", b.Local["settings:language.choice"]));
    }

    [Fact]
    public void PcsOnDifferentVersions_DoNotFightOverAKeyOnlyOneKnows()
    {
        var folder = new Folder();
        var newer = new SyncScope(["clock.style", "newfeature.mode"]);
        var a = new Pc(GuidA, Scope(), Documents(("settings:clock.style", "\"bold\"")));
        var b = new Pc(GuidB, newer, Documents(("settings:newfeature.mode", "\"fast\"")));
        a.Enable(folder, SyncStartChoice.KeepThisPc, Tick());
        b.Enable(folder, SyncStartChoice.UseOneDrive, Tick());
        SyncUntilQuiet(folder, a, b);

        a.Edit("settings:clock.style", "\"plain\"", Tick());
        SyncUntilQuiet(folder, a, b);

        Assert.False(a.Local.ContainsKey("settings:newfeature.mode"));
        Assert.True(SameJson("\"fast\"", b.Local["settings:newfeature.mode"]));
        Assert.True(SameJson("\"plain\"", b.Local["settings:clock.style"]));
        Assert.Contains("settings:newfeature.mode", SyncBundleReader.Read(folder.Main).Bundle!.Units.Keys);
        Assert.False(a.Sync(folder, Tick()));
        Assert.False(b.Sync(folder, Tick()));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(3650)]
    public void PcWithAClockDaysAhead_ConvergesAndThenStaysQuiet(int daysAhead)
    {
        var folder = new Folder();
        var a = new Pc(GuidA, Scope(), Documents(("settings:clock.style", "\"bold\""), ("settings:malaysia.state", "\"SGR\"")));
        var b = new Pc(GuidB, Scope(), Documents(("settings:clock.style", "\"bold\""), ("settings:malaysia.state", "\"SGR\"")))
        {
            ClockOffset = TimeSpan.FromDays(daysAhead),
        };

        // B's stamps are days ahead of A's clock.
        b.Enable(folder, SyncStartChoice.KeepThisPc, Tick());
        a.Enable(folder, SyncStartChoice.UseOneDrive, Tick());
        SyncUntilQuiet(folder, a, b);
        AssertConverged(a, b);

        // An edit on A still wins, although A's clock is behind B's stamps.
        a.Edit("settings:clock.style", "\"plain\"", Tick());
        b.Edit("settings:malaysia.state", "\"JHR\"", Tick());
        var rounds = SyncUntilQuiet(folder, a, b);
        AssertConverged(a, b);
        Assert.True(rounds <= 2, $"took {rounds} rounds");
        Assert.True(SameJson("\"plain\"", b.Local["settings:clock.style"]));
        Assert.True(SameJson("\"JHR\"", a.Local["settings:malaysia.state"]));

        // B's clock is put right. Its next edit still comes after the old stamps.
        b.ClockOffset = TimeSpan.Zero;
        b.Edit("settings:clock.style", "\"compact\"", Tick());
        SyncUntilQuiet(folder, b, a);
        AssertConverged(a, b);
        Assert.True(SameJson("\"compact\"", a.Local["settings:clock.style"]));

        Assert.False(a.Sync(folder, Tick()));
        Assert.False(b.Sync(folder, Tick()));
    }

    [Fact]
    public void BundleFromANewerAtap_IsNeverOverwritten()
    {
        var folder = new Folder { Main = """{ "format": 2, "deviceId": "9f8c3a52-7d1e-4c6b-a0e2-5b3f1d2c4e6a", "parts": [] }""" };
        var a = new Pc(GuidA, Scope(), Documents(("settings:clock.style", "\"bold\"")));

        a.Enable(folder, SyncStartChoice.KeepThisPc, Tick());

        Assert.StartsWith("""{ "format": 2""", folder.Main);
        Assert.Equal(SyncBundleOutcome.Blocked, a.LastOutcome);
    }

    private DateTimeOffset Tick() => _now = _now.AddSeconds(7);

    /// <summary>Lets the PCs take turns until a full round writes nothing. Returns the rounds that wrote.</summary>
    private int SyncUntilQuiet(Folder folder, params Pc[] pcs)
    {
        for (var round = 0; round < 10; round++)
        {
            var wrote = false;
            foreach (var pc in pcs)
                wrote |= pc.Sync(folder, Tick());
            if (!wrote)
                return round;
        }
        throw new InvalidOperationException("The PCs kept rewriting the bundle.");
    }

    private static void AssertConverged(Pc a, Pc b)
    {
        Assert.Equal(a.SyncedHashes(), b.SyncedHashes());
    }

    private sealed class Folder
    {
        public string? Main { get; set; }

        public Dictionary<string, string> Copies { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Folder Snapshot()
        {
            var copy = new Folder { Main = Main };
            foreach (var (name, text) in Copies)
                copy.Copies[name] = text;
            return copy;
        }
    }

    private sealed class Pc(Guid device, SyncScope scope, Dictionary<string, JsonElement> local)
    {
        private SyncState? _state;

        public Dictionary<string, JsonElement> Local { get; private set; } = local;

        public SyncBundleOutcome LastOutcome { get; private set; }

        /// <summary>How far this PC's clock is off. Every time this PC sees is moved by it.</summary>
        public TimeSpan ClockOffset { get; set; }

        public void Enable(Folder folder, SyncStartChoice choice, DateTimeOffset now)
        {
            var existing = folder.Main is null ? null : SyncBundleReader.Read(folder.Main).Bundle;
            _state = SyncStamper.Initialize(device, Local, scope, choice, existing, now + ClockOffset);
            Sync(folder, now);
        }

        /// <summary>A local edit, as a store write while sync is on. Null removes the unit.</summary>
        public void Edit(string name, string? json, DateTimeOffset now)
        {
            now += ClockOffset;
            if (json is null)
                Local.Remove(name);
            else
                Local[name] = Json(json);
            _state = SyncStamper.Stamp(_state!, Local, scope, now);
        }

        /// <summary>One pass as the App runs it. Returns true when it wrote the bundle.</summary>
        public bool Sync(Folder folder, DateTimeOffset now)
        {
            now += ClockOffset;
            var main = folder.Main is null ? null : SyncBundleReader.Read(folder.Main);
            var copies = folder.Copies.Values.Select(SyncBundleReader.Read).Where(read => read.Bundle is not null).Select(read => read.Bundle!).ToList();
            var result = SyncMerger.Merge(new SyncMergeRequest
            {
                State = _state!,
                Scope = scope,
                LocalUnits = Local,
                Main = main?.Bundle,
                MainCanBeReplaced = main?.CanBeReplaced ?? true,
                ConflictCopies = copies,
                Now = now,
            });

            _state = result.State;
            Local = SyncMergerTests.Apply(Local, result.Changes);
            // The stores write the applied values. That must not look like a new local change.
            Assert.Same(_state, SyncStamper.Stamp(_state, Local, scope, now));

            LastOutcome = result.BundleOutcome;
            if (result.BundleOutcome is SyncBundleOutcome.Write or SyncBundleOutcome.Unchanged)
                folder.Copies.Clear();
            if (result.BundleOutcome != SyncBundleOutcome.Write)
                return false;
            folder.Main = result.BundleJson;
            return true;
        }

        /// <summary>The hash of every synced unit as it would leave this PC.</summary>
        public SortedDictionary<string, string> SyncedHashes() => new(
            Local.Where(pair => scope.IsSynced(pair.Key))
                .Select(pair => (pair.Key, Stripped: SyncValues.Strip(pair.Value, field => scope.IsMachineField(pair.Key, field))))
                .Where(pair => pair.Stripped is not null)
                .ToDictionary(pair => pair.Key, pair => SyncValues.Hash(pair.Stripped!.Value)),
            StringComparer.Ordinal);
    }
}
