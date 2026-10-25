using System.Windows.Input;
using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Notch;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.Panel;
using Atap.Notch;

[assembly: AtapFeature(typeof(PanelDescriptor))]

namespace Atap.Features.Panel;

/// <summary>
/// The panel: the largest notch, with pages of widgets. A click on the notch opens it and another click
/// closes it. It also opens and closes from the right-click menu, and Esc or a click on another app closes it.
/// </summary>
internal sealed class PanelDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "panel";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    /// <summary>Not in safe mode, so the notch only expands on hover there.</summary>
    public bool IsEnabled(SettingValues settings, AppMode mode) => mode != AppMode.Safe;

    public IAppFeature Create(FeatureContext context) => new PanelFeature(context.Notch, context.Commands, FeatureId);
}

/// <summary>Holds the PanelOpen flag. Takes clicks on the notch and Esc in the panel. Holds no timer.</summary>
internal sealed class PanelFeature(INotchControl notch, ICommandRegistry commands, string ownerId) : IAppFeature, INotchInputHandler
{
    public const string ToggleCommand = "notch.panel.toggle";
    public const string MenuGroup = "notch.panel";

    private readonly List<IDisposable> _registrations = [];

    public bool IsOpen => notch.IsSet(NotchFlag.PanelOpen);

    public Task StartAsync()
    {
        _registrations.Add(commands.Register(new AtapCommand(ToggleCommand, "panel.menu.show", _ =>
        {
            Toggle();
            return Task.CompletedTask;
        })
        {
            MenuGroup = MenuGroup,
            MenuOrder = 5,
            KeywordsKey = "panel.keywords",
            IsChecked = () => IsOpen,
        }));
        _registrations.Add(notch.AddInputHandler(this));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var registration in _registrations)
            registration.Dispose();
        _registrations.Clear();
        // The feature host clears the flag once the feature has stopped.
        return ValueTask.CompletedTask;
    }

    public void Toggle() => notch.SetFlag(NotchFlag.PanelOpen, ownerId, !IsOpen);

    public void Close() => notch.SetFlag(NotchFlag.PanelOpen, ownerId, on: false);

    /// <summary>
    /// A click on the notch opens the panel, and a click on the open panel closes it. Clicks on an alert stay
    /// with the alert, and buttons inside widgets keep their own clicks. Esc in the panel closes it, and so
    /// does a click on another app.
    /// </summary>
    public bool TryHandle(NotchInput input)
    {
        switch (input.Kind)
        {
            case NotchInputKind.Click when input.Surface is NotchSurface.Idle or NotchSurface.Compact or NotchSurface.Expanded or NotchSurface.Slim:
                notch.SetFlag(NotchFlag.PanelOpen, ownerId, on: true);
                return true;
            case NotchInputKind.Click when input.Surface == NotchSurface.Panel:
            case NotchInputKind.Dismiss when input.Surface == NotchSurface.Panel:
            case NotchInputKind.Key when input.Surface == NotchSurface.Panel && input.Key == Key.Escape:
                Close();
                return true;
            default:
                return false;
        }
    }
}
