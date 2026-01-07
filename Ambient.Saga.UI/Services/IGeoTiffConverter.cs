namespace Ambient.Saga.UI.Services;

/// <summary>
/// Service for converting GeoTIFF files to a standardized format (UInt16, LZW compression).
/// Uses GDAL for proper handling of various GeoTIFF formats.
/// </summary>
public interface IGeoTiffConverter
{
    /// <summary>
    /// Converts a GeoTIFF file to standardized UInt16 format with LZW compression.
    /// Preserves geotransform and projection information.
    /// </summary>
    /// <param name="inputPath">Path to the input GeoTIFF file</param>
    /// <param name="outputPath">Path for the converted output file</param>
    /// <param name="progress">Optional progress callback (0.0 to 1.0)</param>
    /// <returns>True if conversion succeeded, false otherwise</returns>
    bool Convert(string inputPath, string outputPath, Action<double>? progress = null);

    /// <summary>
    /// Converts a GeoTIFF file asynchronously.
    /// </summary>
    Task<bool> ConvertAsync(string inputPath, string outputPath, Action<double>? progress = null);

    /// <summary>
    /// Gets information about a GeoTIFF file without converting it.
    /// </summary>
    GeoTiffInfo? GetInfo(string inputPath);

    /// <summary>
    /// Whether GDAL is properly initialized and available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Error message if GDAL is not available.
    /// </summary>
    string? ErrorMessage { get; }
}

/// <summary>
/// Information about a GeoTIFF file.
/// </summary>
public record GeoTiffInfo(
    int Width,
    int Height,
    int BandCount,
    string? Projection,
    double[] GeoTransform);
