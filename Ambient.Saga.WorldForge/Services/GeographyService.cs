using Ambient.Domain;
using Ambient.Saga.WorldForge;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.Services;

/// <summary>
/// Shared geography and distance calculation utilities.
/// </summary>
public class GeographyService
{
    private readonly RefNameGenerator _refNameGenerator;

    public GeographyService(RefNameGenerator refNameGenerator)
    {
        _refNameGenerator = refNameGenerator;
    }

    /// <summary>
    /// Calculate distance between two lat/lon points in kilometers
    /// </summary>
    public double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0; // Earth's radius in kilometers
        var lat1Rad = ToRadians(lat1);
        var lat2Rad = ToRadians(lat2);
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    /// <summary>
    /// Find nearby locations sorted by distance
    /// </summary>
    public List<string> FindNearbyLocations(SourceLocation location, List<SourceLocation> allLocations, int maxCount)
    {
        var distances = allLocations
            .Where(l => l != location)
            .Select(l => new
            {
                Location = l,
                Distance = HaversineDistance(location.Lat, location.Lon, l.Lat, l.Lon)
            })
            .OrderBy(x => x.Distance)
            .Take(maxCount)
            .Select(x => _refNameGenerator.GetRefName(x.Location))
            .ToList();

        return distances;
    }

    private double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
