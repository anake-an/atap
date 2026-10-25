namespace Atap.Core.Widgets;

/// <summary>
/// The lifetime of one widget, feature or settings card. Everything added here is disposed when the
/// owner stops, so scheduled items, commands and leases never outlive it.
/// </summary>
public interface IWidgetLifetime
{
    /// <summary>Cancelled when the owner stops.</summary>
    CancellationToken Stopping { get; }

    /// <summary>Disposed when the owner stops, newest first. Disposed at once if it has already stopped.</summary>
    void Add(IDisposable resource);

    /// <summary>Counts toward Failed: five errors within 60 seconds stop a widget.</summary>
    void ReportError(Exception exception);

    void ReportStatus(WidgetStatus status, string? messageKey = null);
}
