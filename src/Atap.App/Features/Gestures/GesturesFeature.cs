using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Notch;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.Gestures;
using Atap.Notch;

[assembly: AtapFeature(typeof(GesturesDescriptor))]

namespace Atap.Features.Gestures;

/// <summary>
/// Mouse gestures on the notch: the wheel changes the volume and a middle click plays or pauses music. Each runs
/// a command, and a command nothing has registered does nothing, so the wheel still scrolls where it should.
/// A click opens the panel and the sideways wheel turns its pages; those live with the panel.
/// </summary>
internal sealed class GesturesDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "gestures";
    public const string WheelVolumeKey = "gestures.wheelVolume";
    public const string MiddleClickKey = "gestures.middleClick";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(WheelVolumeKey, "gestures.wheelVolume", SettingType.Bool, true) { SectionId = SettingsSections.Behavior },
        new(MiddleClickKey, "gestures.middleClick", SettingType.Bool, true) { SectionId = SettingsSections.Behavior },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) => new GesturesFeature(context.Settings, context.Commands, context.Notch);
}

/// <summary>The command ids gestures run. The media and audio features register them.</summary>
internal static class GestureCommands
{
    public const string VolumeUp = "audio.volume.up";
    public const string VolumeDown = "audio.volume.down";
    public const string PlayPause = "media.playPause";
}

/// <summary>Turns the wheel and middle click on the notch into commands. Holds no timer.</summary>
internal sealed class GesturesFeature(IAppSettingsView settings, ICommandRegistry commands, INotchControl notch) : IAppFeature, INotchInputHandler
{
    private IDisposable? _registration;

    public Task StartAsync()
    {
        _registration = notch.AddInputHandler(this);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _registration?.Dispose();
        _registration = null;
        return ValueTask.CompletedTask;
    }

    public bool TryHandle(NotchInput input)
    {
        switch (input.Kind)
        {
            // The open panel keeps the wheel for scrolling its widgets, and an answer for scrolling its text.
            case NotchInputKind.Wheel when input.Surface is not (NotchSurface.Panel or NotchSurface.Edit or NotchSurface.Answer)
                && input.Delta != 0
                && settings.Values.GetBool(GesturesDescriptor.WheelVolumeKey):
                return Run(input.Delta > 0 ? GestureCommands.VolumeUp : GestureCommands.VolumeDown);
            case NotchInputKind.MiddleClick when settings.Values.GetBool(GesturesDescriptor.MiddleClickKey):
                return Run(GestureCommands.PlayPause);
            default:
                return false;
        }
    }

    private bool Run(string id)
    {
        if (!commands.Contains(id))
            return false;
        _ = commands.TryRunAsync(id);
        return true;
    }
}
