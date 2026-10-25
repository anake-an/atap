namespace Atap.Core.Sync;

/// <summary>Fixed limits of the sync bundle format. A bundle outside them is ignored.</summary>
public static class SyncLimits
{
    /// <summary>The bundle and sync-state format this version reads and writes.</summary>
    public const int CurrentFormat = 1;

    /// <summary>Largest bundle accepted or written, in UTF-8 bytes (1 MB).</summary>
    public const int MaxBundleBytes = 1_048_576;

    /// <summary>
    /// Largest bundle file worth reading, in bytes: <see cref="MaxBundleBytes"/> plus 3 bytes for a
    /// UTF-8 byte order mark. Check the file length first and report a longer file as TooLarge
    /// without reading it.
    /// </summary>
    public const int MaxBundleFileBytes = MaxBundleBytes + 3;

    /// <summary>Deepest nesting accepted, counting the bundle's root object as level 1.</summary>
    public const int MaxDepth = 32;

    /// <summary>Longest unit name accepted.</summary>
    public const int MaxUnitNameLength = 256;
}
