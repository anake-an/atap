using Atap.Core.Alerts;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Notch;
using Atap.Core.Power;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Shared;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Notch;

namespace Atap.Composition;

/// <summary>How Atap was started.</summary>
internal enum AppMode
{
    Normal,

    /// <summary>After a crash: clock only, with no local API, hotkeys, rules, sync, scripts or MQTT.</summary>
    Safe,
}

/// <summary>
/// Describes an app feature (hotkeys, the local API, sync). Created once at startup, so it must stay
/// cheap: feature code is reached only inside <see cref="Create"/>.
/// </summary>
internal interface IFeatureDescriptor
{
    /// <summary>Also the prefix of its setting keys, for example "hotkeys".</summary>
    string Id { get; }

    /// <summary>Registered with the app settings at startup, whether the feature is on or not.</summary>
    IReadOnlyList<SettingDefinition> Settings { get; }

    /// <summary>Setting keys that can turn the feature on or off. Only changes to these re-check <see cref="IsEnabled"/>.</summary>
    IReadOnlyCollection<string> EnabledBy { get; }

    bool IsEnabled(SettingValues settings, AppMode mode);

    IAppFeature Create(FeatureContext context);
}

/// <summary>A running feature. Disposed when it is turned off and at shutdown.</summary>
internal interface IAppFeature : IAsyncDisposable
{
    /// <summary>
    /// Gets <see cref="FeatureHost.StartTimeout"/> to finish. Long work should watch the context's
    /// <c>Lifetime.Stopping</c>, which is cancelled when the feature is stopped or the app quits.
    /// </summary>
    Task StartAsync();
}

/// <summary>Everything a feature may use. Services marked scoped are released when the feature stops.</summary>
internal sealed class FeatureContext
{
    public required string FeatureId { get; init; }

    public required AppMode Mode { get; init; }

    public required SynchronizationContext Ui { get; init; }

    public required TimeProvider Time { get; init; }

    /// <summary>Scoped.</summary>
    public required IScheduler Scheduler { get; init; }

    /// <summary>For the clock feature only: tells the scheduler about clock changes and resume.</summary>
    public required ISchedulerControl SchedulerControl { get; init; }

    /// <summary>
    /// The shared HTTP service. Call For with the feature's own capabilities. The network privacy feature
    /// also sets its <see cref="HttpDataService.OfflineMode"/>.
    /// </summary>
    public required HttpDataService Http { get; init; }

    public required ILocalizer Text { get; init; }

    /// <summary>Scoped.</summary>
    public required ICommandRegistry Commands { get; init; }

    /// <summary>Scoped.</summary>
    public required ISharedServices Shared { get; init; }

    public required IPowerState Power { get; init; }

    public required INetworkState Network { get; init; }

    public required ISecretStore Secrets { get; init; }

    public required IAppSettings Settings { get; init; }

    public required ILayoutService Layout { get; init; }

    public required IForegroundState Foreground { get; init; }

    public required IAlertQueue Alerts { get; init; }

    public required INotchControl Notch { get; init; }

    /// <summary>The widget catalog and the running instances, with their status and a restart.</summary>
    public required IWidgetHostView Widgets { get; init; }

    /// <summary>Settings card types. A card is created only while the settings window is open.</summary>
    public required IReadOnlyList<Type> SettingsCards { get; init; }

    /// <summary>
    /// Everything the app has, including the notch host and updates. For the settings window, which gives
    /// each card a <see cref="SettingsCardContext"/>, and the tray. These services are not scoped: other
    /// features use the scoped ones above, so what they add ends when they stop.
    /// </summary>
    public required AppServices Services { get; init; }

    public required AtapPaths Paths { get; init; }

    public required IWidgetLifetime Lifetime { get; init; }
}
