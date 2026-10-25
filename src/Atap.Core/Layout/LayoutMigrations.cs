using System.Text.Json.Nodes;
using Atap.Core.Storage;

namespace Atap.Core.Layout;

/// <summary>The layout.json upgrades, oldest first. Each one only adds or moves fields, or swaps an untouched default for the new one.</summary>
public static class LayoutMigrations
{
    public static IReadOnlyList<IJsonMigration> All { get; } = [new V1ToV2(), new V2ToV3(), new V3ToV4()];

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

    /// <summary>
    /// v3 to v4: a panel still on the 1.0 default page (music, then the missed alerts) now follows the hover view.
    /// Only that exact page changes, in the Default profile and in every profile with its own copy. A panel the
    /// user arranged stays as it is.
    /// </summary>
    private sealed class V3ToV4 : IJsonMigration
    {
        public int FromVersion => 3;

        public void Apply(JsonObject root)
        {
            ReplaceUntouchedPanel(root["states"] as JsonObject);
            if (root["profiles"] is not JsonArray profiles)
                return;
            foreach (var profile in profiles.OfType<JsonObject>())
                ReplaceUntouchedPanel(profile["states"] as JsonObject);
        }

        private static void ReplaceUntouchedPanel(JsonObject? states)
        {
            if (states?["panel"]?["pages"] is not JsonArray { Count: 1 } pages
                || pages[0] is not JsonObject page
                || page["placements"] is not JsonArray { Count: 2 } placements
                || !IsPlacement(placements[0], DefaultLayout.MediaWidgetId, 0)
                || !IsPlacement(placements[1], DefaultLayout.MissedAlertsWidgetId, 1))
                return;
            page["placements"] = new JsonArray
            {
                new JsonObject
                {
                    ["widgetId"] = DefaultLayout.MissedAlertsWidgetId,
                    ["size"] = "medium",
                    ["order"] = 0,
                    ["slot"] = "center",
                },
            };
        }

        // As 1.0 wrote it: medium, in the centre, at this order, and nothing else.
        private static bool IsPlacement(JsonNode? node, string widgetId, int order) =>
            node is JsonObject placement
            && placement.Count == 4
            && Text(placement["widgetId"]) == widgetId
            && string.Equals(Text(placement["size"]), "medium", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Text(placement["slot"]), "center", StringComparison.OrdinalIgnoreCase)
            && placement["order"] is JsonValue value && value.TryGetValue<int>(out var actual) && actual == order;

        private static string? Text(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
