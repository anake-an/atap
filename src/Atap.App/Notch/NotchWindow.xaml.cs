using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Atap.Core;
using Atap.Core.Notch;
using Atap.Core.Placement;
using Atap.Interop;
using Atap.Notch.Automation;

namespace Atap.Notch;

/// <summary>
/// One notch: a borderless, click-through-where-transparent window stuck to an edge of one
/// monitor, positioned by the Core placement geometry. It never takes focus and stays out of Alt+Tab.
/// </summary>
public partial class NotchWindow : Window, INotchWindow
{
    // Smallest pill along the anchored edge (length) and away from it (depth).
    private const double MinPillLength = 120;
    private const double MinPillDepth = 33; // 15 px shoulder plus 18 px corner, so idle shoulders keep their full size

    // On a side edge the compact tab fits its stacked content. It stays long enough to read as the notch and to point at.
    private const double MinSideCompactLength = 72;

    // Slim: a thin bar that still reads as the notch. Tucked away: a hairline at the edge, still easy to reach
    // with the pointer pushed against the screen edge. Their shoulders are no deeper than the bar.
    private const double SlimLength = 140;
    private const double SlimDepth = 8;
    private const double TuckedLength = 90;
    private const double TuckedDepth = 3;

    // The size and the shoulders spring this long, on the same curve as the website.
    private static readonly Duration ResizeDuration = new(TimeSpan.FromMilliseconds(600));

    // New content fades in from the first frame, while the shape grows around it, so the notch is never empty.
    private static readonly Duration ContentFadeDuration = new(TimeSpan.FromMilliseconds(220));

    // The pointer has to rest on the notch a moment before it opens, and be away a moment before it closes,
    // so passing over it or slipping off the edge does not make it flicker.
    private static readonly TimeSpan HoverOpenDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan HoverCloseDelay = TimeSpan.FromMilliseconds(300);

    // Esc, held only while the pointer is on an answer, so Esc closes it without the notch taking the keyboard.
    private const int EscapeHotkeyId = 1;
    private const uint VirtualKeyEscape = 0x1B;

    // Shoulder radius in DIPs. It follows the state and springs along with the size.
    private static readonly DependencyProperty ShoulderRadiusProperty = DependencyProperty.Register(
        nameof(ShoulderRadius),
        typeof(double),
        typeof(NotchWindow),
        new PropertyMetadata(NotchShape.IdleShoulderRadius, (d, _) => ((NotchWindow)d).UpdateOutline()));

    private readonly NotchViewModel _viewModel;
    private readonly NotchMenuViewModel _menu;
    private readonly NotchInputRouter _input;
    private readonly NotchFocus _focus;
    private readonly List<Control> _commandItems = [];
    private readonly DispatcherTimer _hoverTimer;
    private readonly WheelPager _pager = new();
    private bool _pointerInside;
    private bool _filesInside;
    private bool _holdsEscape;

    // While the panel has the keyboard, and the window that had it before, which gets it back.
    private bool _keyboardMode;
    private IntPtr _previousForeground;
    private HwndSource? _hwndSource;
    private IntPtr _hwnd;
    private bool _isClosed;

    private NotchAnchor _anchor = NotchAnchor.TopCenter;
    private int _offset;
    private bool _anchorStyleApplied;

    // Room the window keeps for the pill, in device-independent pixels.
    // Reposition adds the shoulder room and turns it into a pixel rectangle.
    private double _windowWidth = MinPillLength;
    private double _windowHeight = MinPillDepth;

    // The size the pill is going to, which sets where the notch sits inside the window room.
    private Size _pillTarget = new(MinPillLength, MinPillDepth);

    // The room kept on sides whose window edge would move with the size. Cleared when the notch moves or hides.
    private Size _keptRoom;

    // What the running spring is going to, so asking for the same size again does not restart it.
    private Size _springTarget;
    private double _springRadius;

    // NotchShape.ShoulderRoom snapped to whole pixels by Reposition. It changes only with the monitor's scale.
    private double _appliedShoulderRoom;

    private int _resizeGeneration;
    private bool _resizeQueued;
    private bool _queuedResizeAnimates = true;

    internal NotchWindow(NotchViewModel viewModel, NotchMenuViewModel menu, NotchInputRouter input, NotchFocus focus)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _menu = menu;
        _input = input;
        _focus = focus;
        DataContext = viewModel;
        NotchMenu.DataContext = menu;
        ApplySurfaceTemplate();

        // Runs only between a pointer move onto or off the notch and the delay after it.
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher);
        _hoverTimer.Tick += HoverTimer_Tick;

        _viewModel.ContentChanged += OnContentChanged;
        Notch.SizeChanged += (_, _) => UpdateOutline();
        // A widget whose content grew or shrank on its own, such as one that loaded its data. Raised during layout, so queued.
        ContentFrame.NaturalSizeChanged += (_, _) => QueueResize(animate: !IsCalm);
        Loaded += OnLoaded;
    }

    /// <summary>The monitor this notch is on; null until <see cref="Place"/> is called.</summary>
    public DisplayMonitor? TargetMonitor { get; private set; }

    private double ShoulderRadius
    {
        get => (double)GetValue(ShoulderRadiusProperty);
        set => SetValue(ShoulderRadiusProperty, value);
    }

    /// <summary>Puts the notch on an edge of <paramref name="monitor"/>. Restyles only when the anchor changes.</summary>
    public void Place(DisplayMonitor monitor, NotchAnchor anchor, int offset)
    {
        var anchorChanged = !_anchorStyleApplied || anchor != _anchor;
        if (anchorChanged || !Equals(monitor, TargetMonitor) || offset != _offset)
            _keptRoom = default;
        // Windows may have moved the window with a display change, so it is always placed again.
        _appliedBounds = default;
        TargetMonitor = monitor;
        _anchor = anchor;
        _offset = offset;
        _viewModel.SetScreen(monitor.Bounds);

        if (anchorChanged)
        {
            ApplyAnchorStyle();
            if (IsLoaded)
            {
                // New shape and orientation: resize (which repositions) without the spring.
                QueueResize(animate: false);
                return;
            }
        }
        Reposition();
    }

    /// <summary>Hides the notch while its surface is Hidden (a full-screen app, or a rule) and shows it otherwise.</summary>
    public void UpdateVisibility()
    {
        if (_isClosed)
            return;
        if (_viewModel.IsHidden)
        {
            // Nothing shows while hidden, so the window can let go of the room it kept without anything jumping.
            _keptRoom = default;
            Hide();
        }
        else if (!IsVisible)
            Show(); // ShowActivated is false, so this does not take focus.
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        // Place before the first frame so the notch does not flash at the default window position.
        Reposition();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _keptRoom = default;
        _appliedBounds = default;
        // WPF moves the window to Windows' suggested rectangle while handling the change; place it after that.
        Dispatcher.InvokeAsync(Reposition);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _hoverTimer?.Stop();
        UpdateEscape();
        _hwndSource?.RemoveHook(WndProc);
        _focus?.WindowClosed(this);
        // Null when the constructor failed: WPF still closes the half-built window at shutdown.
        if (_viewModel is not null)
        {
            _viewModel.ContentChanged -= OnContentChanged;
            _viewModel.Dispose();
        }
        base.OnClosed(e);
    }

    /// <summary>A click on another app while the panel has the keyboard closes the panel.</summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_keyboardMode)
            _input.OnDismiss(_viewModel.Surface);
    }

    /// <summary>PageUp, PageDown and Ctrl+Tab turn panel pages. They come before Tab moves focus.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || !_keyboardMode)
            return;
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var step = e.Key switch
        {
            Key.PageDown => 1,
            Key.PageUp => -1,
            Key.Tab when control => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1,
            _ => 0,
        };
        if (step == 0)
            return;
        e.Handled = true;
        if (_viewModel.StepPage(step))
            FocusFirstLater();
    }

    /// <summary>Keys nothing in the notch used, such as Esc, go to the features. Tab and the arrows stay with focus moves.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (e.Handled || key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down)
            return;
        e.Handled = _input.OnKey(key, Keyboard.Modifiers, _viewModel.Surface);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTextScale();
        ResizeToContent(animate: false);
        UpdateVisibility();
    }

    // Less motion, from Atap or battery saver, or Windows' animation effects off: size and content change at once.
    private bool IsCalm => _viewModel.LessMotion || !SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// One smooth step: the new content, its size and the window room are all set here, before the next frame,
    /// so the content is never drawn at the old size and the window never catches up a frame late.
    /// </summary>
    private void OnContentChanged(object? sender, NotchContentChangedEventArgs e)
    {
        ApplySurfaceTemplate();
        ApplyTextScale();
        UpdateVisibility();
        UpdateKeyboardMode();
        UpdateEscape();
        if (!IsLoaded || _isClosed)
            return;
        var plan = NotchTransition.Plan(e.Change, IsCalm, hasSize: !double.IsNaN(Pill.Width));
        if (plan.Fade)
            FadeInContent();
        // A resize still waiting from earlier is done by this one.
        _resizeQueued = false;
        _queuedResizeAnimates = true;
        ResizeToContent(plan.Spring);
    }

    /// <summary>Draws everything in the notch at the chosen text size. The notch grows to fit, as it does for new content.</summary>
    private void ApplyTextScale()
    {
        var scale = _viewModel.TextScale;
        var current = SurfaceHost.LayoutTransform as ScaleTransform;
        if ((current?.ScaleX ?? 1) == scale)
            return;
        SurfaceHost.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// The panel and the layout editor take the keyboard, so Tab, the arrows and Esc work there. The window in
    /// front is noted first and gets the keyboard back when the panel closes. Everywhere else the notch never
    /// takes focus, so a click on it or on a music button leaves the app in front as it is.
    /// </summary>
    private void UpdateKeyboardMode()
    {
        var wants = _hwnd != IntPtr.Zero
            && !_viewModel.IsHidden
            && _viewModel.ShownSurface is NotchSurface.Panel or NotchSurface.Edit
            && !_viewModel.IsOpenForFiles;
        if (wants == _keyboardMode || (wants && !_focus.TryClaim(this)))
            return;

        _keyboardMode = wants;
        if (wants)
        {
            _previousForeground = NativeMethods.GetForegroundWindow();
            NativeMethods.RemoveExtendedStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE);
            KeyboardMode.SetIsOn(this, true);
            Activate();
            FocusFirstLater();
            return;
        }

        KeyboardMode.SetIsOn(this, false);
        NativeMethods.AddExtendedStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE);
        var previous = _previousForeground;
        _previousForeground = IntPtr.Zero;
        // Only while the notch still has the keyboard: a click on another app has given it away already.
        if (IsActive && previous != IntPtr.Zero && previous != _hwnd && NativeMethods.IsWindow(previous))
            NativeMethods.SetForegroundWindow(previous);
    }

    // After the new template is laid out, so its first widget is there to take focus.
    private void FocusFirstLater() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        if (_keyboardMode)
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    });

    /// <summary>Shows the template "surface.&lt;Name&gt;" for the current surface. Unchanged surfaces keep their template.</summary>
    private void ApplySurfaceTemplate()
    {
        var template = TryFindResource($"surface.{_viewModel.ShownSurface}") as DataTemplate;
        if (!ReferenceEquals(SurfaceHost.ContentTemplate, template))
            SurfaceHost.ContentTemplate = template;
    }

    private void Notch_MouseEnter(object sender, MouseEventArgs e) => PointerMoved(inside: true);

    private void Notch_MouseLeave(object sender, MouseEventArgs e) => PointerMoved(inside: false);

    private void PointerMoved(bool inside)
    {
        _pointerInside = inside;
        if (inside)
            _focus.PointerEntered(this);
        UpdateEscape();
        ScheduleHover();
    }

    /// <summary>
    /// While the pointer is on an answer, Esc is a hotkey of this window and goes to the features as a key, so
    /// Ask AI can close the answer. The notch still never takes the keyboard, and Esc is let go when the pointer leaves.
    /// </summary>
    private void UpdateEscape()
    {
        var wants = !_isClosed
            && _hwnd != IntPtr.Zero
            && _pointerInside
            && !_viewModel.IsHidden
            && _viewModel.ShownSurface == NotchSurface.Answer;
        if (wants == _holdsEscape)
            return;
        if (wants)
        {
            // Another app may hold Esc already. Then only Close puts the answer away.
            _holdsEscape = NativeMethods.RegisterHotKey(_hwnd, EscapeHotkeyId, NativeMethods.MOD_NOREPEAT, VirtualKeyEscape);
            return;
        }
        NativeMethods.UnregisterHotKey(_hwnd, EscapeHotkeyId);
        _holdsEscape = false;
    }

    // Hover does not fire while something is dragged. Files open the notch for the file shelf instead, with
    // the same delays. Leave fires between the notch's own parts too, and the enter after it cancels the close.
    private void Notch_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (_viewModel.FileDragTarget(e.Data.GetDataPresent(DataFormats.FileDrop)) != NotchDragTarget.None)
            FilesMoved(inside: true);
    }

    private void Notch_PreviewDragLeave(object sender, DragEventArgs e) => FilesMoved(inside: false);

    private void Notch_PreviewDrop(object sender, DragEventArgs e) => FilesMoved(inside: false);

    // Anywhere but the file shelf, which handles its own, the notch takes no drop.
    private void Notch_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void FilesMoved(bool inside)
    {
        _filesInside = inside;
        ScheduleHover();
    }

    /// <summary>Opens or closes the notch after the hover delay. Moving back before it ends cancels the change.</summary>
    private void ScheduleHover()
    {
        _hoverTimer.Stop();
        var opening = (_pointerInside && !_viewModel.IsHovered) || (_filesInside && !_viewModel.IsDraggingFiles);
        var closing = (!_pointerInside && _viewModel.IsHovered) || (!_filesInside && _viewModel.IsDraggingFiles);
        if (!opening && !closing)
            return;
        _hoverTimer.Interval = opening ? HoverOpenDelay : HoverCloseDelay;
        _hoverTimer.Start();
    }

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        _viewModel.IsHovered = _pointerInside;
        _viewModel.IsDraggingFiles = _filesInside;
    }

    private void Notch_MouseWheel(object sender, MouseWheelEventArgs e) =>
        e.Handled = _input.OnWheel(e.Delta, _viewModel.Surface);

    private void Notch_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
            e.Handled = _input.OnMiddleClick(_viewModel.Surface);
    }

    // Buttons inside widget views mark their clicks handled, so only clicks on the notch itself get here.
    private void Notch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled)
            e.Handled = _input.OnClick(_viewModel.Surface);
    }

    /// <summary>Adds the registered menu commands, grouped, above the update item.</summary>
    private void NotchMenu_Opened(object sender, RoutedEventArgs e)
    {
        RemoveCommandItems();
        var index = NotchMenu.Items.IndexOf(CommandsEnd);
        foreach (var group in _menu.BuildCommandGroups())
        {
            if (group.Count == 0)
                continue;
            var separator = new Separator();
            NotchMenu.Items.Insert(index++, separator);
            _commandItems.Add(separator);
            foreach (var option in group)
            {
                var item = new MenuItem
                {
                    Header = option.Header,
                    IsChecked = option.IsChecked,
                    IsEnabled = option.IsEnabled,
                    Command = option.Command,
                };
                NotchMenu.Items.Insert(index++, item);
                _commandItems.Add(item);
            }
        }
    }

    // The items are rebuilt on every open, so nothing stays around while the menu is closed.
    private void NotchMenu_Closed(object sender, RoutedEventArgs e) => RemoveCommandItems();

    private void RemoveCommandItems()
    {
        foreach (var item in _commandItems)
            NotchMenu.Items.Remove(item);
        _commandItems.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            // Clicks (media buttons, context menu) must not activate the window.
            // While the panel has the keyboard, clicks inside it keep it.
            case NativeMethods.WM_MOUSEACTIVATE when !_keyboardMode:
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            case NativeMethods.WM_HOTKEY when (int)wParam == EscapeHotkeyId:
                handled = true;
                _input.OnKey(Key.Escape, ModifierKeys.None, _viewModel.Surface);
                return IntPtr.Zero;

            // Alt+F4 while the panel has the keyboard would close this notch for good. Close the panel instead.
            case NativeMethods.WM_SYSCOMMAND when ((int)wParam & 0xFFF0) == NativeMethods.SC_CLOSE:
                handled = true;
                _input.OnDismiss(_viewModel.Surface);
                return IntPtr.Zero;

            // WPF has no horizontal wheel event. The delta is the high word of wParam.
            case NativeMethods.WM_MOUSEHWHEEL:
                var delta = unchecked((short)((long)wParam >> 16));
                // In the panel with more than one page, the sideways wheel turns pages before anything else.
                if (_viewModel.ShownSurface == NotchSurface.Panel && _viewModel.HasPages)
                {
                    _viewModel.StepPage(_pager.Add(delta, TimeSpan.FromMilliseconds(Environment.TickCount64)));
                    handled = true;
                    return IntPtr.Zero;
                }
                if (_input.OnHorizontalWheel(delta, _viewModel.Surface))
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Lays the notch out for the anchor: the side touching the screen edge is flush and flares into a
    /// shoulder at each end, the notch grows away from that edge, and on a side edge the slots stack vertically.
    /// </summary>
    private void ApplyAnchorStyle()
    {
        _anchorStyleApplied = true;
        var orientation = PlacementGeometry.OrientationOf(_anchor);
        _viewModel.Orientation = orientation;

        Pill.Padding = _viewModel.ShownSurface == NotchSurface.Slim ? new Thickness(0) : AnchorPadding();
        Pill.Margin = ShoulderMargin(_appliedShoulderRoom);

        // Pin the notch to the anchored side of the window, so it grows away from the screen edge.
        Notch.HorizontalAlignment = _anchor switch
        {
            NotchAnchor.TopLeft or NotchAnchor.LeftCenter => HorizontalAlignment.Left,
            NotchAnchor.TopRight or NotchAnchor.RightCenter => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        Notch.VerticalAlignment = orientation == NotchOrientation.Vertical ? VerticalAlignment.Center : VerticalAlignment.Top;

        // Left and right edges have the same size but mirrored shapes, so the size alone does not trigger a redraw.
        UpdateOutline();
    }

    /// <summary>Content padding for the anchor: slightly less on the flush side, as on the top edge.</summary>
    private Thickness AnchorPadding() => _anchor switch
    {
        NotchAnchor.LeftCenter => new Thickness(6, 14, 8, 14),
        NotchAnchor.RightCenter => new Thickness(8, 14, 6, 14),
        _ => new Thickness(14, 6, 14, 8),
    };

    /// <summary>Shoulder room on both ends of the pill along the screen edge.</summary>
    private Thickness ShoulderMargin(double room) =>
        _viewModel.IsVertical ? new Thickness(0, room, 0, room) : new Thickness(room, 0, room, 0);

    /// <summary>
    /// Redraws the notch and its shoulders as one filled outline from the Core shape math.
    /// Runs only when the pill size, the shoulder room, the radius or the anchor changes.
    /// </summary>
    private void UpdateOutline()
    {
        var outline = NotchShape.Outline(_anchor, Pill.ActualWidth, Pill.ActualHeight, ShoulderRadius, _appliedShoulderRoom);
        NotchFill.Data = Atap.Ui.Preview.NotchFrame.Geometry(outline, default);
    }

    /// <summary>Coalesces content changes into one resize after layout has run. A non-animated request wins.</summary>
    private void QueueResize(bool animate)
    {
        if (!IsLoaded || _isClosed)
            return;
        _queuedResizeAnimates &= animate;
        if (_resizeQueued)
            return;
        _resizeQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            var animateNow = _queuedResizeAnimates;
            _resizeQueued = false;
            _queuedResizeAnimates = true;
            ResizeToContent(animateNow);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Sizes the pill to its content and the shoulders to the state. The window grows first (if needed),
    /// the pill and shoulders spring to their new size, then the window shrinks to fit. The shoulders spring
    /// inside a room that never changes. Nothing redraws after that.
    /// </summary>
    private void ResizeToContent(bool animate)
    {
        if (_isClosed)
            return;

        var vertical = _viewModel.IsVertical;
        var minWidth = vertical ? MinPillDepth : MinPillLength;
        var minHeight = vertical ? (_viewModel.State == NotchState.Compact ? MinSideCompactLength : MinPillLength) : MinPillDepth;

        Size target;
        double targetRadius;
        if (_viewModel.ShownSurface == NotchSurface.Slim)
        {
            // A fixed thin size. The content is only the accent line, centred with no padding.
            var (length, depth) = _viewModel.IsTucked ? (TuckedLength, TuckedDepth) : (SlimLength, SlimDepth);
            Pill.Padding = new Thickness(0);
            target = vertical ? new Size(depth, length) : new Size(length, depth);
            targetRadius = depth;
        }
        else
        {
            Pill.Padding = AnchorPadding();
            SurfaceHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var padding = Pill.Padding;
            target = new Size(
                Math.Ceiling(Math.Max(minWidth, SurfaceHost.DesiredSize.Width + padding.Left + padding.Right)),
                Math.Ceiling(Math.Max(minHeight, SurfaceHost.DesiredSize.Height + padding.Top + padding.Bottom)));
            targetRadius = NotchShape.ShoulderRadiusFor(_viewModel.State);
        }

        // Width, Height and ShoulderRadius return the animated value while an animation is running.
        var current = new Size(
            double.IsNaN(Pill.Width) ? 0 : Pill.Width,
            double.IsNaN(Pill.Height) ? 0 : Pill.Height);
        var currentRadius = ShoulderRadius;
        var spring = animate && current.Width > 0 && current.Height > 0;

        // Already there, or already springing there: a second request for the same size changes nothing, so the
        // spring is never restarted half way (a hover and a click in quick succession, or a late layout pass).
        if (spring && target == _springTarget && targetRadius == _springRadius)
            return;
        if (spring && current == target && currentRadius == targetRadius)
            return;

        var generation = ++_resizeGeneration;
        _pillTarget = target;
        var (during, after) = NotchTransition.Room(_keptRoom, current, target, spring, _anchor);
        _keptRoom = new Size(Math.Max(_keptRoom.Width, during.Width), Math.Max(_keptRoom.Height, during.Height));

        if (!spring)
        {
            Pill.BeginAnimation(WidthProperty, null);
            Pill.BeginAnimation(HeightProperty, null);
            BeginAnimation(ShoulderRadiusProperty, null);
            _springTarget = default;
            Pill.Width = target.Width;
            Pill.Height = target.Height;
            ShoulderRadius = targetRadius;
            SetWindowSize(after.Width, after.Height);
            return;
        }

        // The window takes its room for the whole spring now, in the same frame as the new content. Sides whose
        // edge would move keep their room, so the window does not move and nothing is drawn a frame out of place.
        // The shoulder room stays the same, so the radius springs inside it without moving the notch.
        SetWindowSize(during.Width, during.Height);
        _springTarget = target;
        _springRadius = targetRadius;

        var ease = CubicBezierEase.NotchSpring;
        var widthAnimation = new DoubleAnimation(current.Width, target.Width, ResizeDuration) { EasingFunction = ease };
        var heightAnimation = new DoubleAnimation(current.Height, target.Height, ResizeDuration) { EasingFunction = ease };
        var radiusAnimation = new DoubleAnimation(currentRadius, targetRadius, ResizeDuration) { EasingFunction = ease };
        heightAnimation.Completed += (_, _) =>
        {
            if (generation != _resizeGeneration)
                return;
            _springTarget = default;
            // Only the free sides shrink back, so the anchored corner stays where it is.
            SetWindowSize(after.Width, after.Height);
        };

        Pill.BeginAnimation(WidthProperty, widthAnimation);
        Pill.BeginAnimation(HeightProperty, heightAnimation);
        BeginAnimation(ShoulderRadiusProperty, radiusAnimation);
    }

    /// <summary>
    /// Fades new content in from the first frame while the shape springs to its new size, so there is no empty
    /// moment and no flash. It ends at the content's own opacity and keeps nothing running.
    /// </summary>
    private void FadeInContent()
    {
        if (!IsLoaded || _isClosed)
            return;
        var fade = new DoubleAnimation(0, 1, ContentFadeDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        SurfaceHost.BeginAnimation(OpacityProperty, fade);
    }

    private void SetWindowSize(double width, double height)
    {
        _windowWidth = Math.Ceiling(width);
        _windowHeight = Math.Ceiling(height);
        Reposition();
    }

    /// <summary>
    /// Moves and sizes the window, in physical pixels, to the rectangle from the Core geometry,
    /// and gives the pill its shoulder room inside it.
    /// </summary>
    private void Reposition()
    {
        if (_isClosed || _hwnd == IntPtr.Zero || TargetMonitor is not { } monitor)
            return;

        var scale = MonitorEnumerator.GetScale(monitor.Bounds);

        // Whole pixels, so the pill's sides stay sharp and line up with the window's pixel rectangle.
        // The same room in every state keeps the notch still, even when the offset pins the window to a monitor side.
        _appliedShoulderRoom = Math.Ceiling(Math.Round(NotchShape.ShoulderRoom * scale, 4)) / scale;
        Pill.Margin = ShoulderMargin(_appliedShoulderRoom);

        var bounds = PlacementGeometry.Compute(
            monitor.Bounds, scale, _anchor, _windowWidth, _windowHeight, _offset, _appliedShoulderRoom).Bounds;

        // The room can be larger than the pill, so the notch is placed inside it where the placement puts it.
        var notch = PlacementGeometry.Compute(
            monitor.Bounds, scale, _anchor, _pillTarget.Width, _pillTarget.Height, _offset, _appliedShoulderRoom).Bounds;
        var margin = NotchTransition.Margin(bounds, notch, Notch.HorizontalAlignment, Notch.VerticalAlignment, scale);
        if (Notch.Margin != margin)
            Notch.Margin = margin;

        if (bounds == _appliedBounds)
            return;
        _appliedBounds = bounds;
        WarnIfOffScreen(bounds, monitor);
        // One move and size in pixels, then WPF's logical size to match, which is then a no-op for the window.
        // Two separate moves could show a frame of the old picture in the wrong place.
        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        // Keep WPF's logical size equal to the pixel size, so its layout fills the window exactly.
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;
    }

    private PixelRect _appliedBounds;

    // Once per run is enough to find the cause. The numbers are screen pixels only.
    private static bool _offScreenLogged;

    /// <summary>Logs once when the notch would land outside its screen, which was seen once and never again.</summary>
    private static void WarnIfOffScreen(PixelRect bounds, DisplayMonitor monitor)
    {
        var screen = monitor.Bounds;
        var inside = bounds.Left >= screen.Left && bounds.Top >= screen.Top && bounds.Right <= screen.Right && bounds.Bottom <= screen.Bottom;
        if (inside || _offScreenLogged)
            return;
        _offScreenLogged = true;
        Atap.Diagnostics.Log.Warn($"The notch was placed outside its screen: {bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height} on a screen at {screen.Left},{screen.Top} {screen.Width}x{screen.Height}.");
    }
}
