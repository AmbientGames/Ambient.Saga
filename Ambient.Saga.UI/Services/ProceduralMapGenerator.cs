using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.ValueObjects;
using Ambient.Saga.UI.Models;

namespace Ambient.Saga.UI.Services;

public static class ProceduralMapGenerator
{
    public const int MapSize = 1024;

    public static (GeoTiffMetadata metadata, HeightMapImageData imageData)? CreateFromWorld(IWorld world, double latitudeDegreesToUnits = 250, double longitudeDegreesToUnits = 250, double unitsPerCell = 16)
    {
        var imageData = CreateBlankBGRA32MapImage();
        var metadata = CreateMetadata(world, 250, 250, 16);

        return (metadata, imageData);
    }

    private static GeoTiffMetadata CreateMetadata(IWorld world, double latitudeDegreesToUnits, double longitudeDegreesToUnits, double unitsPerCell)
    {
        var spawnLat = world.WorldConfiguration.SpawnLatitude;
        var spawnLon = 0.0;

        // degrees per pixel (cell)
        var degreesPerPixelLon = unitsPerCell / longitudeDegreesToUnits;
        var degreesPerPixelLat = unitsPerCell / latitudeDegreesToUnits;

        // total span in degrees
        var totalLongitudeSpanDegrees = degreesPerPixelLon * MapSize;
        var totalLatitudeSpanDegrees = degreesPerPixelLat * MapSize;

        // spawn is the center of the TIFF
        var west = spawnLon - (totalLongitudeSpanDegrees / 2.0);
        var east = spawnLon + (totalLongitudeSpanDegrees / 2.0);
        var north = spawnLat + (totalLatitudeSpanDegrees / 2.0);
        var south = spawnLat - (totalLatitudeSpanDegrees / 2.0);

        return new GeoTiffMetadata
        {
            North = north,
            South = south,
            East = east,
            West = west,
            ImageWidth = MapSize,
            ImageHeight = MapSize,
            BitsPerSample = 16,
            SamplesPerPixel = 1,
            PixelScale = (degreesPerPixelLon, degreesPerPixelLat, 0),
            TiePoint = (0, 0, 0, west, north, 0)
        };
    }

    private static HeightMapImageData CreateBlankBGRA32MapImage()
    {
        var stride = MapSize * 4;
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
