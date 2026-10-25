namespace Atap.Core.Layout;

/// <summary>The live layout. Use it on the UI thread.</summary>
public interface ILayoutService
{
    /// <summary>The whole layout.json in memory.</summary>
    LayoutDocument Document { get; }

    /// <summary>What the active profile shows.</summary>
    EffectiveLayout Active { get; }

    string ActiveProfileId { get; }

    /// <summary>Unknown ids fall back to the Default profile.</summary>
    void SetActiveProfile(string profileId);

    /// <summary>
    /// Changes the document in memory and saves the change. The change is applied to the file as it is
    /// on disk, so hand edits made while Atap runs are kept.
    /// </summary>
    LayoutWriteResult Update(Action<LayoutDocument> change);

    /// <summary>Writes queued changes to disk now. Applied once everything is on disk.</summary>
    LayoutWriteResult Flush() => LayoutWriteResult.Applied;

    /// <summary>True when the file could not be read or is from a newer Atap. Changes then stay in memory.</summary>
    bool IsReadOnly { get; }

    /// <summary>Raised on the UI thread after the document or the active profile changed.</summary>
    event EventHandler? Changed;
}

public enum LayoutWriteResult
{
    /// <summary>Saved (or queued to save).</summary>
    Applied,

    /// <summary>The session is read-only. The change applies in memory only.</summary>
    ReadOnly,

    /// <summary>The file does not parse right now, or could not be written. The change applies in memory only.</summary>
    Failed,
}
