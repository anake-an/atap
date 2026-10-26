using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Atap.Settings.Shell;

/// <summary>
/// The settings window. It shows a <see cref="SettingsWindowViewModel"/> and merges each card's views the
/// first time a page needs them. Closing it drops the window, its view models and those views.
/// </summary>
internal partial class SettingsWindow : Window
{
    private readonly HashSet<Uri> _views = [];
    private SettingsWindowViewModel? _model;

    public SettingsWindow()
    {
        InitializeComponent();
        Atap.Interop.ScreenFit.Attach(this);
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => FocusSelectedPage();
        Closing += (_, _) => SaveFocusedText();
        Closed += (_, _) => Watch(null);
    }

    /// <summary>Text boxes write when they lose focus, which closing with X does not cause, so the typed text is written here.</summary>
    private static void SaveFocusedText()
    {
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    /// <summary>Merges a card's views into this window, once. They go when the window closes.</summary>
    public void MergeViews(Uri source)
    {
        if (_views.Add(source))
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
    }

    /// <summary>Puts keyboard focus on the selected page in the list, so arrow keys move between pages.</summary>
    public void FocusSelectedPage()
    {
        Nav.UpdateLayout();
        if (Nav.SelectedItem is { } item && Nav.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
            container.Focus();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Watch(e.NewValue as SettingsWindowViewModel);

    private void Watch(SettingsWindowViewModel? model)
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (_model is not null)
            _model.PropertyChanged += OnModelChanged;
    }

    // A new page starts at the top.
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsWindowViewModel.Current))
            Scroller.ScrollToTop();
    }
}
