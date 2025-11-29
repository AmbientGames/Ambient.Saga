using Ambient.Domain;
using Ambient.Saga.WorldForge;
using System.Xml.Linq;

namespace Ambient.Saga.WorldForge.Services;

/// <summary>
/// Service for generating paths and location patterns.
/// Extracted from StoryGenerator as part of SRP refactoring.
/// Handles MST path building, radial star generation, and path subdivision.
/// </summary>
public class PathGenerationService
{
    public List<SourceLocation> BuildMSTPath(List<SourceLocation> sourceLocations)
    {

        if (sourceLocations.Count <= 1)
            return sourceLocations;

        var mst = new MinimumSpanningTree();

        // Build MST using Kruskal's algorithm
        var tree = mst.BuildMST(sourceLocations);

        // Convert tree to traversable path with backtracking for branches
        var path = mst.TreeToPath(tree);

        return path;
    }
    public List<(SourceLocation from, SourceLocation to)> BuildMSTEdges(List<SourceLocation> path)
    {
        var edges = new List<(SourceLocation, SourceLocation)>();
        var seen = new HashSet<(SourceLocation, SourceLocation)>();

        for (var i = 0; i < path.Count - 1; i++)
        {
            var from = path[i];
            var to = path[i + 1];

            // Skip backtracking (duplicate locations)
            if (from == to)
                continue;

            // Add edge in normalized order to avoid duplicates
            var edge = from.GetHashCode() < to.GetHashCode() ? (from, to) : (to, from);
            if (seen.Add(edge))
            {
                edges.Add((from, to));
            }
        }

        return edges;
    }
    public List<SourceLocation> GenerateStarLocations(
        double centerLat,
        double centerLon,
        double spacing,
        RefNameGenerator refNameGenerator,
        string hubName)
    {
        var locations = new List<SourceLocation>();

        // Create center location
        var centerLocation = new SourceLocation
        {
            DisplayName = hubName,
            Description = $"Hub center at {hubName}",
            Type = SourceLocationType.Structure,
            Lat = centerLat,
            Lon = centerLon
        };
        locations.Add(centerLocation);
        refNameGenerator.GetOrGenerateRefName(centerLocation);

        // Calculate spoke count based on circumference at first ring
        var circumference = 2 * Math.PI * spacing;
        var spokeCount = (int)Math.Floor(circumference / spacing);
        if (spokeCount < 4) spokeCount = 4; // Minimum 4 spokes
        if (spokeCount > 12) spokeCount = 12; // Maximum 12 spokes

        // Fixed max radius: 5 × spacing (scales naturally with spacing parameter)
        var maxRadius = 5 * spacing;
        var pointsPerSpoke = 7; // Center + 6 rings

        const double metersPerDegree = 111320.0; // approximate at equator
        var lonScale = Math.Cos(centerLat * Math.PI / 180.0); // longitude degrees are smaller at higher latitudes

        // Generate spokes (evenly distributed angles)
        for (var spoke = 0; spoke < spokeCount; spoke++)
        {
            var angle = 2.0 * Math.PI * spoke / spokeCount; // radians

            // Generate points along this spoke (skip center which we already added)
            for (var ring = 1; ring < pointsPerSpoke; ring++)
            {
                var radiusMeters = ring * spacing;
                if (radiusMeters > maxRadius)
                    break;

                // Convert to meters offset
                var xMeters = radiusMeters * Math.Cos(angle);
                var yMeters = radiusMeters * Math.Sin(angle);

                // Convert meters to degrees
                var latOffset = yMeters / metersPerDegree;
                var lonOffset = xMeters / (metersPerDegree * lonScale);

                var starLocation = new SourceLocation
                {
                    DisplayName = $"{hubName} Spoke {spoke + 1} Ring {ring}",
                    Description = $"Star waypoint at {radiusMeters:F0}m from {hubName}",
                    Type = SourceLocationType.Landmark,
                    Lat = centerLat + latOffset,
                    Lon = centerLon + lonOffset
                };

                locations.Add(starLocation);
                refNameGenerator.GetOrGenerateRefName(starLocation);
            }
        }

        return locations;
    }
    public List<SourceLocation> SubdividePath(List<SourceLocation> path, RefNameGenerator refNameGenerator, double maxDistanceMeters = 500.0)
    {
        var result = new List<SourceLocation>();

        // Generate RefNames for all original locations first
        foreach (var location in path.Distinct())
        {
            refNameGenerator.GetOrGenerateRefName(location);
        }

        // Subdivide edges between consecutive locations
        for (var i = 0; i < path.Count; i++)
        {
            result.Add(path[i]);

            if (i < path.Count - 1)
            {
                var current = path[i];
                var next = path[i + 1];

                // Skip if same location (backtracking)
                if (current == next)
                    continue;

                var currentRefName = refNameGenerator.GetRefName(current);
                var nextRefName = refNameGenerator.GetRefName(next);

                AddSubdividedEdge(result, current, next, currentRefName, nextRefName, refNameGenerator, maxDistanceMeters);
            }
        }

        return result;
    }
    public List<SourceLocation> ExtractUniqueLocations(List<SourceLocation> path)
    {
        var uniqueLocations = new List<SourceLocation>();
        var seen = new HashSet<SourceLocation>();

        foreach (var location in path)
        {
            if (seen.Add(location))
            {
                uniqueLocations.Add(location);
            }
        }

        return uniqueLocations;
    }
    public void AddSubdividedEdge(List<SourceLocation> result, SourceLocation from, SourceLocation to,
        string fromRefName, string toRefName, RefNameGenerator refNameGenerator, double maxDistanceMeters)
    {
        var distanceKm = HaversineDistanceKm(from.Lat, from.Lon, to.Lat, to.Lon);
        var distanceMeters = distanceKm * 1000.0;

        if (distanceMeters > maxDistanceMeters)
        {
            var numSegments = (int)Math.Ceiling(distanceMeters / maxDistanceMeters);
            var numIntermediatePoints = numSegments - 1;

            for (var j = 1; j <= numIntermediatePoints; j++)
            {
                var fraction = (double)j / numSegments;
                var lat = from.Lat + (to.Lat - from.Lat) * fraction;
                var lon = from.Lon + (to.Lon - from.Lon) * fraction;

                var intermediateLandmark = new SourceLocation
                {
                    DisplayName = $"Waypoint between {from.DisplayName} and {to.DisplayName} ({j})",
                    Description = $"Intermediate waypoint between {from.DisplayName} and {to.DisplayName}",
                    Type = SourceLocationType.Landmark,
                    Lat = lat,
                    Lon = lon
                };

                refNameGenerator.GenerateIntermediateRefName(intermediateLandmark, fromRefName, toRefName, j - 1);
                result.Add(intermediateLandmark);
            }
        }
    }
    public double EstimateMSTDistance(List<SourceLocation> locations)
    {
        if (locations.Count < 2)
            return 0;

        // Build MST and sum edge lengths
        var mst = new MinimumSpanningTree();
        var tree = mst.BuildMST(locations);

        double totalDistance = 0;
        var visited = new HashSet<SourceLocation>();

        foreach (var node in tree)
        {
            visited.Add(node.Key);
            foreach (var neighbor in node.Value)
            {
                if (!visited.Contains(neighbor))
                {
                    totalDistance += HaversineDistance(node.Key.Lat, node.Key.Lon, neighbor.Lat, neighbor.Lon);
                }
            }
        }

        return totalDistance;
    }
    public double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
    public double HaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
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
    public double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>
    /// Updates WorldConfigurations.xml to point the specified world to its generated files
    /// </summary>
    private void UpdateWorldConfigurationReferences(string worldRefName, string outputDirectory)
    {
        var configPath = Path.Combine(outputDirectory, "WorldConfigurations.xml");
        var doc = XDocument.Load(configPath);
        XNamespace ns = "Ambient.Domain";

        var worldConfig = doc.Root?.Elements(ns + "WorldConfiguration")
            .FirstOrDefault(e => e.Attribute("RefName")?.Value == worldRefName);

        if (worldConfig == null)
        {
            throw new InvalidOperationException($"World configuration '{worldRefName}' not found in WorldConfigurations.xml");
        }

        // Update refs to point to generated files
        worldConfig.SetAttributeValue("SagaFeaturesRef", worldRefName);
        worldConfig.SetAttributeValue("SagaArcsRef", worldRefName);
        worldConfig.SetAttributeValue("QuestTokensRef", worldRefName);
        worldConfig.SetAttributeValue("EquipmentRef", worldRefName);
        worldConfig.SetAttributeValue("SpellsRef", worldRefName);
        worldConfig.SetAttributeValue("ConsumableItemsRef", worldRefName);
        worldConfig.SetAttributeValue("CharacterArchetypesRef", worldRefName);
        worldConfig.SetAttributeValue("CharacterAffinitiesRef", worldRefName);
        worldConfig.SetAttributeValue("CombatStancesRef", worldRefName);
        worldConfig.SetAttributeValue("LoadoutSlotsRef", worldRefName);
        worldConfig.SetAttributeValue("AvatarArchetypesRef", worldRefName);
        worldConfig.SetAttributeValue("QuestsRef", worldRefName);
        worldConfig.SetAttributeValue("SagaTriggerPatternsRef", worldRefName);
        worldConfig.SetAttributeValue("ToolsRef", worldRefName);
        worldConfig.SetAttributeValue("BuildingMaterialsRef", worldRefName);
        worldConfig.SetAttributeValue("CharactersRef", worldRefName);
        worldConfig.SetAttributeValue("DialogueTreesRef", worldRefName);
        worldConfig.SetAttributeValue("AchievementsRef", worldRefName);

        // Save with NewLineOnAttributes for better readability
        var settings = new System.Xml.XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            NewLineOnAttributes = true,
            Encoding = System.Text.Encoding.UTF8
        };

        using (var writer = System.Xml.XmlWriter.Create(configPath, settings))
        {
            doc.Save(writer);
        }
    }
    public void ValidateGenerationParameters(WorldConfiguration worldConfig, List<SourceLocation> sourceLocations, GenerationStyle generationStyle)
    {
        if (generationStyle == GenerationStyle.Trail && sourceLocations.Count < 2)
        {
            throw new InvalidOperationException(
                $"Trail generation requires at least 2 source locations. Provided: {sourceLocations.Count}. " +
                $"Use RadialExploration for 0-1 locations.");
        }
    }
    public int EstimatePointCount(WorldConfiguration worldConfig, List<SourceLocation> sourceLocations, GenerationStyle generationStyle, double spacing)
    {
        if (generationStyle == GenerationStyle.RadialExploration)
        {
            if (sourceLocations.Count <= 1)
            {
                // Single star: ~48 points (6 spokes × 8 points per spoke)
                return 48;
            }
            else
            {
                // Multiple hubs: each hub gets ~48 points + trail waypoints
                var hubPoints = sourceLocations.Count * 48;
                var totalDistance = EstimateMSTDistance(sourceLocations);
                var trailPoints = (int)(totalDistance / spacing);
                return hubPoints + trailPoints;
            }
        }
        else // Trail
        {
            // Just trail waypoints
            var totalDistance = EstimateMSTDistance(sourceLocations);
            return (int)(totalDistance / spacing);
        }
    }
}
