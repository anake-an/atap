using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Atap.Core.Placement;
using Atap.Features.LayoutEditor;
using Atap.Interop;

namespace Atap.Features.Ai;

/// <summary>
/// The chat window, fully on the screen of the notch. Enter in the box sends and Shift+Enter starts a new line.
/// The messages follow a growing answer while they are scrolled to the end. Closing it stops every answer.
/// </summary>
internal partial class AiChatWindow : Window
{
    // Space kept from the screen edges and the notch, in DIPs.
    private const double ScreenMargin = 16;

    // How far one wheel notch scrolls the messages, in DIPs, as three lines of text.
    private const double WheelStep = 48;

    private readonly AiChatViewModel _viewModel;
    private readonly EditorScreen? _screen;
    private readonly Size _designSize;
    private bool _placed;
    private bool _atEnd = true;

    public AiChatWindow(AiChatViewModel viewModel, EditorScreen? screen)
    {
        InitializeComponent();
        _designSize = new Size(Width, Height);
        _viewModel = viewModel;
        _screen = screen;
        DataContext = viewModel;
        ScreenFit.Attach(this);
        SourceInitialized += (_, _) => Place();
        ContentRendered += (_, _) => _placed = true;
        Loaded += (_, _) => Box.Focus();
        Box.PreviewKeyDown += OnBoxKeyDown;
        Messages.ScrollChanged += OnMessagesScrolled;
        Messages.PreviewMouseWheel += OnMessagesWheel;
        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelChanged;
    }

    /// <summary>Brings the window back from the taskbar and to the front.</summary>
    public void Raise()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Box.Focus();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // Moving onto a screen with another scale resizes the window. Place it again once, while it opens.
        if (!_placed)
            Dispatcher.InvokeAsync(Place, DispatcherPriority.Loaded);
    }

    // Enter sends. Shift+Enter is left to the box, which starts a new line.
    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;
        e.Handled = true;
        if (_viewModel.Send())
        {
            _atEnd = true;
            Messages.ScrollToEnd();
        }
    }

    // A new chat, or one chosen from the list, opens at its end, with the keyboard in the box.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AiChatViewModel.SelectedChat))
            return;
        _atEnd = true;
        Dispatcher.InvokeAsync(() =>
        {
            Messages.ScrollToEnd();
            if (IsActive)
                Box.Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>While the messages sit at the end, a growing answer keeps them there. Scrolling up stops that.</summary>
    private void OnMessagesScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
            _atEnd = Messages.VerticalOffset >= Messages.ScrollableHeight - 1;
        else if (_atEnd)
            Messages.ScrollToEnd();
    }

    // The answer boxes would keep the wheel for themselves, so the messages scroll as one.
    private void OnMessagesWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        Messages.ScrollToVerticalOffset(Messages.VerticalOffset - e.Delta / 120.0 * WheelStep);
    }

    /// <summary>Opens fully inside the notch's screen, below the notch, centred, at that screen's scale.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _screen is not { } screen || screen.WorkArea.Width < 200 || screen.WorkArea.Height < 200)
            return;
        var rect = EditorWindowPlacement.Compute(screen.WorkArea, screen.Notch, screen.Scale, _designSize.Width, _designSize.Height, ScreenMargin);
        // A small screen gets a smaller window. Let WPF keep it at that size.
        MinWidth = Math.Min(MinWidth, rect.Width / screen.Scale);
        MinHeight = Math.Min(MinHeight, rect.Height / screen.Scale);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER | NativeMethods.SWP_NOACTIVATE);
    }
}

/// <summary>The real chat window. One at a time, made on open and dropped on close.</summary>
/// <param name="screen">The notch's screen when the window opens, or null when it is not known.</param>
internal sealed class AiChatShell(Func<EditorScreen?> screen) : IAiChatShell
{
    private AiChatWindow? _window;

    public bool IsOpen => _window is not null;

    public void Show(AiChatViewModel viewModel, Action closed)
    {
        var window = new AiChatWindow(viewModel, screen() ?? EditorScreen.ForPointer());
        window.Closed += (_, _) =>
        {
            _window = null;
            closed();
        };
        _window = window;
        window.Show();
        window.Activate();
    }

    public void Activate() => _window?.Raise();

    public void Close() => _window?.Close();

    public void Dispose() => _window?.Close();
}
