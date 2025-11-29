using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge;

/// <summary>
/// Geographic helper for distance calculations between coordinates
/// </summary>
public static class GeoHelper
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Calculate distance between two lat/lon points using Haversine formula
    /// </summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        lat1 = DegreesToRadians(lat1);
        lat2 = DegreesToRadians(lat2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    /// <summary>
    /// Calculate distance between two SourceLocations
    /// </summary>
    public static double DistanceKm(SourceLocation from, SourceLocation to)
    {
        return DistanceKm(from.Lat, from.Lon, to.Lat, to.Lon);
    }

    /// <summary>
    /// Find closest location to a given point
    /// </summary>
    public static SourceLocation? FindClosest(SourceLocation from, List<SourceLocation> candidates)
    {
        if (candidates.Count == 0) return null;

        return candidates
            .OrderBy(c => DistanceKm(from, c))
            .First();
    }

    /// <summary>
    /// Find locations within a certain radius (km)
    /// </summary>
    public static List<SourceLocation> FindWithinRadius(SourceLocation center, List<SourceLocation> candidates, double radiusKm)
    {
        return candidates
            .Where(c => DistanceKm(center, c) <= radiusKm)
            .OrderBy(c => DistanceKm(center, c))
            .ToList();
    }

    /// <summary>
    /// Group locations by proximity clusters
    /// </summary>
    public static List<List<SourceLocation>> ClusterByProximity(List<SourceLocation> locations, double clusterRadiusKm)
    {
        var clusters = new List<List<SourceLocation>>();
        var remaining = new List<SourceLocation>(locations);

        while (remaining.Count > 0)
        {
            var seed = remaining[0];
            var cluster = FindWithinRadius(seed, remaining, clusterRadiusKm);
            clusters.Add(cluster);
            remaining = remaining.Except(cluster).ToList();
        }

        return clusters;
    }

    /// <summary>
    /// Calculate geographic bounds (min/max lat/lon) for a set of locations
    /// </summary>
    public static (double minLat, double maxLat, double minLon, double maxLon) CalculateBounds(List<SourceLocation> locations)
    {
        if (locations.Count == 0)
            return (0, 0, 0, 0);

        return (
            locations.Min(l => l.Lat),
            locations.Max(l => l.Lat),
            locations.Min(l => l.Lon),
            locations.Max(l => l.Lon)
        );
    }

    /// <summary>
    /// Calculate approximate world size in km (diagonal distance)
    /// </summary>
    public static double CalculateWorldSizeKm(List<SourceLocation> locations)
    {
        var bounds = CalculateBounds(locations);
        return DistanceKm(bounds.minLat, bounds.minLon, bounds.maxLat, bounds.maxLon);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
