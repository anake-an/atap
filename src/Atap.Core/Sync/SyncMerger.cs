using System.Text.Json;

namespace Atap.Core.Sync;

/// <summary>Everything one sync pass needs. Export and import are the same pass.</summary>
public sealed class SyncMergeRequest
{
    /// <summary>The saved sync-state.json. The merge stamps local changes first.</summary>
    public required SyncState State { get; init; }

    public required SyncScope Scope { get; init; }

    /// <summary>Every local unit with its current value, from <see cref="SyncUnits"/>.</summary>
    /// <remarks>
    /// Only pass a document that was loaded from its file. One with defaults in memory, because the
    /// file is locked or unreadable, must be left out, or the defaults win on every PC. When the
    /// file itself was reset to defaults, for example after it was corrupt, call
    /// <see cref="SyncStamper.YieldToOneDrive"/> for that document before the next stamp or merge.
    /// </remarks>
    public required IReadOnlyDictionary<string, JsonElement> LocalUnits { get; init; }

    /// <summary>atap-sync.json as read, or null when it is missing or could not be read.</summary>
    public SyncBundle? Main { get; init; }

    /// <summary>
    /// False when atap-sync.json exists but must not be written over
    /// (<see cref="SyncBundleReadResult.CanBeReplaced"/> is false).
    /// </summary>
    public bool MainCanBeReplaced { get; init; } = true;

    /// <summary>OneDrive conflict copies (atap-sync-*.json) that were read. They merge like the main file.</summary>
    public IReadOnlyList<SyncBundle> ConflictCopies { get; init; } = [];

    /// <summary>Checks remote values before they are applied. Null applies every value that fits its shape.</summary>
    public SyncUnitValidator? Validator { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public enum SyncChangeKind
{
    /// <summary>Write <see cref="SyncChange.Value"/> into the local store.</summary>
    Set,

    /// <summary>Remove the unit locally: reset the key to its default, or drop the widget instance's settings.</summary>
    Remove,
}

/// <summary>A change the App applies to a local store.</summary>
public sealed record SyncChange(string UnitName, SyncChangeKind Kind, JsonElement? Value);

public enum SyncBundleOutcome
{
    /// <summary>The bundle already holds the merged result. Nothing to write.</summary>
    Unchanged,

    /// <summary>Write <see cref="SyncMergeResult.BundleJson"/> to a temp file, then rename it over atap-sync.json.</summary>
    Write,

    /// <summary>The merged bundle is over <see cref="SyncLimits.MaxBundleBytes"/>, even without carried units. Nothing is written.</summary>
    TooLarge,

    /// <summary>
    /// A write is needed, but the file on disk must not be written over. Every PC stays blocked until
    /// the file is replaced, so tell the user (see <see cref="SyncBundleReadResult.CanBeReplaced"/>).
    /// </summary>
    Blocked,
}

/// <summary>
/// The outcome of one sync pass.
/// </summary>
/// <remarks>
/// Apply it in this order:
/// <list type="number">
/// <item>Make <see cref="State"/> current, so the store writes that follow see the new hashes and do not stamp again.</item>
/// <item>Apply <see cref="Changes"/>. For any that fail, use <see cref="WithoutChangesTo"/> and keep that state instead.</item>
/// <item>Save the state to sync-state.json.</item>
/// <item>When <see cref="BundleOutcome"/> is Write, write <see cref="BundleJson"/>.</item>
/// <item>When the bundle was written or is Unchanged, delete the conflict copies that were merged.</item>
/// </list>
/// </remarks>
public sealed class SyncMergeResult
{
    private readonly SyncState _stamped;

    internal SyncMergeResult(
        SyncState stamped,
        SyncState state,
        bool stateChanged,
        IReadOnlyList<SyncChange> changes,
        IReadOnlyList<SyncSkip> skipped,
        SyncBundle bundle,
        SyncBundleOutcome bundleOutcome,
        string? bundleJson)
    {
        _stamped = stamped;
        State = state;
        StateChanged = stateChanged;
        Changes = changes;
        Skipped = skipped;
        Bundle = bundle;
        BundleOutcome = bundleOutcome;
        BundleJson = bundleJson;
    }

    /// <summary>The new sync state, with applied units at their remote versions so they do not bounce back.</summary>
    public SyncState State { get; }

    /// <summary>True when <see cref="State"/> differs from the request's state and should be saved.</summary>
    public bool StateChanged { get; }

    /// <summary>Changes to apply locally, in unit name order.</summary>
    public IReadOnlyList<SyncChange> Changes { get; }

    /// <summary>Units that were not read, applied or written, with the reason. For the log.</summary>
    public IReadOnlyList<SyncSkip> Skipped { get; }

    /// <summary>The merged bundle: the newest version of every unit this PC may write.</summary>
    public SyncBundle Bundle { get; }

    public SyncBundleOutcome BundleOutcome { get; }

    /// <summary>The bundle text to write when <see cref="BundleOutcome"/> is Write. Otherwise null.</summary>
    public string? BundleJson { get; }

    /// <summary>
    /// The state to keep when some changes could not be applied, for example because a store is
    /// read-only. Those units keep this PC's version, so the next pass offers them again instead of
    /// stamping the old local value as a new change.
    /// </summary>
    public SyncState WithoutChangesTo(IEnumerable<string> unitNames)
    {
        ArgumentNullException.ThrowIfNull(unitNames);
        var units = new Dictionary<string, SyncStateUnit>(State.Units, StringComparer.Ordinal);
        foreach (var name in unitNames)
        {
            if (_stamped.Units.TryGetValue(name, out var before))
                units[name] = before;
            else
                units.Remove(name);
        }
        return State with { Units = units };
    }
}

/// <summary>
/// Merges this PC's units with the bundle and its conflict copies. The newest version of each
/// unit wins, a tie goes to the higher device id, and an applied unit keeps its remote version so
/// it does not bounce back. Pure: time comes in through the request.
/// </summary>
/// <remarks>
/// A remote stamp is taken as it is, even far past this PC's clock. Every PC then writes the same
/// newest version, so the bundle settles. A PC whose clock runs ahead only wins edits made at about
/// the same time on other PCs: an edit made after its change is stamped later by
/// <see cref="SyncVersion.NextStamp"/>.
/// </remarks>
public static class SyncMerger
{
    /// <summary>Deepest unit value that still fits in a bundle: root, units and the unit entry take three levels.</summary>
    private const int MaxValueDepth = SyncLimits.MaxDepth - 3;

    /// <summary>
    /// How far ahead of this PC's clock a remote stamp may be: 100 years. A PC whose clock runs days or years
    /// ahead still syncs, because a later edit is stamped just after its stamp. Only a stamp near the end of
    /// </summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromDays(36525);

    public static SyncMergeResult Merge(SyncMergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = request.Scope;
        var local = request.LocalUnits;
        var stamped = SyncStamper.Stamp(request.State, local, scope, request.Now);
        var skips = new Skips();

        var remote = BestRemoteUnits(request, skips);

        var outUnits = new Dictionary<string, SyncBundleUnit>(StringComparer.Ordinal);
        var carried = new HashSet<string>(StringComparer.Ordinal);
        var state = new Dictionary<string, SyncStateUnit>(stamped.Units, StringComparer.Ordinal);
        var changes = new List<SyncChange>();
        var initial = new Dictionary<string, SyncStateUnit>(StringComparer.Ordinal);

        var names = stamped.Units.Keys.Union(remote.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var name in names)
        {
            remote.TryGetValue(name, out var theirs);
            stamped.Units.TryGetValue(name, out var ours);

            if (scope.Classify(name) == SyncUnitKind.Unknown)
            {
                // Only remote units can be unknown. Keep them for the PCs that know them.
                outUnits[name] = theirs!;
                carried.Add(name);
                skips.Add(name, SyncSkipReason.Unknown);
                continue;
            }

            if (theirs is null || (ours is not null && ours.Version > theirs.Version))
            {
                if (ExportOurs(name, ours!, local, scope, skips) is { } exported)
                {
                    if (exported.Version.Stamp == SyncVersion.InitialStamp)
                    {
                        // The initial stamp never leaves this PC. It goes out as a change made now, so a PC
                        // that later takes the OneDrive copy finds a real stamp to yield to.
                        initial[name] = ours!;
                        exported = exported with { Version = new SyncVersion(SyncVersion.NextStamp(request.Now, theirs?.Version.Stamp), stamped.DeviceId) };
                    }
                    outUnits[name] = exported;
                }
                else if (theirs is not null)
                    outUnits[name] = theirs;
                continue;
            }

            // The remote version wins, or both sides already have the same version.
            outUnits[name] = theirs;
            if (ours is not null && ours.Version == theirs.Version)
                continue;
            Import(name, theirs, ours, request, state, changes, skips);
        }

        var (outcome, json) = Serialize(new SyncBundle(stamped.DeviceId, outUnits), carried, request);
        foreach (var (name, ours) in initial)
        {
            // Written: the state takes the stamp the bundle has. Not written: the unit keeps the
            // initial stamp, so it still yields to any bundle that turns up before the next pass.
            if (outcome == SyncBundleOutcome.Write)
                state[name] = new SyncStateUnit(outUnits[name].Version, ours.Hash);
            else
                outUnits[name] = outUnits[name] with { Version = ours.Version };
        }
        var bundle = new SyncBundle(stamped.DeviceId, outUnits);
        var newState = stamped with { Units = state };

        return new SyncMergeResult(stamped, newState, !newState.SameAs(request.State), changes, skips.List, bundle, outcome, json);
    }

    /// <summary>The newest version of each unit across the main file and its conflict copies.</summary>
    private static Dictionary<string, SyncBundleUnit> BestRemoteUnits(SyncMergeRequest request, Skips skips)
    {
        var best = new Dictionary<string, SyncBundleUnit>(StringComparer.Ordinal);
        IEnumerable<SyncBundle> bundles = request.Main is null ? request.ConflictCopies : [request.Main, .. request.ConflictCopies];
        foreach (var bundle in bundles)
        {
            foreach (var (name, unit) in bundle.Units)
            {
                if (request.Scope.Classify(name) == SyncUnitKind.Excluded)
                {
                    skips.Add(name, SyncSkipReason.Excluded);
                    continue;
                }
                if (unit.Version.Stamp > request.Now + FutureTolerance)
                {
                    skips.Add(name, SyncSkipReason.FutureStamp);
                    continue;
                }
                if (!best.TryGetValue(name, out var current) || unit.Version > current.Version)
                    best[name] = unit;
            }
        }
        return best;
    }

    /// <summary>This PC's unit as it goes into the bundle, or null when it cannot leave the PC.</summary>
    private static SyncBundleUnit? ExportOurs(string name, SyncStateUnit ours, IReadOnlyDictionary<string, JsonElement> local, SyncScope scope, Skips skips)
    {
        if (ours.IsDeleted)
            return SyncBundleUnit.Deleted(ours.Version);

        // A fixed unit of a document that was not read has a version but no value this pass.
        if (!local.TryGetValue(name, out var value) || SyncValues.Strip(value, scope.LocalFieldsOf(name)) is not { } exported)
            return null;

        if (Depth(exported) > MaxValueDepth)
        {
            skips.Add(name, SyncSkipReason.TooDeep);
            return null;
        }
        return new SyncBundleUnit(ours.Version, exported);
    }

    private static void Import(
        string name,
        SyncBundleUnit theirs,
        SyncStateUnit? ours,
        SyncMergeRequest request,
        Dictionary<string, SyncStateUnit> state,
        List<SyncChange> changes,
        Skips skips)
    {
        var local = request.LocalUnits;
        var localFields = request.Scope.LocalFieldsOf(name);

        if (!SyncStamper.HasDocument(name, local))
        {
            skips.Add(name, SyncSkipReason.DocumentMissing);
            return;
        }

        var hasLocal = local.TryGetValue(name, out var localValue);
        if (hasLocal && SyncValues.Strip(localValue, localFields) is null)
        {
            skips.Add(name, SyncSkipReason.LocalOnly);
            return;
        }

        if (theirs.Value is not { } remoteValue)
        {
            if (SyncUnitNames.IsFixed(name))
            {
                skips.Add(name, SyncSkipReason.FixedUnitDeleted);
                return;
            }
            state[name] = SyncStateUnit.Deleted(theirs.Version);
            if (hasLocal)
                changes.Add(new SyncChange(name, SyncChangeKind.Remove, null));
            return;
        }

        // Nothing from the bundle may point at this PC's secrets or set its Machine or Secret fields.
        if (SyncValues.Strip(remoteValue, localFields) is not { } incoming)
        {
            skips.Add(name, SyncSkipReason.SecretValue);
            return;
        }
        if (!SyncUnitShapes.Fits(name, incoming))
        {
            skips.Add(name, SyncSkipReason.WrongShape);
            return;
        }

        var candidate = SyncValues.Restore(incoming, hasLocal ? localValue : null, localFields, field => request.Scope.IsSecretField(name, field));
        if (request.Validator is { } validator)
        {
            var validation = validator(name, candidate);
            if (!validation.IsAccepted)
            {
                skips.Add(name, SyncSkipReason.Rejected, validation.Reason);
                return;
            }
            candidate = validation.Value;
        }

        if (SyncValues.Strip(candidate, localFields) is not { } tracked)
        {
            skips.Add(name, SyncSkipReason.Rejected, "validated value is a secret reference");
            return;
        }

        var hash = SyncValues.Hash(tracked);
        state[name] = new SyncStateUnit(theirs.Version, hash);
        if (!hasLocal || ours?.Hash != hash)
            changes.Add(new SyncChange(name, SyncChangeKind.Set, candidate));
    }

    private static (SyncBundleOutcome Outcome, string? Json) Serialize(SyncBundle bundle, HashSet<string> carried, SyncMergeRequest request)
    {
        if (!NeedsWrite(bundle, request.Main))
            return (SyncBundleOutcome.Unchanged, null);
        if (!request.MainCanBeReplaced)
            return (SyncBundleOutcome.Blocked, null);
        if (SyncBundleWriter.TryWrite(bundle, out var json))
            return (SyncBundleOutcome.Write, json);

        // Units only other versions know are the first to go.
        var trimmed = bundle with { Units = bundle.Units.Where(pair => !carried.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal) };
        return carried.Count > 0 && SyncBundleWriter.TryWrite(trimmed, out json)
            ? (SyncBundleOutcome.Write, json)
            : (SyncBundleOutcome.TooLarge, null);
    }

    /// <summary>
    /// True when the merged bundle has a unit, a version or a deletion the main file lacks. Every
    /// main unit that counted is in the merged bundle at the same or a newer version. Excluded units
    /// do not force a write by themselves, so two PCs that disagree about them do not keep
    /// rewriting the file.
    /// </summary>
    private static bool NeedsWrite(SyncBundle bundle, SyncBundle? main)
    {
        if (main is null)
            return bundle.Units.Count > 0;

        foreach (var (name, unit) in bundle.Units)
        {
            if (!main.Units.TryGetValue(name, out var onDisk) || onDisk.Version != unit.Version || onDisk.IsDeleted != unit.IsDeleted)
                return true;
        }
        return false;
    }

    private static int Depth(JsonElement value)
    {
        var deepest = 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                    deepest = Math.Max(deepest, Depth(property.Value));
                return deepest + 1;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    deepest = Math.Max(deepest, Depth(item));
                return deepest + 1;
            default:
                return 0;
        }
    }

    /// <summary>Skips without repeats: a unit in the main file and a conflict copy is reported once.</summary>
    private sealed class Skips
    {
        private readonly HashSet<(string, SyncSkipReason)> _seen = [];

        public List<SyncSkip> List { get; } = [];

        public void Add(string name, SyncSkipReason reason, string? detail = null)
        {
            if (_seen.Add((name, reason)))
                List.Add(new SyncSkip(name, reason, detail));
        }
    }
}
