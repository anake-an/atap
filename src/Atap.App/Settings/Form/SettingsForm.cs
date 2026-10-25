using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Atap.Core.Input;

namespace Atap.Settings.Form;

/// <summary>
/// The one generated settings form, for app pages, widget settings and later onboarding. Its
/// DataContext is a <see cref="SettingsFormViewModel"/>. The look comes from <see cref="ViewsSource"/>,
/// which the host merges while it is open, so nothing of the form stays loaded after it closes.
/// </summary>
internal sealed class SettingsForm : Control
{
    /// <summary>The form's style and field templates. It merges the settings theme too.</summary>
    public static readonly Uri ViewsSource = new("/Atap;component/Settings/Form/SettingsForm.xaml", UriKind.Relative);

    public SettingsForm()
    {
        Focusable = false;
        IsTabStop = false;
    }
}

/// <summary>Writes a text box's binding on Enter, as well as when it loses focus.</summary>
internal static class FormInput
{
    public static readonly DependencyProperty CommitOnEnterProperty = DependencyProperty.RegisterAttached(
        "CommitOnEnter", typeof(bool), typeof(FormInput), new PropertyMetadata(false, OnCommitOnEnterChanged));

    public static bool GetCommitOnEnter(DependencyObject element) => (bool)element.GetValue(CommitOnEnterProperty);

    public static void SetCommitOnEnter(DependencyObject element, bool value) => element.SetValue(CommitOnEnterProperty, value);

    private static void OnCommitOnEnterChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box)
            return;
        box.KeyDown -= OnKeyDown;
        if ((bool)e.NewValue)
            box.KeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box)
            return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}

/// <summary>
/// The key capture box of a hotkey field. Tab and Shift+Tab still move focus, Backspace and Delete clear
/// the hotkey, and any other key with its modifiers is offered to the field.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (DataContext is not HotkeyField field)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if ((key == Key.Tab && (modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
            || (key == Key.Escape && modifiers == ModifierKeys.None))
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        e.Handled = true;

        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            field.ClearCommand.Execute(null);
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var flags = HotkeyModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0)
            flags |= HotkeyModifiers.Control;
        if ((modifiers & ModifierKeys.Alt) != 0)
            flags |= HotkeyModifiers.Alt;
        if ((modifiers & ModifierKeys.Shift) != 0)
            flags |= HotkeyModifiers.Shift;
        if ((modifiers & ModifierKeys.Windows) != 0)
            flags |= HotkeyModifiers.Windows;
        field.Capture(flags, KeyInterop.VirtualKeyFromKey(key));
    }
}

/// <summary>Finds a custom field's editor by its EditorKey in the owner's views, or shows a short note.</summary>
internal sealed class CustomEditorSelector : DataTemplateSelector
{
    public DataTemplate? Missing { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is CustomField { EditorKey: { Length: > 0 } key } && container is FrameworkElement element
            && element.TryFindResource(key) is DataTemplate template)
        {
            return template;
        }
        return Missing;
    }
}

/// <summary>True when the value is false, for showing one of two controls.</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
internal sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
