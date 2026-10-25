using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Atap.Ui.Controls;

namespace Atap.Tests.Ui.Controls;

/// <summary>
/// Hosts a UI kit element in a real window that is never shown: a hidden window handle with the element under a
/// plain root, so layout, templates, resources and keyboard events run as on the notch, but nothing appears on
/// screen and nothing takes focus from the desktop. A WPF Window cannot do this: until it is shown it stays
/// collapsed, and WPF lays out nothing under it. Call it inside <c>Sta.Run</c>.
/// </summary>
internal sealed class KitHost : IDisposable
{
    // WS_POPUP without WS_VISIBLE.
    private const int HiddenPopup = unchecked((int)0x80000000);

    private readonly HwndSource _source;
    private readonly Border _root;

    public KitHost(FrameworkElement element, double width = 320, double height = 240)
    {
        _root = new Border { Child = element, Width = width, Height = height };
        _source = new HwndSource(new HwndSourceParameters("Atap test") { Width = (int)width, Height = (int)height, WindowStyle = HiddenPopup })
        {
            RootVisual = _root,
        };
        Layout();
    }

    public void Layout()
    {
        // Twice: ProgressBar sizes its indicator from the track's SizeChanged, which needs a second pass.
        _root.UpdateLayout();
        _root.UpdateLayout();
    }

    public void Dispose()
    {
        _root.Child = null;
        _source.Dispose();
    }

    /// <summary>Runs everything queued on the dispatcher, such as an Invoke from a screen reader.</summary>
    public static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Turns high contrast on for the kit until disposed.</summary>
    public static IDisposable HighContrast()
    {
        Contrast.Current.Override = true;
        return new ContrastReset();
    }

    public static AutomationPeer Peer(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException("No automation peer.");

    /// <summary>The first element in the visual tree with this x:Name.</summary>
    public static T Named<T>(DependencyObject root, string name)
        where T : FrameworkElement =>
        All<T>(root).FirstOrDefault(e => e.Name == name) ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name}.");

    public static IEnumerable<T> All<T>(DependencyObject root)
        where T : DependencyObject
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is T match)
                yield return match;
            for (var i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--)
                stack.Push(VisualTreeHelper.GetChild(current, i));
        }
    }

    /// <summary>The colour of a solid brush, for comparing a used brush with a theme or system one.</summary>
    public static Color ColorOf(object? brush) => Assert.IsAssignableFrom<SolidColorBrush>(brush).Color;

    /// <summary>A colour token from Tokens.xaml, such as "SystemAccentColor".</summary>
    public static Color Token(string key) => Assert.IsType<Color>(Application.Current.FindResource(key));

    public static Geometry Icon(string key) => Assert.IsAssignableFrom<Geometry>(Application.Current.FindResource(key));

    private sealed class ContrastReset : IDisposable
    {
        public void Dispose() => Contrast.Current.Override = null;
    }
}

/// <summary>A command that counts its runs.</summary>
internal sealed class CountingCommand(bool canExecute = true) : System.Windows.Input.ICommand
{
    public int Runs { get; private set; }

    public object? LastParameter { get; private set; }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => canExecute;

    public void Execute(object? parameter)
    {
        Runs++;
        LastParameter = parameter;
    }
}
