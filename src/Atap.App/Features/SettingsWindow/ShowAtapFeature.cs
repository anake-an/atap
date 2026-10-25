using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Features.SettingsWindow;
using Atap.Notch;

[assembly: AtapFeature(typeof(ShowAtapDescriptor))]

namespace Atap.Features.SettingsWindow;

/// <summary>
/// "Show Atap" on the Home page of Settings. Turning it off hides the notch until it is turned on again,
/// also after a restart. The feature runs only while the notch is hidden.
/// </summary>
internal sealed class ShowAtapDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "notch";
    public const string ShowKey = "notch.show";

    public string Id => FeatureId;

    /// <summary>Machine scope: hiding the notch on one PC never hides it on another.</summary>
    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(ShowKey, "settingswindow.showAtap", SettingType.Bool, true)
        {
            SectionId = SettingsSections.General,
            DescriptionKey = "settingswindow.showAtapHint",
            Scope = SettingScope.Machine,
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [ShowKey];

    /// <summary>Never in safe mode, so the safe mode alert can be seen.</summary>
    public bool IsEnabled(SettingValues settings, AppMode mode) => mode != AppMode.Safe && !settings.GetBool(ShowKey);

    public IAppFeature Create(FeatureContext context) => new HideNotchFeature(context.Notch, FeatureId);
}

/// <summary>Holds the Hide flag while it runs. The feature host also clears the flag when it stops.</summary>
internal sealed class HideNotchFeature(INotchControl notch, string ownerId) : IAppFeature
{
    public Task StartAsync()
    {
        notch.SetFlag(NotchFlag.Hide, ownerId, on: true);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        notch.SetFlag(NotchFlag.Hide, ownerId, on: false);
        return ValueTask.CompletedTask;
    }
}
