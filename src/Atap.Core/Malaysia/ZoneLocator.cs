namespace Atap.Core.Malaysia;

/// <summary>The zone whose district centre is nearest to a location.</summary>
/// <param name="DistanceKm">Distance to <paramref name="Nearest"/>.</param>
public sealed record ZoneMatch(PrayerZone Zone, DistrictCentroid Nearest, double DistanceKm)
{
    /// <summary>
    /// True when the location is too far from every district centre to be in Malaysia. The App
    /// then offers local calculation instead of a zone. Places just over a land border, such
    /// as Singapore or Brunei, are close enough to match a zone, which is why the App always
    /// asks before switching.
    /// </summary>
    public bool IsAbroad => DistanceKm > ZoneLocator.AbroadDistanceKm;
}

/// <summary>Matches a location to a zone entirely on the PC. Nothing is sent anywhere.</summary>
public static class ZoneLocator
{
    /// <summary>Beyond this distance from the nearest district centre, a location counts as abroad.</summary>
    public const double AbroadDistanceKm = 100;

    /// <summary>The nearest zone, or null for an invalid point. Special zones are never returned.</summary>
    public static ZoneMatch? FindNearest(GeoPoint point)
    {
        if (!point.IsValid)
            return null;

        DistrictCentroid? best = null;
        var bestDistance = double.MaxValue;
        foreach (var centroid in DistrictCentroids.All)
        {
            var distance = point.DistanceKmTo(centroid.Point);
            if (distance < bestDistance)
            {
                best = centroid;
                bestDistance = distance;
            }
        }

        if (best is null || PrayerZones.Find(best.ZoneCode) is not { } zone)
            return null;
        return new ZoneMatch(zone, best, bestDistance);
    }
}
