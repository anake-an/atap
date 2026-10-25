using System.Text.Json.Nodes;

namespace Atap.Core.Storage;

/// <summary>
/// Upgrades a file from <see cref="FromVersion"/> to the next version. It works on the raw JSON before
/// typed parsing, so fields it does not know survive. The store backs the file up before the first step.
/// </summary>
public interface IJsonMigration
{
    int FromVersion { get; }

    /// <summary>Changes <paramref name="root"/> in place. The store sets schemaVersion afterwards.</summary>
    void Apply(JsonObject root);
}
