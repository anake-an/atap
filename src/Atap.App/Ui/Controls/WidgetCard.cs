using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Atap.Ui.Controls;

/// <summary>
/// The card for an Expanded or Panel widget: a dark rounded surface with an optional icon and title row,
/// and the widget's content below it.
/// <code>
/// &lt;controls:WidgetCard Header="{loc:T weather.name}" Icon="{DynamicResource icon.cloud}"
///                      Accent="{DynamicResource MalaysiaAccentBrush}" AutomationProperties.Name="{Binding AccessibleSummary}"&gt;
///   ...
/// &lt;/controls:WidgetCard&gt;
/// </code>
/// It takes keyboard focus with a ring. With a <see cref="Command"/>, Enter, Space, a click or the
/// screen reader's Invoke runs it. Its screen reader name is the Header text unless AutomationProperties.Name is set.
/// </summary>
public class WidgetCard : HeaderedContentControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(WidgetCard), new PropertyMetadata(null));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(WidgetCard), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(WidgetCard), new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(WidgetCard), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey HasCommandKey = DependencyProperty.RegisterReadOnly(
        nameof(HasCommand), typeof(bool), typeof(WidgetCard), new PropertyMetadata(false));

    public static readonly DependencyProperty HasCommandProperty = HasCommandKey.DependencyProperty;

    private bool _pressed;

    static WidgetCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(WidgetCard), new FrameworkPropertyMetadata(typeof(WidgetCard)));
    }

    /// <summary>An "icon.&lt;name&gt;" geometry shown before the Header.</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The icon colour, usually the widget group's accent.</summary>
    public Brush? Accent
    {
        get => (Brush?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Runs on Enter, Space, a click on the card itself or Invoke. Buttons inside the card keep their own clicks.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>True while a Command is set. The template shows hover only then.</summary>
    public bool HasCommand => (bool)GetValue(HasCommandProperty);

    /// <summary>Runs the Command when it can run. False when there is none or it cannot run now.</summary>
    public bool Activate()
    {
        var command = Command;
        var parameter = CommandParameter;
        if (!IsEnabled || command is null || !command.CanExecute(parameter))
            return false;
        command.Execute(parameter);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Only keys on the card itself: a focused button inside handles its own Enter and Space.
        if (!e.Handled && ReferenceEquals(e.OriginalSource, this) && (e.Key is Key.Enter or Key.Space) && Activate())
            e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // Not marked handled, so the notch still sees the press. Buttons inside mark theirs, so they never get here.
        _pressed = !e.Handled && HasCommand;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var pressed = _pressed;
        _pressed = false;
        if (pressed && !e.Handled && IsMouseOver && Activate())
            e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pressed = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new WidgetCardAutomationPeer(this);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.SetValue(HasCommandKey, e.NewValue is not null);

    private sealed class WidgetCardAutomationPeer(WidgetCard owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        private WidgetCard Card => (WidgetCard)Owner;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke && Card.HasCommand ? this : base.GetPattern(patternInterface);

        public void Invoke()
        {
            if (!IsEnabled())
                throw new ElementNotEnabledException();
            // Queued, as buttons do, so a screen reader is not held while the command runs.
            Card.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Card.Activate());
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(WidgetCard);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            return Card.Header switch
            {
                string text => text,
                FrameworkElement element => AutomationProperties.GetName(element),
                _ => string.Empty,
            };
        }
    }
}
