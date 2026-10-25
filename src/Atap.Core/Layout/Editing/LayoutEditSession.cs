using System.Text.Json;
using Atap.Core.Widgets;

namespace Atap.Core.Layout.Editing;

public enum EditResult
{
    Applied,

    /// <summary>Nothing to do, for example moving the first tile left.</summary>
    Unchanged,

    /// <summary>The widget does not support that state or size, or the place does not exist.</summary>
    Rejected,
}

/// <summary>A place the editor shows: the idle notch, the open notch, or one panel page.</summary>
public readonly record struct EditSpot(NotchState State, int Page = 0)
{
    /// <summary>The states the editor arranges. Compact shows whoever is live, ordered by the priority list.</summary>
    public static IReadOnlyList<NotchState> EditableStates { get; } = [NotchState.Idle, NotchState.Expanded, NotchState.Panel];
}

/// <summary>What the active profile shows: the part of layout.json the editor changes.</summary>
public sealed class LayoutDraft
{
    public List<string> EnabledWidgets { get; set; } = [];

    public StateLayouts States { get; set; } = new();

    public List<string> CompactPriority { get; set; } = [];
}

/// <summary>
/// One run of the layout editor, pure. It works on a copy of what the active profile shows, so nothing reaches
/// layout.json until <see cref="ApplyTo"/>, in one write. Every change can be undone. A widget is only placed in
/// a state and at a size it supports.
/// </summary>
public sealed class LayoutEditSession
{
    public const int MaxPages = 6;

    private readonly WidgetCatalog _catalog;
    private readonly string _original;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private LayoutDraft _draft;

    /// <param name="document">layout.json as it is now. It is copied, never changed.</param>
    /// <param name="profileId">The profile being edited. Unknown ids and the Default profile edit the top level.</param>
    public LayoutEditSession(LayoutDocument document, string profileId, WidgetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        _catalog = catalog;
        ProfileId = document.FindProfile(profileId) is { } profile ? profile.Id : EffectiveLayout.DefaultProfileId;
        var source = document.FindProfile(ProfileId);
        _draft = Copy(new LayoutDraft
        {
            EnabledWidgets = source?.EnabledWidgets ?? document.EnabledWidgets,
            States = source?.States ?? document.States,
            CompactPriority = source?.CompactPriority ?? document.CompactPriority,
        });
        _draft.States.Panel.Normalize();
        _original = Serialize(_draft);

        // Ids in use anywhere when editing starts are never given to a new widget, even after it is removed in
        // this session. Machine state such as a script's approval is keyed by id, so a reused id would inherit it.
        _takenIds = document.EnabledWidgets
            .Concat(document.Profiles.SelectMany(p => p.EnabledWidgets ?? []))
            .Concat(document.WidgetSettings.Keys)
            .ToHashSet(StringComparer.Ordinal);
    }

    private readonly HashSet<string> _takenIds;

    public string ProfileId { get; }

    public LayoutDraft Draft => _draft;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when the draft differs from what was there when the editor opened.</summary>
    public bool IsDirty => Serialize(_draft) != _original;

    public IReadOnlyList<PanelPage> Pages => _draft.States.Panel.Pages;

    /// <summary>The tiles in a place, in order.</summary>
    public IReadOnlyList<WidgetPlacement> PlacementsAt(EditSpot spot) =>
        ListAt(spot) is { } list ? list.OrderBy(p => p.Order).ToList() : [];

    /// <summary>True when <paramref name="typeId"/> can go in <paramref name="spot"/> right now.</summary>
    public bool CanAdd(string typeId, EditSpot spot) =>
        _catalog.TryGet(typeId, out var descriptor)
        && descriptor.SupportedStates.Contains(spot.State)
        && ListAt(spot) is { } list
        && (descriptor.AllowsMultipleInstances || !list.Any(p => p.WidgetId == typeId));

    /// <summary>
    /// Adds a tile at <paramref name="size"/> or the widget's smallest size. It goes at the end, or before the tile
    /// at <paramref name="at"/> when that is given.
    /// </summary>
    public EditResult Add(string typeId, EditSpot spot, WidgetSize? size = null, int? at = null)
    {
        if (!CanAdd(typeId, spot) || !_catalog.TryGet(typeId, out var descriptor))
            return EditResult.Rejected;
        var chosen = size ?? descriptor.SupportedSizes.Min();
        if (!descriptor.SupportedSizes.Contains(chosen))
            return EditResult.Rejected;

        return Change(draft =>
        {
            var instanceId = descriptor.AllowsMultipleInstances ? NewInstanceId(draft, typeId) : typeId;
            var list = ListAt(draft, spot)!;
            list.Add(new WidgetPlacement { WidgetId = instanceId, Size = chosen, Order = list.Count, Slot = PlacementSlot.Center });
            Renumber(list);
            if (at is { } index)
            {
                var ordered = list.OrderBy(p => p.Order).ToList();
                var added = ordered[^1];
                ordered.RemoveAt(ordered.Count - 1);
                ordered.Insert(Math.Clamp(index, 0, ordered.Count), added);
                list.Clear();
                list.AddRange(ordered.Select((p, i) => p with { Order = i }));
            }
            if (!draft.EnabledWidgets.Contains(instanceId))
                draft.EnabledWidgets.Add(instanceId);
            if (descriptor.SupportedStates.Contains(NotchState.Compact))
                AddCompact(draft, instanceId);
        });
    }

    /// <summary>Takes a tile off. A widget placed nowhere else stops, unless it works without a place.</summary>
    public EditResult Remove(EditSpot spot, int index)
    {
        if (PlacementAt(spot, index) is null)
            return EditResult.Rejected;
        return Change(draft =>
        {
            var list = ListAt(draft, spot)!;
            var placement = list.OrderBy(p => p.Order).ElementAt(index);
            list.Remove(placement);
            Renumber(list);
            ForgetIfUnplaced(draft, placement.WidgetId);
        });
    }

    public EditResult Resize(EditSpot spot, int index, WidgetSize size)
    {
        if (PlacementAt(spot, index) is not { } placement || !_catalog.TryResolve(placement.WidgetId, out var descriptor))
            return EditResult.Rejected;
        if (!descriptor.SupportedSizes.Contains(size))
            return EditResult.Rejected;
        if (placement.Size == size)
            return EditResult.Unchanged;
        return Change(draft => Replace(draft, spot, index, p => p with { Size = size }));
    }

    /// <summary>Moves a tile one or more places earlier (negative) or later (positive).</summary>
    public EditResult Move(EditSpot spot, int index, int delta)
    {
        if (PlacementAt(spot, index) is null)
            return EditResult.Rejected;
        var count = ListAt(spot)!.Count;
        var target = Math.Clamp(index + delta, 0, count - 1);
        if (target == index)
            return EditResult.Unchanged;
        return Change(draft =>
        {
            var list = ListAt(draft, spot)!;
            var ordered = list.OrderBy(p => p.Order).ToList();
            var moving = ordered[index];
            ordered.RemoveAt(index);
            ordered.Insert(target, moving);
            list.Clear();
            list.AddRange(ordered.Select((p, i) => p with { Order = i }));
        });
    }

    /// <summary>
    /// Moves a tile to the place <paramref name="target"/> in <paramref name="to"/>: elsewhere in the same place, or
    /// onto another panel page. A tile keeps its state, and a page never gets the same widget twice.
    /// </summary>
    public EditResult MoveTo(EditSpot from, int index, EditSpot to, int target)
    {
        if (PlacementAt(from, index) is not { } placement || ListAt(to) is not { } destination || from.State != to.State)
            return EditResult.Rejected;
        if (from == to)
            return Move(from, index, Math.Clamp(target, 0, destination.Count - 1) - index);
        if (destination.Any(p => p.WidgetId == placement.WidgetId))
            return EditResult.Rejected;

        return Change(draft =>
        {
            var source = ListAt(draft, from)!;
            var moving = source.OrderBy(p => p.Order).ElementAt(index);
            source.Remove(moving);
            Renumber(source);
            var list = ListAt(draft, to)!;
            var ordered = list.OrderBy(p => p.Order).ToList();
            ordered.Insert(Math.Clamp(target, 0, ordered.Count), moving);
            list.Clear();
            list.AddRange(ordered.Select((p, i) => p with { Order = i }));
        });
    }

    /// <summary>Adds an empty panel page at the end, up to <see cref="MaxPages"/>.</summary>
    public EditResult AddPage(string name)
    {
        if (Pages.Count >= MaxPages)
            return EditResult.Rejected;
        return Change(draft =>
        {
            var pages = draft.States.Panel.Pages;
            var number = pages.Count + 1;
            while (pages.Any(p => p.Id == $"page-{number}"))
                number++;
            pages.Add(new PanelPage { Id = $"page-{number}", Name = name });
        });
    }

    /// <summary>Removes a panel page and its tiles. The panel always keeps one page.</summary>
    public EditResult RemovePage(int page)
    {
        if (Pages.Count <= 1 || page < 0 || page >= Pages.Count)
            return EditResult.Rejected;
        return Change(draft =>
        {
            var removed = draft.States.Panel.Pages[page];
            draft.States.Panel.Pages.RemoveAt(page);
            foreach (var placement in removed.Placements)
                ForgetIfUnplaced(draft, placement.WidgetId);
        });
    }

    /// <summary>Moves a widget up (negative) or down the compact priority list. First wins.</summary>
    /// <remarks>
    /// Only widgets that are on count as neighbours. The list also names widgets a new install has not turned on
    /// yet, so they slot in later, and stepping past those would look like nothing happened.
    /// </remarks>
    public EditResult MovePriority(string instanceId, int delta)
    {
        var enabled = EnabledPriority;
        var index = enabled.IndexOf(instanceId);
        if (index < 0)
            return EditResult.Rejected;
        var target = Math.Clamp(index + delta, 0, enabled.Count - 1);
        if (target == index)
            return EditResult.Unchanged;
        var neighbour = enabled[target];
        return Change(draft =>
        {
            draft.CompactPriority.Remove(instanceId);
            var at = draft.CompactPriority.IndexOf(neighbour);
            draft.CompactPriority.Insert(delta < 0 ? at : at + 1, instanceId);
        });
    }

    /// <summary>The compact priority of the widgets that are on, highest first.</summary>
    public List<string> EnabledPriority => _draft.CompactPriority.Where(_draft.EnabledWidgets.Contains).ToList();

    /// <summary>Goes back to the layout a new install has. Undo brings the old one back.</summary>
    public EditResult ResetToDefault()
    {
        var defaults = DefaultLayout.Create();
        var fresh = new LayoutDraft
        {
            EnabledWidgets = defaults.EnabledWidgets,
            States = defaults.States,
            CompactPriority = defaults.CompactPriority,
        };
        if (Serialize(fresh) == Serialize(_draft))
            return EditResult.Unchanged;
        return Change(draft =>
        {
            draft.EnabledWidgets = Copy(fresh).EnabledWidgets;
            draft.States = Copy(fresh).States;
            draft.CompactPriority = Copy(fresh).CompactPriority;
        });
    }

    public bool Undo() => Step(_undo, _redo);

    public bool Redo() => Step(_redo, _undo);

    /// <summary>
    /// Writes the draft into <paramref name="document"/>: the top level for the Default profile, otherwise the
    /// profile's own fields, so other profiles keep inheriting what they did. Copies, so later edits never leak.
    /// </summary>
    public void ApplyTo(LayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var copy = Copy(_draft);
        if (document.FindProfile(ProfileId) is { } profile)
        {
            profile.EnabledWidgets = copy.EnabledWidgets;
            profile.States = copy.States;
            profile.CompactPriority = copy.CompactPriority;
            return;
        }
        document.EnabledWidgets = copy.EnabledWidgets;
        document.States = copy.States;
        document.CompactPriority = copy.CompactPriority;
    }

    private WidgetPlacement? PlacementAt(EditSpot spot, int index)
    {
        var placements = PlacementsAt(spot);
        return index >= 0 && index < placements.Count ? placements[index] : null;
    }

    private List<WidgetPlacement>? ListAt(EditSpot spot) => ListAt(_draft, spot);

    private static List<WidgetPlacement>? ListAt(LayoutDraft draft, EditSpot spot) => spot.State switch
    {
        NotchState.Idle => draft.States.Idle.Placements,
        NotchState.Expanded => draft.States.Expanded.Placements,
        NotchState.Panel when spot.Page >= 0 && spot.Page < draft.States.Panel.Pages.Count => draft.States.Panel.Pages[spot.Page].Placements,
        _ => null,
    };

    private EditResult Change(Action<LayoutDraft> change)
    {
        var before = Serialize(_draft);
        var draft = Copy(_draft);
        change(draft);
        if (Serialize(draft) == before)
            return EditResult.Unchanged;
        _undo.Push(before);
        _redo.Clear();
        _draft = draft;
        return EditResult.Applied;
    }

    private bool Step(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0)
            return false;
        to.Push(Serialize(_draft));
        _draft = Deserialize(from.Pop());
        return true;
    }

    private static void Replace(LayoutDraft draft, EditSpot spot, int index, Func<WidgetPlacement, WidgetPlacement> change)
    {
        var list = ListAt(draft, spot)!;
        var placement = list.OrderBy(p => p.Order).ElementAt(index);
        list[list.IndexOf(placement)] = change(placement);
    }

    private static void Renumber(List<WidgetPlacement> list)
    {
        var ordered = list.OrderBy(p => p.Order).Select((p, i) => p with { Order = i }).ToList();
        list.Clear();
        list.AddRange(ordered);
    }

    private void ForgetIfUnplaced(LayoutDraft draft, string instanceId)
    {
        if (IsPlaced(draft, instanceId))
            return;
        if (_catalog.TryResolve(instanceId, out var descriptor) && descriptor.WorksWithoutPlacement)
            return;
        draft.EnabledWidgets.Remove(instanceId);
        draft.CompactPriority.Remove(instanceId);
        draft.States.Compact.Placements.RemoveAll(p => p.WidgetId == instanceId);
    }

    /// <summary>Compact shows the live widget with the highest priority, on both sides of the camera.</summary>
    private static void AddCompact(LayoutDraft draft, string instanceId)
    {
        if (!draft.CompactPriority.Contains(instanceId))
            draft.CompactPriority.Add(instanceId);
        if (draft.States.Compact.Placements.Any(p => p.WidgetId == instanceId))
            return;
        draft.States.Compact.Placements.Add(new WidgetPlacement { WidgetId = instanceId, Size = WidgetSize.Small, Slot = PlacementSlot.Left });
        draft.States.Compact.Placements.Add(new WidgetPlacement { WidgetId = instanceId, Size = WidgetSize.Small, Slot = PlacementSlot.Right });
    }

    private static bool IsPlaced(LayoutDraft draft, string instanceId) =>
        draft.States.Idle.Placements
            .Concat(draft.States.Expanded.Placements)
            .Concat(draft.States.Panel.Pages.SelectMany(p => p.Placements))
            .Any(p => p.WidgetId == instanceId);

    /// <summary>"json-url" for the first, then "json-url#2", "json-url#3". Never an id that was in use when editing started.</summary>
    private string NewInstanceId(LayoutDraft draft, string typeId)
    {
        if (IsFree(draft, typeId))
            return typeId;
        for (var n = 2; ; n++)
        {
            var id = new WidgetInstanceId(typeId, n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString();
            if (IsFree(draft, id))
                return id;
        }
    }

    private bool IsFree(LayoutDraft draft, string id) =>
        !draft.EnabledWidgets.Contains(id) && !IsPlaced(draft, id) && !_takenIds.Contains(id);

    private static string Serialize(LayoutDraft draft) => JsonSerializer.Serialize(draft, LayoutStore.JsonOptions);

    private static LayoutDraft Deserialize(string json) => JsonSerializer.Deserialize<LayoutDraft>(json, LayoutStore.JsonOptions)!;

    private static LayoutDraft Copy(LayoutDraft draft) => Deserialize(Serialize(draft));
}
