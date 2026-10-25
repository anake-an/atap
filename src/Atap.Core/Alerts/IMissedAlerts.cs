namespace Atap.Core.Alerts;

/// <summary>
/// The Missed list, for the missed-alerts widget. Every sink that <see cref="AlertQueue.For"/> hands
/// out offers it, so a widget reads it as <c>context.Alerts as IMissedAlerts</c>.
/// </summary>
public interface IMissedAlerts
{
    /// <summary>Normal and High alerts that arrived while quiet or hidden, newest first. Memory only.</summary>
    IReadOnlyList<ShownAlert> Missed { get; }

    void ClearMissed();

    /// <summary>Raised on the UI thread when <see cref="Missed"/> changes.</summary>
    event EventHandler? MissedChanged;
}
