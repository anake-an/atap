using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atap.Core.Placement;

namespace Atap.Core.Layout;

/// <summary>
/// layout.json: where the notch sits, which widget instances are enabled, what each state shows, who
/// wins the compact view, per-instance settings and profiles. The top level is the Default profile.
/// </summary>
public sealed class LayoutDocument
{
    /// <summary>
    /// 1: widgets, states, compact priority, settings.
    /// 2: adds <see cref="Placement"/> (missing means top centre on the primary monitor).
    /// 3: adds <see cref="Profiles"/>, and the panel holds pages instead of placements.
    /// 4: an untouched 1.0 panel page (music and missed alerts) becomes the new default, which follows the hover view.
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings = new Dictionary<string, JsonElement>();

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Machine-specific: never synced, and the same for every profile.</summary>
    public NotchPlacement Placement { get; set; } = new();

    /// <summary>Instance ids to create. Instances not listed are never created.</summary>
    public List<string> EnabledWidgets { get; set; } = [];

    public StateLayouts States { get; set; } = new();

    /// <summary>Instance ids in the order they claim the compact view when live.</summary>
    public List<string> CompactPriority { get; set; } = [];

    /// <summary>Setting values keyed by instance id, then setting key. The same for every profile.</summary>
    public Dictionary<string, Dictionary<string, JsonElement>> WidgetSettings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Profiles other than Default. Each may override the enabled widgets, the states and the compact priority.</summary>
    public List<LayoutProfile> Profiles { get; set; } = [];

    /// <summary>Fields this version does not know. Kept so a newer file round-trips.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public IReadOnlyDictionary<string, JsonElement> GetWidgetSettings(string instanceId) =>
        WidgetSettings.TryGetValue(instanceId, out var values) ? values : NoSettings;

    public void SetWidgetSetting<T>(string instanceId, string key, T value)
    {
        if (!WidgetSettings.TryGetValue(instanceId, out var values))
        {
            values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            WidgetSettings[instanceId] = values;
        }
        values[key] = JsonSerializer.SerializeToElement(value);
    }

    public LayoutProfile? FindProfile(string? profileId) =>
        profileId is null ? null : Profiles.Find(p => string.Equals(p.Id, profileId, StringComparison.Ordinal));

    /// <summary>Replaces nulls left by a hand-edited file with empty values.</summary>
    public void Normalize()
    {
        Placement ??= new();
        Placement.Normalize();
        EnabledWidgets = NormalizeIds(EnabledWidgets) ?? [];
        CompactPriority = NormalizeIds(CompactPriority) ?? [];
        States ??= new();
        States.Normalize();
        WidgetSettings = WidgetSettings?
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new(StringComparer.Ordinal);

        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        Profiles = (Profiles ?? [])
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id) && profileIds.Add(p.Id))
            .ToList();
        foreach (var profile in Profiles)
        {
            // "default" names the top level. A profile with that id would hide it, so it gets a free id instead.
            if (string.Equals(profile.Id, EffectiveLayout.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
                profile.Id = FreeProfileId(profileIds);
            profile.Normalize();
        }
    }

    private static string FreeProfileId(HashSet<string> taken)
    {
        for (var n = 2; ; n++)
        {
            var id = string.Create(CultureInfo.InvariantCulture, $"{EffectiveLayout.DefaultProfileId}-{n}");
            if (taken.Add(id))
                return id;
        }
    }

    internal static List<string>? NormalizeIds(List<string>? ids) =>
        ids?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
}

/// <summary>A named profile. A field left out (null) is inherited from the Default profile at the top level.</summary>
public sealed class LayoutProfile
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string>? EnabledWidgets { get; set; }

    public StateLayouts? States { get; set; }

    public List<string>? CompactPriority { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    internal void Normalize()
    {
        Name ??= string.Empty;
        EnabledWidgets = LayoutDocument.NormalizeIds(EnabledWidgets);
        CompactPriority = LayoutDocument.NormalizeIds(CompactPriority);
        States?.Normalize();
    }
}
