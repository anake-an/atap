using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Layout.Editing;
using Atap.Core.Localization;
using Atap.Core.Notch;
using Atap.Core.Widgets;
using Atap.Ui.Preview;

namespace Atap.Features.LayoutEditor;

/// <summary>A tab in the editor: one notch state, or the compact priority list.</summary>
public enum EditorTab
{
    Idle,
    Open,
    Panel,
    Priority,
}

/// <summary>A size button for the selected widget. Only the sizes the widget supports are shown.</summary>
public sealed record SizeChip(WidgetSize Size, string Label, string Name, bool IsSelected, IRelayCommand Pick);

/// <summary>One placed widget, with its size buttons and buttons to move or remove it.</summary>
public sealed record TileRow(
    string Name,
    string Detail,
    IReadOnlyList<SizeChip> Sizes,
    bool CanMoveEarlier,
    bool CanMoveLater,
    IRelayCommand MoveEarlier,
    IRelayCommand MoveLater,
    IRelayCommand Remove)
{
    public bool HasDetail => Detail.Length > 0;

    public bool HasSizes => Sizes.Count > 0;
}

/// <summary>A small box per size in the gallery, as wide as the widget is at that size.</summary>
public sealed record SizeMark(string Label, string Name, double Width);

/// <summary>A widget in the Add widgets list, with a preview of how it looks on the notch.</summary>
public sealed record GalleryItem(string TypeId, string Name, string Description, bool CanAdd, string Note, IRelayCommand Add)
{
    public bool HasNote => Note.Length > 0;

    /// <summary>The widget at the size it is added at, with sample content.</summary>
    public PreviewTile? Preview { get; init; }

    /// <summary>One plain line about it.</summary>
    public string Line { get; init; } = string.Empty;

    public IReadOnlyList<SizeMark> Sizes { get; init; } = [];

    /// <summary>"Small or Medium", for a screen reader.</summary>
    public string SizesText { get; init; } = string.Empty;
}

public sealed record GalleryGroup(string Name, IReadOnlyList<GalleryItem> Items);

/// <summary>A panel page tab.</summary>
public sealed record PageTab(int Index, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A state tab: "Small notch", "When you point at it", "When you click it" or "When something is live".</summary>
public sealed record TabOption(EditorTab Tab, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A row in the compact priority list, highest first.</summary>
public sealed record PriorityRow(string Rank, string Name, bool CanMoveUp, bool CanMoveDown, IRelayCommand MoveUp, IRelayCommand MoveDown);

/// <summary>
/// The editor window's model over a <see cref="LayoutEditSession"/>. The middle of the editor is a picture of the
/// notch in the chosen state, which widgets are dragged onto, within and off. Every change, by mouse or keyboard,
/// is one session call, so Undo and Redo cover it. Nothing is saved until Done, which makes one write and keeps the
/// old layout in layout.prev.json. A change to layout.json from outside while editing offers a reload instead.
/// </summary>
public sealed partial class LayoutEditorViewModel : ObservableObject, IDisposable
{
    // Widths of the size boxes in the gallery, in the same proportion as the notch's columns.
    private const double SizeMarkUnit = 20;

    private readonly ILayoutService _layout;
    private readonly WidgetCatalog _catalog;
    private readonly ILocalizer _text;
    private readonly LayoutBackup _backup;
    private LayoutEditSession _session;
    private EditorTab _tab = EditorTab.Open;
    private int _page;
    private int _selected = -1;
    private string _search = string.Empty;
    private bool _committing;
    private bool _changedOutside;
    private string? _messageKey;
    private IReadOnlyList<WidgetSize> _baseSizes = [];
    private EditorDrag? _drag;
    private EditSpot? _dropSpot;
    private int _dropIndex;

    internal LayoutEditorViewModel(ILayoutService layout, WidgetCatalog catalog, ILocalizer text, LayoutBackup backup)
    {
        _layout = layout;
        _catalog = catalog;
        _text = text;
        _backup = backup;
        _session = new LayoutEditSession(layout.Document, layout.ActiveProfileId, catalog);
        _layout.Changed += OnLayoutChanged;
        _text.LanguageChanged += OnLanguageChanged;
        Rebuild();
    }

    /// <summary>Raised when Done or Cancel should close the window.</summary>
    public event EventHandler? CloseRequested;

    public EditorTab Tab
    {
        get => _tab;
        set
        {
            if (!SetProperty(ref _tab, value))
                return;
            _selected = -1;
            Rebuild();
        }
    }

    public IReadOnlyList<TabOption> Tabs { get; private set; } = [];

    public TabOption? SelectedTab
    {
        get => Tabs.FirstOrDefault(t => t.Tab == _tab);
        set
        {
            if (value is not null)
                Tab = value.Tab;
        }
    }

    public PageTab? SelectedPage
    {
        get => _page < Pages.Count ? Pages[_page] : null;
        set
        {
            if (value is null || value.Index == _page)
                return;
            _page = value.Index;
            _selected = -1;
            Rebuild();
        }
    }

    public bool IsPanel => _tab == EditorTab.Panel;

    public bool IsPriority => _tab == EditorTab.Priority;

    public bool IsArranging => _tab != EditorTab.Priority;

    /// <summary>One line on what the chosen state is, such as "The notch grows a little when you point at it."</summary>
    public string TabHint => _text[NotchPreviews.HintKey(StateOf(_tab))];

    /// <summary>The notch in the chosen state, as it will look, with a marker where a dragged widget will land.</summary>
    public NotchPicture Picture { get; private set; } = new(NotchState.Expanded, string.Empty, string.Empty, [], string.Empty);

    /// <summary>The placed widgets in this state or page, in order. The selected one gets the size and move buttons.</summary>
    public IReadOnlyList<TileRow> Tiles { get; private set; } = [];

    public bool HasNoTiles => IsArranging && Tiles.Count == 0;

    /// <summary>The selected widget's place in <see cref="Tiles"/>, or -1.</summary>
    public int SelectedIndex => _selected;

    /// <summary>The selected widget's buttons, or null when nothing is selected.</summary>
    public TileRow? Selected => _selected >= 0 && _selected < Tiles.Count ? Tiles[_selected] : null;

    public bool HasSelection => Selected is not null;

    public bool ShowsSelectHint => IsArranging && !HasSelection && Tiles.Count > 0;

    public IReadOnlyList<PageTab> Pages { get; private set; } = [];

    public bool CanAddPage => _session.Pages.Count < LayoutEditSession.MaxPages;

    public bool CanRemovePage => _session.Pages.Count > 1;

    public IReadOnlyList<GalleryGroup> Gallery { get; private set; } = [];

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
                Rebuild();
        }
    }

    public bool HasNoSearch => _search.Length == 0;

    public IReadOnlyList<PriorityRow> Priority { get; private set; } = [];

    public bool CanUndo => _session.CanUndo;

    public bool CanRedo => _session.CanRedo;

    public bool IsDirty => _session.IsDirty;

    /// <summary>layout.json changed outside Atap while editing. Reload starts again from the new file.</summary>
    public bool ChangedOutside
    {
        get => _changedOutside;
        private set => SetProperty(ref _changedOutside, value);
    }

    /// <summary>"Not saved yet", or what went wrong.</summary>
    public string StatusText => _messageKey is { } key ? _text[key]
        : _session.IsDirty ? _text["layouteditor.status.unsaved"]
        : _text["layouteditor.status.noChanges"];

    /// <summary>"Editing the Work profile", or empty for the Default profile.</summary>
    public string ProfileText => _session.ProfileId == EffectiveLayout.DefaultProfileId
        ? string.Empty
        : _text.Format("layouteditor.profile", ("name", _layout.Document.FindProfile(_session.ProfileId)?.Name ?? _session.ProfileId));

    /// <summary>The place shown: the small notch, the open notch, or the panel page that is open.</summary>
    public EditSpot Spot => SpotOf(_tab, _page);

    public bool IsDragging => _drag is not null;

    /// <summary>True while a widget already on the notch is dragged, so the remove area shows.</summary>
    public bool IsDraggingPlaced => _drag is { IsNew: false };

    /// <summary>What letting go does right now, such as "Let go to add it here".</summary>
    public string DragHint => _drag is null ? string.Empty : HintFor(_drag);

    internal LayoutEditSession Session => _session;

    public void Dispose()
    {
        _layout.Changed -= OnLayoutChanged;
        _text.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>The place a panel page tab stands for.</summary>
    public static EditSpot PageSpot(int page) => new(NotchState.Panel, page);

    /// <summary>Selects a placed widget by its place, or nothing with -1.</summary>
    public void Select(int index)
    {
        var next = index >= 0 && index < Tiles.Count ? index : -1;
        if (next == _selected)
            return;
        _selected = next;
        RebuildPicture();
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowsSelectHint));
    }

    /// <summary>Moves the selected widget earlier (negative) or later. It stays selected.</summary>
    public EditResult MoveSelected(int delta)
    {
        if (Selected is null)
            return EditResult.Unchanged;
        var target = Math.Clamp(_selected + delta, 0, Tiles.Count - 1);
        var result = _session.Move(Spot, _selected, delta);
        if (result == EditResult.Applied)
            _selected = target;
        return Apply(result);
    }

    /// <summary>Removes the selected widget. Undo brings it back.</summary>
    public EditResult RemoveSelected() => Selected is null ? EditResult.Unchanged : RemoveAt(_selected);

    /// <summary>Where a widget dropped at a point in the picture's tiles goes, counting the tiles as they are now.</summary>
    public int DropIndexAt(double x, double y) =>
        PreviewLayout.DropIndex(PreviewLayout.Cells(Picture.Shape, _baseSizes, PreviewMetrics.Notch), x, y);

    /// <summary>Starts a drag. Nothing changes until <see cref="Drop"/>.</summary>
    public void BeginDrag(EditorDrag drag)
    {
        if (!IsArranging)
            return;
        _drag = drag;
        _dropSpot = null;
        _dropIndex = 0;
        RebuildPicture();
        RaiseDrag();
    }

    /// <summary>
    /// The pointer moved. <paramref name="spot"/> is where it is, or null outside the notch. The picture shows the
    /// result of letting go there.
    /// </summary>
    public void DragOver(EditSpot? spot, int index)
    {
        if (_drag is null || (spot == _dropSpot && index == _dropIndex))
            return;
        _dropSpot = spot;
        _dropIndex = index;
        RebuildPicture();
        OnPropertyChanged(nameof(DragHint));
    }

    /// <summary>Lets go: adds, moves or removes in one change. Outside the notch a new widget is left out.</summary>
    public EditResult Drop()
    {
        if (_drag is not { } drag)
            return EditResult.Unchanged;
        var spot = _dropSpot;
        var index = _dropIndex;
        _drag = null;
        _dropSpot = null;

        if (spot is { } to && !CanDrop(drag, to))
        {
            Rebuild();
            return EditResult.Rejected;
        }
        var result = EditorDrop.Apply(_session, drag, spot, index);
        if (result == EditResult.Applied && spot is { } landed)
        {
            // The dropped widget stays selected where it landed, on its page.
            if (landed.State == NotchState.Panel)
                _page = landed.Page;
            var count = _session.PlacementsAt(landed).Count;
            _selected = Math.Clamp(EditorDrop.FinalIndex(drag, landed, index), 0, Math.Max(0, count - 1));
        }
        else if (result == EditResult.Applied)
        {
            _selected = -1;
        }
        _messageKey = result == EditResult.Applied && spot is null ? "layouteditor.status.removed" : null;
        Rebuild();
        RaiseDrag();
        return result;
    }

    /// <summary>Esc, or the pointer was lost: nothing changes.</summary>
    public void CancelDrag()
    {
        if (_drag is null)
            return;
        _drag = null;
        _dropSpot = null;
        RebuildPicture();
        RaiseDrag();
    }

    [RelayCommand]
    private void ShowTab(EditorTab tab) => Tab = tab;

    [RelayCommand]
    private void SelectTile(PreviewTile? tile) => Select(tile?.Index ?? -1);

    [RelayCommand]
    private void Undo()
    {
        _session.Undo();
        _messageKey = null;
        Rebuild();
    }

    [RelayCommand]
    private void Redo()
    {
        _session.Redo();
        _messageKey = null;
        Rebuild();
    }

    [RelayCommand]
    private void Reset() => Apply(_session.ResetToDefault());

    [RelayCommand]
    private void AddPage()
    {
        if (Apply(_session.AddPage(_text.Format("layouteditor.pageName", ("number", (_session.Pages.Count + 1).ToString(CultureInfo.CurrentCulture))))) == EditResult.Applied)
        {
            _page = _session.Pages.Count - 1;
            _selected = -1;
            Rebuild();
        }
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (Apply(_session.RemovePage(_page)) == EditResult.Applied)
        {
            _page = Math.Min(_page, _session.Pages.Count - 1);
            _selected = -1;
            Rebuild();
        }
    }

    /// <summary>Saves in one write, after keeping the old layout in layout.prev.json.</summary>
    [RelayCommand]
    private void Done()
    {
        if (_session.IsDirty)
        {
            _backup.TrySave(_layout.Document);
            _committing = true;
            LayoutWriteResult result;
            try
            {
                result = _layout.Update(_session.ApplyTo);
            }
            finally
            {
                _committing = false;
            }
            if (result != LayoutWriteResult.Applied)
            {
                _messageKey = "layouteditor.status.notSaved";
                OnPropertyChanged(nameof(StatusText));
                return;
            }
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Starts again from layout.json as it is now. Edits made here are dropped.</summary>
    [RelayCommand]
    private void Reload()
    {
        _session = new LayoutEditSession(_layout.Document, _layout.ActiveProfileId, _catalog);
        ChangedOutside = false;
        _page = 0;
        _selected = -1;
        Rebuild();
    }

    private static EditSpot SpotOf(EditorTab tab, int page) => tab switch
    {
        EditorTab.Idle => new EditSpot(NotchState.Idle),
        EditorTab.Panel => new EditSpot(NotchState.Panel, page),
        _ => new EditSpot(NotchState.Expanded),
    };

    private static NotchState StateOf(EditorTab tab) => tab switch
    {
        EditorTab.Idle => NotchState.Idle,
        EditorTab.Panel => NotchState.Panel,
        EditorTab.Priority => NotchState.Compact,
        _ => NotchState.Expanded,
    };

    private EditResult Apply(EditResult result)
    {
        _messageKey = result == EditResult.Rejected ? "layouteditor.status.rejected" : null;
        Rebuild();
        return result;
    }

    private EditResult RemoveAt(int index)
    {
        var result = _session.Remove(Spot, index);
        if (result == EditResult.Applied)
            _selected = -1;
        Apply(result);
        if (result == EditResult.Applied)
        {
            _messageKey = "layouteditor.status.removed";
            OnPropertyChanged(nameof(StatusText));
        }
        return result;
    }

    private bool CanDrop(EditorDrag drag, EditSpot spot)
    {
        if (drag.NewTypeId is { } typeId)
            return _session.CanAdd(typeId, spot);
        if (drag.From.State != spot.State)
            return false;
        if (drag.From == spot)
            return true;
        var moving = _session.PlacementsAt(drag.From).ElementAtOrDefault(drag.FromIndex);
        return moving is not null && _session.PlacementsAt(spot).All(p => p.WidgetId != moving.WidgetId);
    }

    private string HintFor(EditorDrag drag)
    {
        if (_dropSpot is not { } spot)
            return _text[drag.IsNew ? "layouteditor.drag.cancel" : "layouteditor.drag.remove"];
        if (!CanDrop(drag, spot))
        {
            return _text[drag.NewTypeId is { } typeId && _catalog.TryGet(typeId, out var d) && !d.SupportedStates.Contains(spot.State)
                ? "layouteditor.notHere"
                : "layouteditor.alreadyHere"];
        }
        if (spot != Spot && spot.Page < Pages.Count)
            return _text.Format("layouteditor.drag.page", ("page", Pages[spot.Page].Name));
        return _text[drag.IsNew ? "layouteditor.drag.add" : "layouteditor.drag.move"];
    }

    private void RaiseDrag()
    {
        OnPropertyChanged(nameof(IsDragging));
        OnPropertyChanged(nameof(IsDraggingPlaced));
        OnPropertyChanged(nameof(DragHint));
    }

    private void Rebuild()
    {
        _page = Math.Clamp(_page, 0, Math.Max(0, _session.Pages.Count - 1));
        var spot = Spot;
        var placements = IsArranging ? _session.PlacementsAt(spot) : [];
        Tiles = placements.Select((p, i) => TileFor(spot, p, i, placements.Count)).ToList();
        if (_selected >= Tiles.Count)
            _selected = Tiles.Count - 1;
        Pages = _session.Pages.Select((p, i) => new PageTab(i, p.Name)).ToList();
        Tabs = Enum.GetValues<EditorTab>().Select(t => new TabOption(t, _text[NotchPreviews.LabelKey(StateOf(t))])).ToList();
        Gallery = BuildGallery(spot);
        Priority = BuildPriority();
        BuildPicture();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Only the picture, for each step of a drag and a new selection, so the gallery is left alone.</summary>
    private void RebuildPicture()
    {
        BuildPicture();
        OnPropertyChanged(nameof(Picture));
    }

    private void BuildPicture()
    {
        var state = StateOf(_tab);
        if (!IsArranging)
        {
            Picture = CompactPicture();
            _baseSizes = [];
            return;
        }

        var spot = Spot;
        var placements = _session.PlacementsAt(spot);
        _baseSizes = placements.Select(p => p.Size).ToList();
        var tiles = placements
            .Select((p, i) => NotchPreviews.Tile(_catalog, p.WidgetId, p.Size, _text, i) with { IsSelected = _drag is null && i == _selected })
            .ToList();

        if (_drag is { } drag)
        {
            PreviewTile? marker = null;
            if (!drag.IsNew && drag.From == spot && drag.FromIndex < tiles.Count)
            {
                marker = tiles[drag.FromIndex] with { IsMarker = true, Index = -1 };
                tiles.RemoveAt(drag.FromIndex);
            }
            else if (drag.NewTypeId is { } typeId && _catalog.TryGet(typeId, out var descriptor))
            {
                marker = NotchPreviews.Tile(_catalog, typeId, descriptor.SupportedSizes.Min(), _text) with { IsMarker = true };
            }
            else if (!drag.IsNew && _session.PlacementsAt(drag.From).ElementAtOrDefault(drag.FromIndex) is { } moving)
            {
                marker = NotchPreviews.Tile(_catalog, moving.WidgetId, moving.Size, _text) with { IsMarker = true };
            }

            if (marker is not null && _dropSpot == spot && CanDrop(drag, spot))
                tiles.Insert(Math.Clamp(EditorDrop.FinalIndex(drag, spot, _dropIndex), 0, tiles.Count), marker);
        }

        Picture = new NotchPicture(state, _text[NotchPreviews.LabelKey(state)], _text[NotchPreviews.HintKey(state)], tiles, _text["layouteditor.empty"]);
    }

    /// <summary>The small notch while something is live: the widget highest in the priority list.</summary>
    private NotchPicture CompactPicture()
    {
        var top = _session.EnabledPriority.FirstOrDefault(id =>
            _catalog.TryResolve(id, out var d) && d.SupportedStates.Contains(NotchState.Compact));
        IReadOnlyList<PreviewTile> tiles = top is null ? [] : [NotchPreviews.Tile(_catalog, top, WidgetSize.Small, _text, 0)];
        return new NotchPicture(NotchState.Compact, _text[NotchPreviews.LabelKey(NotchState.Compact)], _text[NotchPreviews.HintKey(NotchState.Compact)], tiles, _text["preview.empty"]);
    }

    private TileRow TileFor(EditSpot spot, WidgetPlacement placement, int index, int count)
    {
        _catalog.TryResolve(placement.WidgetId, out var descriptor);
        var name = descriptor is null ? placement.WidgetId : _text[descriptor.NameKey];
        var detail = WidgetInstanceId.TryParse(placement.WidgetId, out var id) && id.Name is { } suffix
            ? _text.Format("layouteditor.copy", ("number", suffix))
            : string.Empty;
        var sizes = (descriptor?.SupportedSizes ?? [])
            .Order()
            .Select(size => new SizeChip(
                size,
                _text[$"layouteditor.size.{size.ToString().ToLowerInvariant()}"],
                _text[$"layouteditor.sizeName.{size.ToString().ToLowerInvariant()}"],
                size == placement.Size,
                new RelayCommand(() =>
                {
                    _selected = index;
                    Apply(_session.Resize(spot, index, size));
                })))
            .ToList();
        return new TileRow(
            name,
            detail,
            sizes.Count > 1 ? sizes : [],
            index > 0,
            index < count - 1,
            new RelayCommand(() =>
            {
                _selected = index;
                MoveSelected(-1);
            }),
            new RelayCommand(() =>
            {
                _selected = index;
                MoveSelected(1);
            }),
            new RelayCommand(() => RemoveAt(index)));
    }

    private IReadOnlyList<GalleryGroup> BuildGallery(EditSpot spot)
    {
        if (!IsArranging)
            return [];
        var words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _catalog.Descriptors
            .Select(d => (Descriptor: d, Name: _text[d.NameKey], Description: _text[d.DescriptionKey], Line: NotchPreviews.LineOf(d, _text)))
            .Where(w => words.All(word =>
                w.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                || w.Line.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                || w.Description.Contains(word, StringComparison.CurrentCultureIgnoreCase)))
            .GroupBy(w => w.Descriptor.Group)
            .OrderBy(g => g.Key)
            .Select(g => new GalleryGroup(
                _text[$"layouteditor.group.{g.Key.ToString().ToLowerInvariant()}"],
                g.OrderBy(w => w.Name, StringComparer.CurrentCulture)
                    .Select(w =>
                    {
                        var descriptor = w.Descriptor;
                        var supported = descriptor.SupportedStates.Contains(spot.State);
                        var canAdd = _session.CanAdd(descriptor.TypeId, spot);
                        var note = !supported ? _text["layouteditor.notHere"] : !canAdd ? _text["layouteditor.alreadyHere"] : string.Empty;
                        var sizes = descriptor.SupportedSizes.Order().ToList();
                        return new GalleryItem(descriptor.TypeId, w.Name, w.Description, canAdd, note,
                            new RelayCommand(() => AddFromGallery(descriptor.TypeId), () => canAdd))
                        {
                            Preview = NotchPreviews.Tile(_catalog, descriptor.TypeId, sizes.FirstOrDefault(), _text),
                            Line = w.Line,
                            Sizes = sizes.Select(s => new SizeMark(
                                _text[$"layouteditor.size.{s.ToString().ToLowerInvariant()}"],
                                _text[$"layouteditor.sizeName.{s.ToString().ToLowerInvariant()}"],
                                SizeMarkUnit * GridPacker.SpanOf(s))).ToList(),
                            SizesText = string.Join(", ", sizes.Select(s => _text[$"layouteditor.sizeName.{s.ToString().ToLowerInvariant()}"])),
                        };
                    })
                    .ToList()))
            .ToList();
    }

    /// <summary>Adds at the end of the shown place and selects it, so its size and place can be changed next.</summary>
    private void AddFromGallery(string typeId)
    {
        var spot = Spot;
        var result = _session.Add(typeId, spot);
        if (result == EditResult.Applied)
            _selected = _session.PlacementsAt(spot).Count - 1;
        Apply(result);
    }

    private IReadOnlyList<PriorityRow> BuildPriority()
    {
        var enabled = _session.EnabledPriority;
        return enabled.Select((id, i) =>
        {
            var name = _catalog.TryResolve(id, out var descriptor) ? _text[descriptor.NameKey] : id;
            return new PriorityRow(
                (i + 1).ToString(CultureInfo.CurrentCulture),
                name,
                i > 0,
                i < enabled.Count - 1,
                new RelayCommand(() => Apply(_session.MovePriority(id, -1))),
                new RelayCommand(() => Apply(_session.MovePriority(id, 1))));
        }).ToList();
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (!_committing)
            ChangedOutside = true;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Rebuild();
}
