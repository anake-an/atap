using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Atap.Core.Layout.Editing;
using Atap.Core.Placement;
using Atap.Interop;
using Atap.Ui.Preview;

namespace Atap.Features.LayoutEditor;

/// <summary>
/// The layout editor. The middle is a picture of the notch: drag widgets onto it from the list, drag them around
/// on it, onto a panel page tab, or off it to remove them. Everything also works from the keyboard.
/// </summary>
internal partial class LayoutEditorWindow : Window, ILayoutEditorWindow
{
    // Space kept from the screen edges and the notch, in DIPs.
    private const double ScreenMargin = 16;

    private readonly LayoutEditorViewModel _viewModel;
    private readonly EditorScreen? _screen;
    private bool _placed;

    // A press that may become a drag.
    private Point _pressAt;
    private EditorDrag? _pressDrag;
    private FrameworkElement? _pressVisual;
    private Vector _grabOffset;
    private bool _dragging;
    private bool _refocusTile;

    private LayoutEditorWindow(LayoutEditorViewModel viewModel, EditorScreen? screen)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _screen = screen;
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnViewModelChanged;
            viewModel.Dispose();
            Atap.Platform.Diagnostics.MemoryTrim.Soon(Dispatcher);
        };
        SourceInitialized += (_, _) => Place();
        ContentRendered += (_, _) => _placed = true;
    }

    void ILayoutEditorWindow.Activate()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Creates the window and shows it on <paramref name="screen"/>. It closes on Done or Cancel.</summary>
    public static ILayoutEditorWindow Open(LayoutEditorViewModel viewModel, EditorScreen? screen)
    {
        var window = new LayoutEditorWindow(viewModel, screen ?? EditorScreen.ForPointer());
        window.Show();
        window.Activate();
        return window;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // Moving onto a monitor with another scale resizes the window. Place it again once, while it opens.
        if (!_placed)
            Dispatcher.InvokeAsync(Place, DispatcherPriority.Loaded);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_dragging && e.Key == Key.Escape)
        {
            e.Handled = true;
            EndDrag(drop: false);
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_pressDrag is null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (_dragging)
                EndDrag(drop: false);
            _pressDrag = null;
            return;
        }

        var point = e.GetPosition(this);
        if (!_dragging)
        {
            var moved = point - _pressAt;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }
            StartDrag();
        }
        MoveGhost(point);
        _viewModel.DragOver(TargetAt(e), _viewModel.DropIndexAt(e.GetPosition(PreviewTiles).X, e.GetPosition(PreviewTiles).Y));
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_dragging)
        {
            e.Handled = true;
            EndDrag(drop: true);
        }
        _pressDrag = null;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Only the window losing the pointer ends a drag. A tile losing it to the window is how a drag starts.
        if (_dragging && ReferenceEquals(e.OriginalSource, this))
            EndDrag(drop: false);
    }

    /// <summary>Opens fully inside the notch's monitor, below the notch, centred, at that monitor's scale.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _screen is not { } screen || screen.WorkArea.Width < 200 || screen.WorkArea.Height < 200)
            return;

        var rect = EditorWindowPlacement.Compute(screen.WorkArea, screen.Notch, screen.Scale, Width, Height, ScreenMargin);
        // A small screen gets a smaller window. Let WPF keep it at that size.
        MinWidth = Math.Min(MinWidth, rect.Width / screen.Scale);
        MinHeight = Math.Min(MinHeight, rect.Height / screen.Scale);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    // A tile on the notch picture: a click selects it, a drag moves it.
    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsArranging && sender is FrameworkElement { DataContext: PreviewTile { IsMarker: false, Index: >= 0 } tile } element)
            Press(e, EditorDrag.FromPreview(_viewModel.Spot, tile.Index), element);
    }

    // A widget's preview in the Add widgets list: a drag puts it on the notch.
    private void OnGalleryMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GalleryItem item } element && item.CanAdd)
            Press(e, EditorDrag.FromGallery(item.TypeId), element);
    }

    private void Press(MouseButtonEventArgs e, EditorDrag drag, FrameworkElement visual)
    {
        _pressAt = e.GetPosition(this);
        _pressDrag = drag;
        _pressVisual = visual;
    }

    private void OnTileFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Tab onto a tile selects it. A click selects through the button, after any drag.
        if (Mouse.LeftButton != MouseButtonState.Pressed && sender is FrameworkElement { DataContext: PreviewTile { Index: >= 0 } tile })
        {
            _refocusTile = true;
            _viewModel.Select(tile.Index);
        }
    }

    /// <summary>Arrows go to the next tile, Ctrl and an arrow moves the widget, Delete removes it.</summary>
    private void OnTileKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PreviewTile { Index: >= 0 } tile })
            return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var step = e.Key switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            _ => 0,
        };
        if (step != 0)
        {
            _refocusTile = true;
            if (ctrl)
            {
                _viewModel.Select(tile.Index);
                _viewModel.MoveSelected(step);
            }
            else
            {
                _viewModel.Select(Math.Clamp(tile.Index + step, 0, Math.Max(0, _viewModel.Tiles.Count - 1)));
            }
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            _viewModel.Select(tile.Index);
            _viewModel.RemoveSelected();
            _refocusTile = _viewModel.Tiles.Count > 0;
            if (_refocusTile)
                _viewModel.Select(Math.Min(tile.Index, _viewModel.Tiles.Count - 1));
            e.Handled = true;
        }
    }

    // The tiles are made again after each change. Put the keyboard back on the selected one.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_refocusTile || (e.PropertyName is not (nameof(LayoutEditorViewModel.Picture) or "" or null)))
            return;
        Dispatcher.InvokeAsync(() =>
        {
            _refocusTile = false;
            var selected = _viewModel.SelectedIndex;
            for (var i = 0; i < PreviewTiles.Items.Count; i++)
            {
                if (PreviewTiles.Items[i] is PreviewTile { Index: var index } && index == selected
                    && PreviewTiles.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject container
                    && FindChild<Button>(container) is { } button)
                {
                    button.Focus();
                    return;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void StartDrag()
    {
        if (_pressDrag is not { } drag || _pressVisual is not { } visual)
            return;
        _dragging = true;

        // The ghost is a picture of the preview, taken before the tiles are made again.
        var bounds = visual.TransformToAncestor(this).TransformBounds(new Rect(visual.RenderSize));
        Ghost.Source = Snapshot(visual, bounds.Size);
        Ghost.Width = bounds.Width;
        Ghost.Height = bounds.Height;
        Ghost.Visibility = Visibility.Visible;
        _grabOffset = _pressAt - bounds.TopLeft;

        _viewModel.BeginDrag(drag);
        CaptureMouse();
    }

    private void EndDrag(bool drop)
    {
        _dragging = false;
        _pressDrag = null;
        _pressVisual = null;
        Ghost.Visibility = Visibility.Collapsed;
        Ghost.Source = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (drop)
            _viewModel.Drop();
        else
            _viewModel.CancelDrag();
    }

    private void MoveGhost(Point point)
    {
        Canvas.SetLeft(Ghost, point.X - _grabOffset.X);
        Canvas.SetTop(Ghost, point.Y - _grabOffset.Y);
    }

    /// <summary>The place under the pointer: the notch picture, a panel page tab, or null outside both.</summary>
    private EditSpot? TargetAt(MouseEventArgs e)
    {
        if (Inside(Stage, e))
            return _viewModel.Spot;
        if (_viewModel.IsPanel && Inside(PageTabs, e)
            && PageTabs.InputHitTest(e.GetPosition(PageTabs)) is DependencyObject hit
            && FindParent<ListBoxItem>(hit) is { DataContext: PageTab page })
        {
            return LayoutEditorViewModel.PageSpot(page.Index);
        }
        return null;
    }

    private static bool Inside(FrameworkElement element, MouseEventArgs e)
    {
        if (!element.IsVisible)
            return false;
        var p = e.GetPosition(element);
        return p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
    }

    private BitmapSource Snapshot(FrameworkElement visual, Size size)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(visual) { Stretch = Stretch.Fill }, null, new Rect(size));
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    private static T? FindParent<T>(DependencyObject? node)
        where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    private static T? FindChild<T>(DependencyObject node)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T found)
                return found;
            if (FindChild<T>(child) is { } deeper)
                return deeper;
        }
        return null;
    }
}
