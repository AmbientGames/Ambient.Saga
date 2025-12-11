namespace Ambient.Saga.UI.Models;

/// <summary>
/// Platform-agnostic heightmap image data.
/// Compatible with both WPF (BitmapSource) and ImGui/DirectX11 (Texture2D) rendering.
/// </summary>
public class HeightMapImageData
{
    /// <summary>
    /// Raw pixel data in BGRA32 format (4 bytes per pixel: Blue, Green, Red, Alpha)
    /// </summary>
    public byte[] PixelData { get; init; }

    /// <summary>
    /// Image width in pixels
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Image height in pixels
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Number of bytes per row (typically Width * 4 for BGRA32)
    /// </summary>
    public int Stride { get; init; }

    /// <summary>
    /// Pixel format description (currently always BGRA32)
    /// </summary>
    public string Format { get; init; } = "BGRA32";

    public HeightMapImageData(byte[] pixelData, int width, int height, int stride)
    {
        PixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
        Width = width;
        Height = height;
        Stride = stride;
    }
}
