namespace Ambient.Application.WorldCreation;

/// <summary>
/// Analyzes terrain data from GeoTIFF files.
/// </summary>
public interface ITerrainAnalyzer
{
    /// <summary>
    /// Analyze a GeoTIFF file and extract terrain metadata.
    /// </summary>
    /// <param name="tifPath">Path to the GeoTIFF file</param>
    /// <returns>Terrain analysis results</returns>
    TerrainAnalysisResult Analyze(string tifPath);
}

/// <summary>
/// Results from terrain analysis.
/// </summary>
public class TerrainAnalysisResult
{
    public int MinElevation { get; init; }
    public int MaxElevation { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>
    /// GeoTransform array: [originX, pixelWidth, rotX, originY, rotY, pixelHeight]
    /// </summary>
    public double[]? GeoTransform { get; init; }

    /// <summary>
    /// Calculated vertical shift based on min elevation.
    /// </summary>
    public int VerticalShift { get; init; }

    /// <summary>
    /// Recommended chunk height to contain the terrain.
    /// </summary>
    public int RecommendedChunkHeight { get; init; }
}
