namespace Atap.Core.Sync;

/// <summary>Why a unit was not read, applied or written. Core never logs, so the App logs these.</summary>
public enum SyncSkipReason
{
    /// <summary>The name or the entry in the bundle is malformed.</summary>
    MalformedUnit,

    /// <summary>The unit never syncs: placement, a Machine key or a Secret key.</summary>
    Excluded,

    /// <summary>The unit is not known on this PC. It is carried through, never applied.</summary>
    Unknown,

    /// <summary>The value on this PC is a secret reference, so the unit stays on this PC.</summary>
    LocalOnly,

    /// <summary>The remote value is a secret reference. A bundle cannot point at this PC's secrets.</summary>
    SecretValue,

    /// <summary>The document the unit belongs to was not read on this PC, so nothing in it can change.</summary>
    DocumentMissing,

    /// <summary>The remote value has the wrong JSON kind for this unit.</summary>
    WrongShape,

    /// <summary>The validation hook refused the remote value.</summary>
    Rejected,

    /// <summary>The remote bundle deletes a unit that is never deleted.</summary>
    FixedUnitDeleted,

    /// <summary>The local value nests too deeply to fit in a bundle.</summary>
    TooDeep,

    /// <summary>
    /// The remote unit is stamped more than <see cref="SyncMerger.FutureTolerance"/> after now. Taken as
    /// it is, it would win against every later change, so it is ignored and this PC's copy is written.
    /// </summary>
    FutureStamp,
}

/// <param name="UnitName">The unit.</param>
/// <param name="Reason">Why it was skipped.</param>
/// <param name="Detail">More detail, such as the validation hook's reason. Never contains a value.</param>
public sealed record SyncSkip(string UnitName, SyncSkipReason Reason, string? Detail = null);
