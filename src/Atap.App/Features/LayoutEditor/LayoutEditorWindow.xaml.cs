using System.Windows;

namespace Atap.Features.LayoutEditor;

internal partial class LayoutEditorWindow : Window, ILayoutEditorWindow
{
    private LayoutEditorWindow(LayoutEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) => viewModel.Dispose();
    }

    void ILayoutEditorWindow.Activate()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Creates the window and shows it. It closes on Done or Cancel.</summary>
    public static ILayoutEditorWindow Open(LayoutEditorViewModel viewModel)
    {
        var window = new LayoutEditorWindow(viewModel);
        window.Show();
        window.Activate();
        return window;
    }
}
