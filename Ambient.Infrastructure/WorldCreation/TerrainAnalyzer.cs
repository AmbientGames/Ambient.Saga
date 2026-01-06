using Ambient.Application.WorldCreation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ambient.Infrastructure.WorldCreation;

/// <summary>
/// Analyzes GeoTIFF terrain files to extract elevation data and calculate world parameters.
/// </summary>
public class TerrainAnalyzer : ITerrainAnalyzer
{
    /// <inheritdoc/>
    public TerrainAnalysisResult Analyze(string tifPath)
    {
        if (!File.Exists(tifPath))
            throw new FileNotFoundException($"Terrain file not found: {tifPath}");

        using var image = Image.Load<L16>(tifPath);

        // Extract height data and find min/max
        int minElevation = int.MaxValue;
        int maxElevation = int.MinValue;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var height = row[x].PackedValue;
                    if (height < minElevation) minElevation = height;
                    if (height > maxElevation) maxElevation = height;
                }
            }
        });

        // Calculate vertical shift and chunk height
        var verticalShift = WorldConfigurationBuilder.CalculateVerticalShift(minElevation);
        var chunkHeight = WorldConfigurationBuilder.CalculateChunkHeight(minElevation, maxElevation);

        // Try to read GeoTransform from the file
        var geoTransform = TryReadGeoTransform(tifPath);

        return new TerrainAnalysisResult
        {
            MinElevation = minElevation,
            MaxElevation = maxElevation,
            Width = image.Width,
            Height = image.Height,
            GeoTransform = geoTransform,
            VerticalShift = verticalShift,
            RecommendedChunkHeight = chunkHeight
        };
    }

    /// <summary>
    /// Try to read GeoTransform from GeoTIFF metadata.
    /// Returns [originX, pixelWidth, rotX, originY, rotY, pixelHeight] or null if not available.
    /// </summary>
    private static double[]? TryReadGeoTransform(string tifPath)
    {
        try
        {
            var metadata = Sampling.GeoTiffReader.ReadMetadata(tifPath);
            if (metadata != null && metadata.PixelScale.X != 0)
            {
                var pixelScale = metadata.PixelScale;
                var tiePoint = metadata.TiePoint;

                // Build GeoTransform: [originX, pixelWidth, rotX, originY, rotY, pixelHeight]
                return new[]
                {
                    tiePoint.X,           // originX (longitude of top-left)
                    pixelScale.X,         // pixelWidth
                    0.0,                  // rotX (rotation, usually 0)
                    tiePoint.Y,           // originY (latitude of top-left)
                    0.0,                  // rotY (rotation, usually 0)
                    -pixelScale.Y         // pixelHeight (negative for north-up)
                };
            }
        }
        catch
        {
            // GeoTransform not available - that's ok for basic terrain analysis
        }

        return null;
    }
}
