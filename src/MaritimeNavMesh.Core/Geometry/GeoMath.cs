namespace MaritimeNavMesh.Core.Geometry;

/// <summary>Geodesic calculations for maritime routing.</summary>
public static class GeoMath
{
    private const double EarthRadiusNm = 3440.065; // nautical miles
    private const double Deg2Rad = Math.PI / 180.0;

    /// <summary>Haversine great-circle distance in nautical miles.</summary>
    public static double HaversineNm(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat2 - lat1) * Deg2Rad;
        double dLon = (lon2 - lon1) * Deg2Rad;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Deg2Rad) * Math.Cos(lat2 * Deg2Rad)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2.0 * EarthRadiusNm * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>
    /// Normalize a longitude difference into (-180, 180] for anti-meridian detection.
    /// </summary>
    public static double NormalizeLonDelta(double delta)
    {
        while (delta > 180.0) delta -= 360.0;
        while (delta <= -180.0) delta += 360.0;
        return delta;
    }
}
