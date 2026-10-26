using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Composition;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Notch;
using Atap.Core.Placement;
using Atap.Core.Widgets;

namespace Atap.Notch;

/// <summary>
/// Drives one notch window: picks the surface from hover, flags, full screen and live widgets, and builds
/// the placements to show from the active layout. Content is only rebuilt when the surface (or the compact
/// owner, or the layout) actually changes. Each window has its own instance, so hovering one notch does
/// not expand the others; the widgets themselves are shared.
/// </summary>
public sealed class NotchViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyDictionary<string, WidgetVisibility> NothingShown = new Dictionary<string, WidgetVisibility>();

    private readonly ILayoutService _layout;
    private readonly WidgetHost _host;
    private readonly NotchControl _control;
    private readonly IForegroundState _foreground;

    private IReadOnlyList<IWidget> _widgets = [];
    private Dictionary<string, IWidget> _widgetsById = new(StringComparer.Ordinal);
    private EffectiveLayout _active;

    private NotchSurface _surface = NotchSurface.Idle;
    private NotchSurface _shownSurface = NotchSurface.Idle;
    private NotchState _state = NotchState.Idle;
    private NotchOrientation _orientation = NotchOrientation.Horizontal;
    private bool _isHovered;
    private bool _isDraggingFiles;
    private bool _isOpenForFiles;
    private bool _disposed;
    private bool _isTucked;
    private bool _hasLiveOwner;
    private object? _shownContent;
    private object? _shownBanner;
    private NotchLook _look = NotchLook.Default;
    private bool _lessMotionFlag;
    private int _pageIndex;
    private IReadOnlyList<IReadOnlyList<WidgetPlacement>> _pages = [];
    private IWidget? _compactOwner;
    private IReadOnlyList<PlacementViewModel> _leftItems = [];
    private IReadOnlyList<PlacementViewModel> _centerItems = [];
    private IReadOnlyList<PlacementViewModel> _rightItems = [];
    private IReadOnlyList<PlacementViewModel> _gridItems = [];
    private PixelRect? _screen;

    internal NotchViewModel(ILayoutService layout, WidgetHost host, NotchControl control, IForegroundState foreground)
    {
        _layout = layout;
        _host = host;
        _control = control;
        _foreground = foreground;
        _active = layout.Active;

        _host.WidgetsChanged += OnWidgetsChanged;
        _layout.Changed += OnLayoutChanged;
        _control.Changed += OnInputsChanged;
        _foreground.Changed += OnInputsChanged;

        AttachWidgets();
        UpdateState(force: true);
    }

    /// <summary>
    /// The screen this notch is on, in physical pixels. A window that covers it, in full screen or borderless,
    /// turns this notch slim until it closes, is minimized or leaves the front.
    /// </summary>
    public void SetScreen(PixelRect screen)
    {
        if (_screen == screen)
            return;
        _screen = screen;
        UpdateState();
    }

    /// <summary>True while the front window fills this notch's screen.</summary>
    private bool IsScreenCovered => _screen is { } screen && _foreground.CoveredScreen == screen;

    /// <summary>
    /// A presentation hides every notch. Windows also reports browsers and videos in full screen as a Direct3D game,
    /// so a game hides the notch only when no front window is seen filling a screen. When one is, the notch on that
    /// screen turns slim and the others stay as they are.
    /// </summary>
    private bool HidesForFullScreen => _foreground.FullScreen switch
    {
        FullScreenKind.Presentation => true,
        FullScreenKind.Game => _foreground.CoveredScreen is null,
        _ => false,
    };

    /// <summary>Raised after the content has been set. The view resizes (or hides) on this, and fades content in only when it is new.</summary>
    public event EventHandler<NotchContentChangedEventArgs>? ContentChanged;

    /// <summary>The resolved surface. Hidden while a full-screen app is in front or a rule hides the notch.</summary>
    public NotchSurface Surface
    {
        get => _surface;
        private set
        {
            if (SetProperty(ref _surface, value))
                OnPropertyChanged(nameof(IsHidden));
        }
    }

    public bool IsHidden => _surface == NotchSurface.Hidden;

    /// <summary>The surface whose template is shown. It keeps the last visible one while hidden.</summary>
    public NotchSurface ShownSurface
    {
        get => _shownSurface;
        private set
        {
            if (SetProperty(ref _shownSurface, value))
                OnPropertyChanged(nameof(SurfaceContent));
        }
    }

    /// <summary>The state the shown surface stands for. Sets the shoulder radius and the template keys.</summary>
    public NotchState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    /// <summary>What a feature gave the shown surface, if anything (the alert, the editor).</summary>
    public object? SurfaceContent => _control.GetSurfaceContent(_shownSurface);

    /// <summary>An alert that came while the notch is open, shown as a banner above the widgets. The open notch would hide it otherwise.</summary>
    public object? Banner => _shownSurface is NotchSurface.Expanded or NotchSurface.Panel ? _control.GetSurfaceContent(NotchSurface.Alert) : null;

    /// <summary>Set by the view from the anchor: horizontal on the top edge, vertical on a side edge.</summary>
    public NotchOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (!SetProperty(ref _orientation, value))
                return;
            OnPropertyChanged(nameof(IsVertical));
            OnPropertyChanged(nameof(SlotOrientation));
        }
    }

    public bool IsVertical => _orientation == NotchOrientation.Vertical;

    /// <summary>How the left and right slots run: side by side on the top edge, stacked top to bottom on a side edge.</summary>
    public System.Windows.Controls.Orientation SlotOrientation =>
        IsVertical ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;

    /// <summary>Set by the view on mouse enter and leave.</summary>
    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (SetProperty(ref _isHovered, value))
                UpdateState();
        }
    }

    /// <summary>Set by the view while files are dragged over the notch, after the same delays as hover.</summary>
    public bool IsDraggingFiles
    {
        get => _isDraggingFiles;
        set
        {
            if (SetProperty(ref _isDraggingFiles, value))
                UpdateState();
        }
    }

    /// <summary>True while the panel shows only because files are dragged over it. It never takes the keyboard then.</summary>
    public bool IsOpenForFiles => _isOpenForFiles;

    /// <summary>Where a drag would open the notch now. The view asks when a drag comes in.</summary>
    public NotchDragTarget FileDragTarget(bool hasFiles) =>
        NotchFileDrag.Target(hasFiles, VisiblePlacements(NotchState.Expanded, null).Any(IsFileShelf), FileShelfPage());

    /// <summary>Slim shows only a hairline at the screen edge instead of a thin bar.</summary>
    public bool IsTucked
    {
        get => _isTucked;
        private set => SetProperty(ref _isTucked, value);
    }

    /// <summary>How big everything in the notch is drawn, 1 for the usual size. The view scales the content by it.</summary>
    public double TextScale => _look.TextScale;

    /// <summary>True when the notch should change size and content at once, without the spring and the fade.</summary>
    public bool LessMotion => _look.LessMotion || _lessMotionFlag;

    /// <summary>A widget is live, such as music playing. The slim bar shows an accent line then.</summary>
    public bool HasLiveOwner
    {
        get => _hasLiveOwner;
        private set => SetProperty(ref _hasLiveOwner, value);
    }

    public IReadOnlyList<PlacementViewModel> LeftItems
    {
        get => _leftItems;
        private set => SetProperty(ref _leftItems, value);
    }

    public IReadOnlyList<PlacementViewModel> CenterItems
    {
        get => _centerItems;
        private set => SetProperty(ref _centerItems, value);
    }

    public IReadOnlyList<PlacementViewModel> RightItems
    {
        get => _rightItems;
        private set => SetProperty(ref _rightItems, value);
    }

    /// <summary>The Expanded widgets, or those of the current panel page, in order for the 4-column grid.</summary>
    public IReadOnlyList<PlacementViewModel> GridItems
    {
        get => _gridItems;
        private set
        {
            if (SetProperty(ref _gridItems, value))
                OnPropertyChanged(nameof(IsGridEmpty));
        }
    }

    /// <summary>True when the open notch has no widget to show, so the panel shows a hint instead.</summary>
    public bool IsGridEmpty => _gridItems.Count == 0;

    /// <summary>Panel pages with at least one widget that can show. Empty pages are skipped.</summary>
    public int PageCount => _pages.Count;

    /// <summary>True with two pages or more, so the page dots show.</summary>
    public bool HasPages => _pages.Count > 1;

    /// <summary>The panel page shown, from 0. The page dots set it. It is kept while the panel is closed.</summary>
    public int PageIndex
    {
        get => _pageIndex;
        set
        {
            var index = Math.Clamp(value, 0, Math.Max(0, _pages.Count - 1));
            if (!SetProperty(ref _pageIndex, index) || ShownSurface != NotchSurface.Panel || _disposed)
                return;
            RebuildItems();
            ReportVisibility();
            ContentChanged?.Invoke(this, NotchContentChangedEventArgs.Swapped);
        }
    }

    /// <summary>Goes one page forward or back. False at the first or last page, or when the panel is not open.</summary>
    public bool StepPage(int step)
    {
        if (ShownSurface != NotchSurface.Panel || step == 0)
            return false;
        var before = _pageIndex;
        PageIndex = _pageIndex + Math.Sign(step);
        return _pageIndex != before;
    }

    /// <summary>Stops listening to the shared widgets and services; called when the window closes.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _host.WidgetsChanged -= OnWidgetsChanged;
        _layout.Changed -= OnLayoutChanged;
        _control.Changed -= OnInputsChanged;
        _foreground.Changed -= OnInputsChanged;
        DetachWidgets();
        _host.RemoveReporter(this);
    }

    private void OnWidgetsChanged(object? sender, EventArgs e)
    {
        DetachWidgets();
        AttachWidgets();
        UpdateState(force: true);
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        var active = _layout.Active;
        var same = active.HasSameContent(_active);
        _active = active;
        if (!same)
            UpdateState(force: true);
    }

    private void OnInputsChanged(object? sender, EventArgs e) => UpdateState();

    private void OnWidgetLiveChanged(object? sender, EventArgs e) => UpdateState();

    // A card that comes or goes changes the open notch's grid, so it is rebuilt then. Other surfaces only re-check.
    private void OnWidgetContentChanged(object? sender, EventArgs e) =>
        UpdateState(force: ShownSurface is NotchSurface.Expanded or NotchSurface.Panel);

    private void AttachWidgets()
    {
        _widgets = _host.Widgets;
        _widgetsById = _widgets.ToDictionary(w => w.Id, StringComparer.Ordinal);
        foreach (var widget in _widgets)
        {
            widget.IsLiveChanged += OnWidgetLiveChanged;
            if (widget is IOptionalContent optional)
                optional.HasContentChanged += OnWidgetContentChanged;
        }
    }

    private void DetachWidgets()
    {
        foreach (var widget in _widgets)
        {
            widget.IsLiveChanged -= OnWidgetLiveChanged;
            if (widget is IOptionalContent optional)
                optional.HasContentChanged -= OnWidgetContentChanged;
        }
    }

    private void UpdateState(bool force = false)
    {
        if (_disposed)
            return;

        var owner = CompactArbiter.SelectOwner(_widgets, _active.CompactPriority);
        var panelOpen = _control.IsSet(NotchFlag.PanelOpen);
        var drag = _isDraggingFiles ? FileDragTarget(hasFiles: true) : NotchDragTarget.None;
        var next = NotchSurfaceResolver.Resolve(new NotchInputs(
            Hidden: _control.IsSet(NotchFlag.Hide) || HidesForFullScreen,
            Editing: _control.IsSet(NotchFlag.Editing),
            PanelOpen: panelOpen || drag == NotchDragTarget.Panel,
            Hovered: IsHovered || drag == NotchDragTarget.Expanded,
            Peeking: _control.IsSet(NotchFlag.Peek),
            HasExpandedContent: VisiblePlacements(NotchState.Expanded, null).Any() || _control.GetSurfaceContent(NotchSurface.Expanded) is not null,
            HasAlert: _control.GetSurfaceContent(NotchSurface.Alert) is not null,
            Slim: _control.IsSet(NotchFlag.Slim) || IsScreenCovered,
            HasCompactOwner: owner is not null,
            HasCompactContent: owner is not null && VisiblePlacements(NotchState.Compact, owner).Any()));

        var wasHidden = IsHidden;
        _isOpenForFiles = next == NotchSurface.Panel && drag == NotchDragTarget.Panel && !panelOpen;
        if (next == NotchSurface.Hidden)
        {
            // Mouse leave and drag leave never arrive while the window is hidden.
            if (_isHovered)
            {
                _isHovered = false;
                OnPropertyChanged(nameof(IsHovered));
            }
            if (_isDraggingFiles)
            {
                _isDraggingFiles = false;
                OnPropertyChanged(nameof(IsDraggingFiles));
            }
            _compactOwner = owner;
            Surface = NotchSurface.Hidden;
            if (!wasHidden || force)
            {
                _host.ReportVisibility(this, NothingShown);
                ContentChanged?.Invoke(this, NotchContentChangedEventArgs.Refreshed);
            }
            return;
        }

        // The slim bar's size and accent follow these, so a change resizes it like a new surface would.
        // So does a new text size, on every surface.
        var tucked = _control.IsSet(NotchFlag.Tucked);
        var slimLookChanged = next == NotchSurface.Slim && (tucked != _isTucked || (owner is not null) != _hasLiveOwner);
        var look = _control.Look;
        var scaleChanged = look.TextScale != _look.TextScale;
        var lessMotionFlag = _control.IsSet(NotchFlag.LessMotion);
        if (look != _look || lessMotionFlag != _lessMotionFlag)
        {
            _look = look;
            _lessMotionFlag = lessMotionFlag;
            OnPropertyChanged(nameof(TextScale));
            OnPropertyChanged(nameof(LessMotion));
        }
        IsTucked = tucked;
        HasLiveOwner = owner is not null;

        // The compact content depends on who owns it; other surfaces do not.
        var unchanged = next == ShownSurface && (next != NotchSurface.Compact || owner == _compactOwner);
        _compactOwner = owner;
        Surface = next;
        if (unchanged && !force && !wasHidden)
        {
            OnPropertyChanged(nameof(SurfaceContent));
            OnPropertyChanged(nameof(Banner));
            // A new alert in place of the one on screen can need a different size, and so can a banner.
            var content = SurfaceContent;
            var banner = Banner;
            if (slimLookChanged || scaleChanged || !ReferenceEquals(content, _shownContent) || !ReferenceEquals(banner, _shownBanner))
            {
                _shownContent = content;
                _shownBanner = banner;
                ContentChanged?.Invoke(this, NotchContentChangedEventArgs.Refreshed);
            }
            return;
        }

        // New content fades in. The same content rebuilt, such as after a layout save or a card that came or went, only resizes.
        var swapped = !unchanged || wasHidden;
        ShownSurface = next;
        State = DisplayStateOf(next);
        // A panel opened by a drag shows the page with the file shelf.
        if (_isOpenForFiles)
            SetProperty(ref _pageIndex, FileShelfPage(), nameof(PageIndex));
        RebuildItems();
        OnPropertyChanged(nameof(SurfaceContent));
        OnPropertyChanged(nameof(Banner));
        _shownContent = SurfaceContent;
        _shownBanner = Banner;
        ReportVisibility();
        ContentChanged?.Invoke(this, swapped ? NotchContentChangedEventArgs.Swapped : NotchContentChangedEventArgs.Refreshed);
    }

    /// <summary>Surfaces without placements borrow the shape of the closest state.</summary>
    private static NotchState DisplayStateOf(NotchSurface surface) =>
        NotchSurfaceResolver.StateOf(surface) ?? surface switch
        {
            NotchSurface.Edit => NotchState.Panel,
            NotchSurface.Alert => NotchState.Compact,
            _ => NotchState.Idle,
        };

    /// <summary>Placements in a state whose instance runs and whose type supports that state.</summary>
    private IEnumerable<WidgetPlacement> VisiblePlacements(NotchState state, IWidget? compactOwner) =>
        _active.PlacementsFor(state).Where(p => IsVisible(p, state, compactOwner));

    private bool IsVisible(WidgetPlacement placement, NotchState state, IWidget? compactOwner) =>
        _widgetsById.TryGetValue(placement.WidgetId, out var widget)
        && widget.Descriptor.SupportedStates.Contains(state)
        && (state != NotchState.Compact || widget == compactOwner)
        && (state is not (NotchState.Expanded or NotchState.Panel) || HasContent(placement.WidgetId));

    /// <summary>False while an optional card, such as the missed alerts with nothing missed, has nothing to show.</summary>
    private bool HasContent(string instanceId) =>
        !_widgetsById.TryGetValue(instanceId, out var widget) || widget is not IOptionalContent { HasContent: false };

    private void RebuildItems()
    {
        List<PlacementViewModel> items = [];
        if (NotchSurfaceResolver.StateOf(ShownSurface) is { } state)
        {
            IEnumerable<WidgetPlacement> placements = [];
            if (state == NotchState.Panel)
            {
                RebuildPages();
                if (_pages.Count > 0)
                    placements = _pages[_pageIndex];
            }
            else
            {
                placements = VisiblePlacements(state, _compactOwner);
            }

            items = placements
                .OrderBy(p => p.Order)
                .Select(p =>
                {
                    var widget = _widgetsById[p.WidgetId];
                    return new PlacementViewModel(p.WidgetId, widget.Descriptor.TypeId, widget.ViewModel, state, p.Size, p.Slot)
                    {
                        NameKey = widget.Descriptor.NameKey,
                    };
                })
                .ToList();

            // The middle of the small notch, between the owner's left and right, can hold the owner's own view for it,
            // such as the song title. A widget without one leaves it empty, see WidgetTemplateSelector.
            if (state == NotchState.Compact && !items.Any(i => i.Slot == PlacementSlot.Center) && items.FirstOrDefault() is { } first)
                items.Add(first with { Slot = PlacementSlot.Center, IsGap = true });
        }

        // Expanded and Panel lay every widget on the grid. Idle and Compact keep the slots around the camera.
        var onGrid = State is NotchState.Expanded or NotchState.Panel;
        GridItems = onGrid ? items : [];
        if (onGrid)
            items = [];
        LeftItems = items.Where(i => i.Slot == PlacementSlot.Left).ToList();
        CenterItems = items.Where(i => i.Slot == PlacementSlot.Center).ToList();
        RightItems = items.Where(i => i.Slot == PlacementSlot.Right).ToList();
    }

    /// <summary>The pages that have a widget to show. The page shown stays in range when pages go away.</summary>
    private void RebuildPages()
    {
        var pages = VisiblePanelPages();
        var countChanged = pages.Count != _pages.Count;
        _pages = pages;
        var indexChanged = SetProperty(ref _pageIndex, Math.Clamp(_pageIndex, 0, Math.Max(0, pages.Count - 1)), nameof(PageIndex));
        if (countChanged || indexChanged)
        {
            OnPropertyChanged(nameof(PageCount));
            OnPropertyChanged(nameof(HasPages));
        }
    }

    // An unfilled panel follows the hover view, a size up. See PanelContent.
    private List<IReadOnlyList<WidgetPlacement>> VisiblePanelPages() => PanelContent
        .Pages(_active, id => _widgetsById.TryGetValue(id, out var widget) ? widget.Descriptor : null, HasContent)
        .ToList();

    /// <summary>The panel page, among those that show, with a file shelf on it. -1 when there is none.</summary>
    private int FileShelfPage() => VisiblePanelPages().FindIndex(page => page.Any(IsFileShelf));

    private bool IsFileShelf(WidgetPlacement placement) =>
        _widgetsById.TryGetValue(placement.WidgetId, out var widget) && widget.Descriptor.TypeId == NotchFileDrag.ShelfTypeId;

    private void ReportVisibility()
    {
        var visibility = State switch
        {
            NotchState.Idle => WidgetVisibility.Idle,
            NotchState.Compact => WidgetVisibility.Compact,
            NotchState.Expanded => WidgetVisibility.Expanded,
            _ => WidgetVisibility.Panel,
        };
        var shown = new Dictionary<string, WidgetVisibility>(StringComparer.Ordinal);
        foreach (var item in LeftItems.Concat(CenterItems).Concat(RightItems).Concat(GridItems))
            shown[item.InstanceId] = visibility;
        _host.ReportVisibility(this, shown);
    }
}
