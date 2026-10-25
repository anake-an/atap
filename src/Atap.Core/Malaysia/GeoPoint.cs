namespace Atap.Core.Malaysia;

/// <summary>A point in decimal degrees (WGS 84). It stays on the PC and is never sent anywhere.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusKm = 6371.0088;

    public bool IsValid =>
        double.IsFinite(Latitude) && double.IsFinite(Longitude)
        && Latitude is >= -90 and <= 90
        && Longitude is >= -180 and <= 180;

    /// <summary>Great-circle distance in kilometres (haversine).</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        var lat1 = ToRadians(Latitude);
        var lat2 = ToRadians(other.Latitude);
        var dLat = lat2 - lat1;
        var dLon = ToRadians(other.Longitude - Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
