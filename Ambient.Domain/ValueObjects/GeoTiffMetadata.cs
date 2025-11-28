using Ambient.Domain;

namespace Ambient.Domain.ValueObjects;

/// <summary>
/// Complete GeoTIFF metadata including bounds and all decoded TIFF properties.
/// </summary>
public class GeoTiffMetadata : IHeightMapMetadata
{
    public double North { get; init; }
    public double South { get; init; }
    public double East { get; init; }
    public double West { get; init; }
    public double Width => East - West;
    public double Height => North - South;
    
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public int? BitsPerSample { get; init; }
    public int? SamplesPerPixel { get; init; }
    
    public (double X, double Y, double Z) PixelScale { get; init; }
    public (double I, double J, double K, double X, double Y, double Z) TiePoint { get; init; }
}
