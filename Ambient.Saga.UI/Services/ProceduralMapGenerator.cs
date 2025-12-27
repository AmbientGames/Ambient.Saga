using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.ValueObjects;
using Ambient.Saga.UI.Models;

namespace Ambient.Saga.UI.Services;

/// <summary>
/// Generates a synthetic map for procedural worlds based on saga arc locations.
/// Creates a 1024x1024 placeholder at 3x the saga arc bounding box size,
/// allowing room for procedural content to be filled in as the world generates.
/// </summary>
public static class ProceduralMapGenerator
{
    public const int MapSize = 1024;
    private const double PaddingMultiplier = 1.0; // 1.0 = add full range to each side, resulting in 3x total size

    /// <summary>
    /// Generates a procedural map from the world's saga arcs.
    /// Returns null if there are no saga arcs to base the map on.
    /// </summary>
    public static (GeoTiffMetadata metadata, HeightMapImageData imageData)? GenerateFromSagaArcs(IWorld world)
    {
        var sagaArcs = world.SagaArcLookup.Values.ToList();

        if (sagaArcs.Count == 0)
        {
            return null;
        }

        // Calculate bounding box from saga arc positions
        var bounds = CalculateBoundingBox(sagaArcs);

        // Create metadata with the calculated bounds
        var metadata = CreateMetadata(bounds);

        // Create blank map image
        var imageData = CreateBlankMapImage();

        return (metadata, imageData);
    }

    /// <summary>
    /// Calculates the bounding box for all saga arcs with 10% padding.
    /// </summary>
    private static (double North, double South, double East, double West) CalculateBoundingBox(
        IList<SagaArc> sagaArcs)
    {
        // Find min/max coordinates
        var minLat = sagaArcs.Min(s => s.LatitudeZ);
        var maxLat = sagaArcs.Max(s => s.LatitudeZ);
        var minLon = sagaArcs.Min(s => s.LongitudeX);
        var maxLon = sagaArcs.Max(s => s.LongitudeX);

        // Calculate dimensions
        var latRange = maxLat - minLat;
        var lonRange = maxLon - minLon;

        // Handle edge case where all saga arcs are at same point
        if (latRange < 0.001) latRange = 1.0;
        if (lonRange < 0.001) lonRange = 1.0;

        // Add full range to each side (3x total size) to allow for procedural expansion
        var latPadding = latRange * PaddingMultiplier;
        var lonPadding = lonRange * PaddingMultiplier;

        return (
            North: maxLat + latPadding,
            South: minLat - latPadding,
            East: maxLon + lonPadding,
            West: minLon - lonPadding
        );
    }

    /// <summary>
    /// Creates GeoTiffMetadata for the procedural map.
    /// </summary>
    private static GeoTiffMetadata CreateMetadata((double North, double South, double East, double West) bounds)
    {
        var width = bounds.East - bounds.West;
        var height = bounds.North - bounds.South;

        // Calculate pixel scale (degrees per pixel)
        var pixelScaleX = width / MapSize;
        var pixelScaleY = height / MapSize;

        return new GeoTiffMetadata
        {
            North = bounds.North,
            South = bounds.South,
            East = bounds.East,
            West = bounds.West,
            ImageWidth = MapSize,
            ImageHeight = MapSize,
            BitsPerSample = 16,
            SamplesPerPixel = 1,
            PixelScale = (pixelScaleX, pixelScaleY, 0),
            // TiePoint: pixel (0,0) maps to (West, North) - top-left corner
            TiePoint = (0, 0, 0, bounds.West, bounds.North, 0)
        };
    }

    /// <summary>
    /// Creates a blank 1024x1024 map image with a subtle gradient.
    /// </summary>
    private static HeightMapImageData CreateBlankMapImage()
    {
        var stride = MapSize * 4; // 4 bytes per pixel for BGRA32
        var data = new byte[MapSize * stride];

        for (int y = 0; y < MapSize; y++)
        {
            for (int x = 0; x < MapSize; x++)
            {
                // Create subtle radial gradient from center (lighter) to edges (darker)
                var centerX = MapSize / 2.0;
                var centerY = MapSize / 2.0;
                var maxDist = Math.Sqrt(centerX * centerX + centerY * centerY);
                var dist = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));

                // Base colors for a terrain-like appearance
                // Slight green/brown tint for land feel
                var brightness = 1.0 - (dist / maxDist * 0.3); // 1.0 at center, 0.7 at edges
                var baseR = (byte)(140 * brightness);
                var baseG = (byte)(160 * brightness);
                var baseB = (byte)(120 * brightness);

                var index = y * stride + x * 4;
                data[index] = baseB;     // Blue
                data[index + 1] = baseG; // Green
                data[index + 2] = baseR; // Red
                data[index + 3] = 255;   // Alpha (fully opaque)
            }
        }

        return new HeightMapImageData(data, MapSize, MapSize, stride);
    }
}
