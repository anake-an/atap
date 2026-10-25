using System.Text.Json.Nodes;
using Atap.Core.Storage;

namespace Atap.Core.Layout;

/// <summary>The layout.json upgrades, oldest first. Each one only adds or moves fields.</summary>
public static class LayoutMigrations
{
    public static IReadOnlyList<IJsonMigration> All { get; } = [new V1ToV2(), new V2ToV3()];

    /// <summary>Runs every step from <paramref name="fromVersion"/> up to the current version and sets schemaVersion.</summary>
    public static void Upgrade(JsonObject root, int fromVersion)
    {
        foreach (var migration in All.Where(m => m.FromVersion >= fromVersion).OrderBy(m => m.FromVersion))
            migration.Apply(root);
        root["schemaVersion"] = LayoutDocument.CurrentSchemaVersion;
    }

    /// <summary>v1 to v2 adds placement. A missing placement already means top centre on the primary monitor.</summary>
    private sealed class V1ToV2 : IJsonMigration
    {
        public int FromVersion => 1;

        public void Apply(JsonObject root)
        {
        }
    }

    /// <summary>v2 to v3 adds profiles and moves the panel's placements into a first page named "main".</summary>
    private sealed class V2ToV3 : IJsonMigration
    {
        public int FromVersion => 2;

        public void Apply(JsonObject root)
        {
            if (root["profiles"] is not JsonArray)
                root["profiles"] = new JsonArray();

            if (root["states"] is not JsonObject states || states["panel"] is not JsonObject panel)
                return;
            if (panel["pages"] is JsonArray)
            {
                panel.Remove("placements");
                return;
            }

            var placements = panel["placements"]?.DeepClone() ?? new JsonArray();
            panel.Remove("placements");
            panel["pages"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = PanelLayout.MainPageId,
                    ["name"] = PanelLayout.MainPageName,
                    ["placements"] = placements,
                },
            };
        }
    }
}
