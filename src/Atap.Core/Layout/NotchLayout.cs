using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Layout;

/// <summary>What each notch state shows. The panel has pages, the other states have one list of placements.</summary>
public sealed class StateLayouts
{
    public StateLayout Idle { get; set; } = new();

    public StateLayout Compact { get; set; } = new();

    public StateLayout Expanded { get; set; } = new();

    public PanelLayout Panel { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Every placement in a state. For the panel, every page in order.</summary>
    public IReadOnlyList<WidgetPlacement> PlacementsFor(NotchState state) => state switch
    {
        NotchState.Idle => Idle.Placements,
        NotchState.Compact => Compact.Placements,
        NotchState.Expanded => Expanded.Placements,
        NotchState.Panel => Panel.Pages.SelectMany(p => p.Placements).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    internal void Normalize()
    {
        Idle = StateLayout.Normalized(Idle);
        Compact = StateLayout.Normalized(Compact);
        Expanded = StateLayout.Normalized(Expanded);
        Panel ??= new();
        Panel.Normalize();
    }
}

/// <summary>What one notch state shows.</summary>
public sealed class StateLayout
{
    public List<WidgetPlacement> Placements { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    internal static StateLayout Normalized(StateLayout? layout)
    {
        layout ??= new();
        layout.Placements = NormalizePlacements(layout.Placements);
        return layout;
    }

    internal static List<WidgetPlacement> NormalizePlacements(List<WidgetPlacement>? placements) =>
        placements?.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.WidgetId)).ToList() ?? [];
}
