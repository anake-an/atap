namespace Atap.Core.Storage;

/// <summary>What happened when a settings or layout file was loaded. The app logs anything but Loaded.</summary>
public enum FileLoadOutcome
{
    /// <summary>The file was read successfully.</summary>
    Loaded,

    /// <summary>No file existed; defaults were written (or are used in memory until the first change).</summary>
    CreatedDefaults,

    /// <summary>The file used an older schema; it was backed up, upgraded and saved back.</summary>
    Migrated,

    /// <summary>The file was corrupt; it was backed up and defaults were written.</summary>
    RecoveredFromCorrupt,

    /// <summary>
    /// The file could not be read (locked, no access), or it was corrupt and could not be moved
    /// aside; it is left alone and defaults are used in memory only.
    /// </summary>
    Unreadable,

    /// <summary>The file is from a newer Atap. What is understood is used, and the file is never written.</summary>
    NewerVersionReadOnly,
}
