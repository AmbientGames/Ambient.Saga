namespace Ambient.Domain.Contracts;

/// <summary>
/// Interface for complete height map metadata including geographic bounds and TIFF properties.
/// Provides strongly-typed access to decoded GeoTIFF information for mathematical operations.
/// </summary>
public interface IHeightMapMetadata
{
    // Geographic bounds
    double North { get; }
    double South { get; }
    double East { get; }
    double West { get; }
    double Width { get; }
    double Height { get; }
    
    // TIFF image properties
    int ImageWidth { get; }
    int ImageHeight { get; }
    int? BitsPerSample { get; }
    int? SamplesPerPixel { get; }
    
    // GeoTIFF transformation data
    (double X, double Y, double Z) PixelScale { get; }
    (double I, double J, double K, double X, double Y, double Z) TiePoint { get; }
}