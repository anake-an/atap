using Atap.Core.Alerts;
using Atap.Core.Commands;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Power;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Shared;
using Atap.Core.Storage;

namespace Atap.Core.Widgets;

/// <summary>
/// Everything a widget instance may use. Services marked scoped are tied to <see cref="Lifetime"/>
/// and are released when the instance stops.
/// </summary>
public sealed class WidgetContext
{
    public required string InstanceId { get; init; }

    public required IWidgetDescriptor Descriptor { get; init; }

    /// <summary>The UI thread. Post here from event handlers that arrive on other threads.</summary>
    public required SynchronizationContext Ui { get; init; }

    public required TimeProvider Time { get; init; }

    /// <summary>Scoped. The only way to run code later or repeatedly.</summary>
    public required IScheduler Scheduler { get; init; }

    /// <summary>Scoped to the descriptor's capabilities.</summary>
    public required IHttpDataService Http { get; init; }

    public required ILocalizer Text { get; init; }

    /// <summary>Alerts from this sink carry the instance id as their source.</summary>
    public required IAlertSink Alerts { get; init; }

    /// <summary>Scoped. Commands registered here are removed when the instance stops.</summary>
    public required ICommandRegistry Commands { get; init; }

    /// <summary>Scoped. Leases taken here end when the instance stops.</summary>
    public required ISharedServices Shared { get; init; }

    public required IAppSettingsView App { get; init; }

    public required IPowerState Power { get; init; }

    public required INetworkState Network { get; init; }

    public required ISecretStore Secrets { get; init; }

    /// <summary>Files under data\&lt;instanceId&gt;\ and state\&lt;instanceId&gt;\.</summary>
    public required IWidgetStorage Storage { get; init; }

    /// <summary>Where the instance is shown right now, merged over every notch window.</summary>
    public required IObservableValue<WidgetVisibility> Visibility { get; init; }

    public required IWidgetLifetime Lifetime { get; init; }
}
