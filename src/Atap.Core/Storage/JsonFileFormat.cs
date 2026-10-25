using System.Text.Json.Nodes;

namespace Atap.Core.Storage;

/// <summary>How a <see cref="JsonFileStore{T}"/> reads, upgrades and writes one kind of file.</summary>
public sealed class JsonFileFormat<T> where T : class
{
    /// <summary>The schemaVersion this Atap writes. A file with a higher one is used read-only and never written.</summary>
    public required int CurrentVersion { get; init; }

    /// <summary>A new default document. Called for each use, so it must not hand out a shared instance.</summary>
    public required Func<T> CreateDefaults { get; init; }

    /// <summary>Typed parse of a root that is already at <see cref="CurrentVersion"/> (or newer). Null when the shape is wrong.</summary>
    public required Func<JsonObject, T?> Parse { get; init; }

    /// <summary>The bytes to write. Only called for documents this version may write.</summary>
    public required Func<T, byte[]> Serialize { get; init; }

    /// <summary>Fixes nulls and out-of-range values after each parse and each change.</summary>
    public Action<T>? Normalize { get; init; }

    /// <summary>The upgrades from older versions. They run oldest first, after a backup.</summary>
    public IReadOnlyList<IJsonMigration> Migrations { get; init; } = [];

    /// <summary>
    /// True: a missing or corrupt file gets defaults written at load. False: nothing is written until the first
    /// change, so a missing file still tells a new install apart from an upgraded one.
    /// </summary>
    public bool WriteDefaultsAtLoad { get; init; } = true;
}
