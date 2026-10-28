namespace Placebook.Services;

public static class Geo
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>Luftlinie zwischen zwei Koordinaten in Kilometern (Haversine-Formel).</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(a));
    }

    public static string FormatDistance(double km) =>
        km < 1 ? $"{km * 1000:0} m" : km < 10 ? $"{km:0.0} km" : $"{km:N0} km";

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
