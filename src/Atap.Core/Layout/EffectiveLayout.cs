namespace Atap.Core.Layout;

/// <summary>A panel page as resolved for the active profile.</summary>
public sealed record EffectivePage(string Id, string Name, IReadOnlyList<WidgetPlacement> Placements);

/// <summary>
/// What the active profile shows, with inherited fields filled in. A snapshot: later edits to the
/// document do not change it, so two snapshots can be compared.
/// </summary>
public sealed record EffectiveLayout(
    string ProfileId,
    IReadOnlyList<string> EnabledWidgets,
    IReadOnlyList<string> CompactPriority,
    IReadOnlyList<WidgetPlacement> Idle,
    IReadOnlyList<WidgetPlacement> Compact,
    IReadOnlyList<WidgetPlacement> Expanded,
    IReadOnlyList<EffectivePage> PanelPages)
{
    /// <summary>The id of the top-level profile.</summary>
    public const string DefaultProfileId = "default";

    /// <summary>Copies the given fields into a snapshot.</summary>
    public static EffectiveLayout Snapshot(string profileId, IEnumerable<string> enabledWidgets, IEnumerable<string> compactPriority, StateLayouts states) => new(
        profileId,
        enabledWidgets.ToList(),
        compactPriority.ToList(),
        states.Idle.Placements.ToList(),
        states.Compact.Placements.ToList(),
        states.Expanded.Placements.ToList(),
        states.Panel.Pages.Select(p => new EffectivePage(p.Id, p.Name, p.Placements.ToList())).ToList());

    /// <summary>Every placement in a state. For the panel, every page in order.</summary>
    public IReadOnlyList<WidgetPlacement> PlacementsFor(NotchState state) => state switch
    {
        NotchState.Idle => Idle,
        NotchState.Compact => Compact,
        NotchState.Expanded => Expanded,
        NotchState.Panel => PanelPages.SelectMany(p => p.Placements).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    /// <summary>True when both show the same thing, so views need no rebuild.</summary>
    public bool HasSameContent(EffectiveLayout other) =>
        string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal)
        && EnabledWidgets.SequenceEqual(other.EnabledWidgets, StringComparer.Ordinal)
        && CompactPriority.SequenceEqual(other.CompactPriority, StringComparer.Ordinal)
        && Idle.SequenceEqual(other.Idle)
        && Compact.SequenceEqual(other.Compact)
        && Expanded.SequenceEqual(other.Expanded)
        && PanelPages.Count == other.PanelPages.Count
        && PanelPages.Zip(other.PanelPages).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name && pair.First.Placements.SequenceEqual(pair.Second.Placements));
}
