namespace Ambient.Saga.WorldForge;

/// <summary>
/// Finds shortest path through a set of geographic points using Greedy Nearest Neighbor + 2-opt optimization
/// </summary>
public class PathFinder
{
    /// <summary>
    /// Represents a geographic point with latitude and longitude
    /// </summary>
    public record GeoPoint(double Latitude, double Longitude, string Id);

    /// <summary>
    /// Finds the shortest path visiting all points, starting from the first point
    /// </summary>
    /// <param name="points">Points to visit (first point is the starting location)</param>
    /// <returns>Ordered list of points representing the shortest path found</returns>
    public List<GeoPoint> FindShortestPath(List<GeoPoint> points)
    {
        if (points.Count <= 1)
            return new List<GeoPoint>(points);

        // Phase 1: Greedy Nearest Neighbor
        var greedyPath = GreedyNearestNeighbor(points);

        // Phase 2: 2-opt optimization
        var optimizedPath = TwoOptOptimization(greedyPath);

        return optimizedPath;
    }

    /// <summary>
    /// Greedy nearest neighbor: Start at first point, always go to nearest unvisited point
    /// </summary>
    private List<GeoPoint> GreedyNearestNeighbor(List<GeoPoint> points)
    {
        var path = new List<GeoPoint> { points[0] };
        var remaining = new HashSet<GeoPoint>(points.Skip(1));

        var current = points[0];

        while (remaining.Count > 0)
        {
            // Find nearest unvisited point
            var nearest = remaining.MinBy(p => HaversineDistance(current, p))!;
            path.Add(nearest);
            remaining.Remove(nearest);
            current = nearest;
        }

        return path;
    }

    /// <summary>
    /// 2-opt optimization: Iteratively swap edges to reduce total path length
    /// </summary>
    private List<GeoPoint> TwoOptOptimization(List<GeoPoint> path)
    {
        if (path.Count <= 3)
            return path;

        var improved = true;
        var currentPath = new List<GeoPoint>(path);

        while (improved)
        {
            improved = false;

            for (var i = 1; i < currentPath.Count - 1; i++)
            {
                for (var j = i + 1; j < currentPath.Count; j++)
                {
                    // Try swapping edge (i-1, i) and (j, j+1) with (i-1, j) and (i, j+1)
                    // This reverses the section between i and j

                    var oldDistance = HaversineDistance(currentPath[i - 1], currentPath[i]) +
                                     (j < currentPath.Count - 1 ? HaversineDistance(currentPath[j], currentPath[j + 1]) : 0);

                    var newDistance = HaversineDistance(currentPath[i - 1], currentPath[j]) +
                                     (j < currentPath.Count - 1 ? HaversineDistance(currentPath[i], currentPath[j + 1]) : 0);

                    if (newDistance < oldDistance)
                    {
                        // Reverse the section between i and j
                        currentPath.Reverse(i, j - i + 1);
                        improved = true;
                    }
                }
            }
        }

        return currentPath;
    }

    /// <summary>
    /// Calculates distance between two geographic points using Haversine formula (in kilometers)
    /// </summary>
    private double HaversineDistance(GeoPoint p1, GeoPoint p2)
    {
        const double R = 6371.0; // Earth's radius in kilometers

        var lat1 = ToRadians(p1.Latitude);
        var lat2 = ToRadians(p2.Latitude);
        var dLat = ToRadians(p2.Latitude - p1.Latitude);
        var dLon = ToRadians(p2.Longitude - p1.Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    private double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
