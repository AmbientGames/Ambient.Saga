using Ambient.Domain.ValueObjects;
using BitMiracle.LibTiff.Classic;

namespace Ambient.Infrastructure.Sampling;

/// <summary>
/// Provides methods for reading GeoTIFF metadata and geographic bounds.
/// Uses LibTiff.NET for cross-platform compatibility.
/// </summary>
public static class GeoTiffReader
{
    private const int ModelPixelScaleTag = 33550;
    private const int ModelTiepointTag = 33922;
    private const int GeoKeyDirectoryTag = 34735;
    private const int GeoAsciiParamsTag = 34737;

    /// <summary>
    /// Reads complete GeoTIFF metadata including bounds and all decoded properties.
    /// </summary>
    /// <param name="filePath">Path to the GeoTIFF file</param>
    /// <returns>Complete metadata with strongly-typed properties</returns>
    public static GeoTiffMetadata ReadMetadata(string filePath)
    {
        return ExecuteWithTiff(filePath, "read complete metadata", tiff =>
        {
            // Get image dimensions
            var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

            // Get optional properties
            var bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE);
            var samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL);

            // Try to read GeoTIFF transformation information
            var pixelScale = ReadModelPixelScale(tiff);
            var tiePoint = ReadModelTiepoint(tiff);

            // Calculate bounds using pixel scale and tie point
            var (north, south, east, west) = CalculateBounds(width, height, pixelScale, tiePoint);

            return new GeoTiffMetadata
            {
                North = north,
                South = south,
                East = east,
                West = west,
                ImageWidth = width,
                ImageHeight = height,
                BitsPerSample = bitsPerSample?[0].ToInt(),
                SamplesPerPixel = samplesPerPixel?[0].ToInt(),
                PixelScale = pixelScale,
                TiePoint = tiePoint
            };
        });
    }


    /// <summary>
    /// Common method to execute operations with TIFF file handling and error management.
    /// </summary>
    private static T? ExecuteWithTiff<T>(string filePath, string operation, Func<Tiff, T?> action) where T : class
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"GeoTIFF file not found: {filePath}");

        using var tiff = Tiff.Open(filePath, "r");
        if (tiff == null)
            throw new InvalidOperationException($"Cannot open TIFF file: {filePath}");

        try
        {
            return action(tiff);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error reading GeoTIFF {operation} from {filePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Calculates geographic bounds from image dimensions and transformation parameters.
    /// </summary>
    private static (double North, double South, double East, double West) CalculateBounds(int width, int height, 
        (double X, double Y, double Z) pixelScale, 
        (double I, double J, double K, double X, double Y, double Z) tiePoint)
    {
        var originX = tiePoint.X;
        var originY = tiePoint.Y;
        var pixelSizeX = pixelScale.X;
        var pixelSizeY = pixelScale.Y;

        // Bounds for north-up imagery:
        var west = originX;
        var east = originX + (width * pixelSizeX);
        var north = originY;
        var south = originY - (height * pixelSizeY);

        return (north, south, east, west);
    }

    /// <summary>
    /// Reads the ModelPixelScaleTag (33550) which contains pixel size in map units.
    /// </summary>
    private static (double X, double Y, double Z) ReadModelPixelScale(Tiff tiff)
    {
        var field = tiff.GetField((TiffTag)ModelPixelScaleTag);
        if (field == null || field.Length == 0)
            return (0, 0, 0);

        try
        {
            var data = field[1].GetBytes();
            if (data.Length < 24) // Need at least 3 doubles (8 bytes each)
                return (0, 0, 0);

            var scaleX = BitConverter.ToDouble(data, 0);
            var scaleY = BitConverter.ToDouble(data, 8);
            var scaleZ = BitConverter.ToDouble(data, 16);

            return (scaleX, scaleY, scaleZ);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    /// <summary>
    /// Reads the ModelTiepointTag (33922) which contains raster-to-model coordinate transformation.
    /// </summary>
    private static (double I, double J, double K, double X, double Y, double Z) ReadModelTiepoint(Tiff tiff)
    {
        var field = tiff.GetField((TiffTag)ModelTiepointTag);
        if (field == null || field.Length == 0)
            return (0, 0, 0, 0, 0, 0);

        try
        {
            var data = field[1].GetBytes();
            if (data.Length < 48) // Need at least 6 doubles (8 bytes each)
                return (0, 0, 0, 0, 0, 0);

            var i = BitConverter.ToDouble(data, 0);  // Raster X (pixel)
            var j = BitConverter.ToDouble(data, 8);  // Raster Y (line)
            var k = BitConverter.ToDouble(data, 16); // Raster Z (elevation)
            var x = BitConverter.ToDouble(data, 24); // Model X (longitude)
            var y = BitConverter.ToDouble(data, 32); // Model Y (latitude)
            var z = BitConverter.ToDouble(data, 40); // Model Z (elevation)

            return (i, j, k, x, y, z);
        }
        catch
        {
            return (0, 0, 0, 0, 0, 0);
        }
    }
}