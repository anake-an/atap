using System.Windows;
using Atap.Composition;
using Atap.Core.Settings;
using Atap.Features.Accessibility;
using Atap.Platform.Diagnostics;
using Atap.Settings.Form;
using Atap.Ui.Controls;

namespace Atap.Settings.Shell;

/// <summary>An open settings window.</summary>
internal interface ISettingsSession
{
    /// <summary>Shows a page by id or section id, and brings the window to the front.</summary>
    void Show(string? pageOrSection);

    void Close();

    /// <summary>Raised once the window closed and everything in it was let go.</summary>
    event EventHandler? Closed;
}

/// <summary>
/// One settings window and its view models, built from the app services when Settings opens. The cards
/// are created now, from their registered types, and every card gets a lifetime that ends on close.
/// </summary>
internal sealed class SettingsSession : ISettingsSession
{
    private readonly SettingsWindow _window;
    private readonly SettingsWindowViewModel _model;
    private readonly IAppSettings _settings;

    private SettingsSession(SettingsWindow window, SettingsWindowViewModel model, IAppSettings settings)
    {
        _window = window;
        _model = model;
        _settings = settings;
        _window.Closed += OnClosed;
        _settings.Changed += OnSettingsChanged;
        ApplyLessMotion();
    }

    public event EventHandler? Closed;

    /// <summary>Must run on the UI thread.</summary>
    /// <param name="services">Everything the app has.</param>
    /// <param name="pageOrSection">The page to show first, or null for Home.</param>
    /// <param name="log">Warnings, with the exception if any.</param>
    public static SettingsSession Open(AppServices services, string? pageOrSection, Action<string, Exception?> log)
    {
        var window = new SettingsWindow();
        var context = new SettingsShellContext
        {
            Text = services.Text,
            Settings = services.Settings,
            Secrets = services.Secrets,
            Definitions = services.Features.SelectMany(f => f.Settings).ToList(),
            Cards = SettingsCardSet.Create(services.SettingsCards, log),
            CreateCardViewModel = (card, lifetime) => card.CreateViewModel(new SettingsCardContext(services, lifetime)),
            Picker = new DialogPathPicker(() => window),
            Log = log,
        };
        var model = new SettingsWindowViewModel(context, window.MergeViews);
        model.Show(pageOrSection);
        window.DataContext = model;

        var session = new SettingsSession(window, model, services.Settings);
        window.Show();
        session.BringToFront();
        return session;
    }

    public void Show(string? pageOrSection)
    {
        _model.Show(pageOrSection);
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        BringToFront();
    }

    public void Close() => _window.Close();

    private void BringToFront()
    {
        // A second launch asks from another process, and Windows may not let this one take the foreground.
        // Topmost for a moment puts the window on top all the same.
        if (!_window.IsActive)
        {
            _window.Topmost = true;
            _window.Topmost = false;
        }
        _window.Activate();
        _window.FocusSelectedPage();
    }

    // Less motion also makes the page jump on the wheel instead of gliding. It follows the setting while open.
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(AccessibilityDescriptor.LessMotionKey))
            ApplyLessMotion();
    }

    private void ApplyLessMotion() =>
        SmoothWheelScroll.SetLessMotion(_window, _settings.Values.GetBool(AccessibilityDescriptor.LessMotionKey));

    private void OnClosed(object? sender, EventArgs e)
    {
        _window.Closed -= OnClosed;
        _settings.Changed -= OnSettingsChanged;
        _window.DataContext = null;
        _model.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
        // Settings is the largest window. What it used goes back to Windows instead of staying with Atap.
        MemoryTrim.Soon(_window.Dispatcher);
    }
}
