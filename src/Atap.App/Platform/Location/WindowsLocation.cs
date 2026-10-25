using System.Diagnostics;
using Atap.Core.Malaysia;
using Windows.Devices.Geolocation;

namespace Atap.Platform.Location;

internal enum LocationOutcome
{
    Found,

    /// <summary>Windows Location is off, or desktop apps may not use it.</summary>
    Off,

    /// <summary>No position came back in time.</summary>
    Failed,
}

/// <param name="Point">The position when <see cref="Outcome"/> is <see cref="LocationOutcome.Found"/>.</param>
internal sealed record LocationFix(LocationOutcome Outcome, GeoPoint? Point);

/// <summary>The PC's position, asked for once each time someone presses Find my area. Nothing is stored.</summary>
internal interface ILocationSource
{
    /// <summary>Never throws, except when cancelled.</summary>
    Task<LocationFix> FindAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the Windows Location page, so people can turn it on.</summary>
    void OpenWindowsSettings();
}

/// <summary>Windows Location. The position stays on the PC: it is only matched to a zone here.</summary>
internal sealed class WindowsLocation : ILocationSource
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>A position Windows already has is good enough to pick a zone.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    public async Task<LocationFix> FindAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await Geolocator.RequestAccessAsync().AsTask(cancellationToken) != GeolocationAccessStatus.Allowed)
                return new LocationFix(LocationOutcome.Off, null);
            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await locator.GetGeopositionAsync(MaxAge, Timeout).AsTask(cancellationToken);
            var point = position.Coordinate.Point.Position;
            return new LocationFix(LocationOutcome.Found, new GeoPoint(point.Latitude, point.Longitude));
        }
        catch (UnauthorizedAccessException)
        {
            return new LocationFix(LocationOutcome.Off, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new LocationFix(LocationOutcome.Failed, null);
        }
    }

    public void OpenWindowsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing to open on this edition of Windows. The card already says what to do.
        }
    }
}
