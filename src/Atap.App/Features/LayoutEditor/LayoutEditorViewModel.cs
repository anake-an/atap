using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Layout.Editing;
using Atap.Core.Localization;
using Atap.Core.Widgets;

namespace Atap.Features.LayoutEditor;

/// <summary>A tab in the editor: one notch state, or the compact priority list.</summary>
public enum EditorTab
{
    Idle,
    Open,
    Panel,
    Priority,
}

/// <summary>A size chip on a tile: S, M or L. Only the sizes the widget supports are shown.</summary>
public sealed record SizeChip(WidgetSize Size, string Label, string Name, bool IsSelected, IRelayCommand Pick);

/// <summary>One placed widget, with its size chips and buttons to move or remove it.</summary>
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
}

/// <summary>A widget in the Add widgets list.</summary>
public sealed record GalleryItem(string TypeId, string Name, string Description, bool CanAdd, string Note, IRelayCommand Add)
{
    public bool HasNote => Note.Length > 0;
}

public sealed record GalleryGroup(string Name, IReadOnlyList<GalleryItem> Items);

/// <summary>A panel page tab.</summary>
public sealed record PageTab(int Index, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A state tab: "When idle", "Open", "Panel" or "Compact priority".</summary>
public sealed record TabOption(EditorTab Tab, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A row in the compact priority list, highest first.</summary>
public sealed record PriorityRow(string Rank, string Name, bool CanMoveUp, bool CanMoveDown, IRelayCommand MoveUp, IRelayCommand MoveDown);

/// <summary>
/// The editor window's model over a <see cref="LayoutEditSession"/>. Nothing is saved until Done, which makes one
/// write and keeps the old layout in layout.prev.json. A change to layout.json from outside while editing offers
/// a reload instead of overwriting it.
/// </summary>
public sealed partial class LayoutEditorViewModel : ObservableObject, IDisposable
{
    private readonly ILayoutService _layout;
    private readonly WidgetCatalog _catalog;
    private readonly ILocalizer _text;
    private readonly LayoutBackup _backup;
    private LayoutEditSession _session;
    private EditorTab _tab = EditorTab.Open;
    private int _page;
    private string _search = string.Empty;
    private bool _committing;
    private bool _changedOutside;
    private string? _messageKey;

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
            if (SetProperty(ref _tab, value))
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
            Rebuild();
        }
    }

    public bool IsPanel => _tab == EditorTab.Panel;

    public bool IsPriority => _tab == EditorTab.Priority;

    public bool IsArranging => _tab != EditorTab.Priority;

    /// <summary>"What the notch shows when you hover it."</summary>
    public string TabHint => _text[$"layouteditor.hint.{_tab.ToString().ToLowerInvariant()}"];

    public IReadOnlyList<TileRow> Tiles { get; private set; } = [];

    public bool HasNoTiles => IsArranging && Tiles.Count == 0;

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

    internal LayoutEditSession Session => _session;

    public void Dispose()
    {
        _layout.Changed -= OnLayoutChanged;
        _text.LanguageChanged -= OnLanguageChanged;
    }

    [RelayCommand]
    private void ShowTab(EditorTab tab) => Tab = tab;

    [RelayCommand]
    private void Undo()
    {
        _session.Undo();
        Rebuild();
    }

    [RelayCommand]
    private void Redo()
    {
        _session.Redo();
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
            Rebuild();
        }
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (Apply(_session.RemovePage(_page)) == EditResult.Applied)
        {
            _page = Math.Min(_page, _session.Pages.Count - 1);
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
        Rebuild();
    }

    private EditSpot Spot => _tab switch
    {
        EditorTab.Idle => new EditSpot(NotchState.Idle),
        EditorTab.Panel => new EditSpot(NotchState.Panel, _page),
        _ => new EditSpot(NotchState.Expanded),
    };

    private EditResult Apply(EditResult result)
    {
        _messageKey = result == EditResult.Rejected ? "layouteditor.status.rejected" : null;
        Rebuild();
        return result;
    }

    private void Rebuild()
    {
        _page = Math.Clamp(_page, 0, Math.Max(0, _session.Pages.Count - 1));
        var spot = Spot;
        Tiles = IsArranging
            ? _session.PlacementsAt(spot).Select((p, i) => TileFor(spot, p, i, _session.PlacementsAt(spot).Count)).ToList()
            : [];
        Pages = _session.Pages.Select((p, i) => new PageTab(i, p.Name)).ToList();
        Tabs = Enum.GetValues<EditorTab>().Select(t => new TabOption(t, _text[$"layouteditor.tab.{t.ToString().ToLowerInvariant()}"])).ToList();
        Gallery = BuildGallery(spot);
        Priority = BuildPriority();
        OnPropertyChanged(string.Empty);
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
                new RelayCommand(() => Apply(_session.Resize(spot, index, size)))))
            .ToList();
        return new TileRow(
            name,
            detail,
            sizes.Count > 1 ? sizes : [],
            index > 0,
            index < count - 1,
            new RelayCommand(() => Apply(_session.Move(spot, index, -1))),
            new RelayCommand(() => Apply(_session.Move(spot, index, 1))),
            new RelayCommand(() => Apply(_session.Remove(spot, index))));
    }

    private IReadOnlyList<GalleryGroup> BuildGallery(EditSpot spot)
    {
        if (!IsArranging)
            return [];
        var words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _catalog.Descriptors
            .Select(d => (Descriptor: d, Name: _text[d.NameKey], Description: _text[d.DescriptionKey]))
            .Where(w => words.All(word =>
                w.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                || w.Description.Contains(word, StringComparison.CurrentCultureIgnoreCase)))
            .GroupBy(w => w.Descriptor.Group)
            .OrderBy(g => g.Key)
            .Select(g => new GalleryGroup(
                _text[$"layouteditor.group.{g.Key.ToString().ToLowerInvariant()}"],
                g.OrderBy(w => w.Name, StringComparer.CurrentCulture)
                    .Select(w =>
                    {
                        var supported = w.Descriptor.SupportedStates.Contains(spot.State);
                        var canAdd = _session.CanAdd(w.Descriptor.TypeId, spot);
                        var note = !supported ? _text["layouteditor.notHere"] : !canAdd ? _text["layouteditor.alreadyHere"] : string.Empty;
                        return new GalleryItem(w.Descriptor.TypeId, w.Name, w.Description, canAdd, note,
                            new RelayCommand(() => Apply(_session.Add(w.Descriptor.TypeId, spot)), () => canAdd));
                    })
                    .ToList()))
            .ToList();
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
