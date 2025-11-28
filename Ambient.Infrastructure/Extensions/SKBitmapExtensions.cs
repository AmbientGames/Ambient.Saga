using SkiaSharp;

namespace Ambient.Infrastructure.Extensions;

/// <summary>
/// Provides extension methods for SKBitmap to facilitate saving as PNG format.
/// </summary>
public static class SKBitmapExtensions
{
    /// <summary>
    /// Saves an SKBitmap as a PNG file to the specified file path.
    /// </summary>
    /// <param name="skBitmap">The SKBitmap to save.</param>
    /// <param name="filepath">The file path where the PNG should be saved.</param>
    /// <param name="quality">The quality of the PNG encoding (0-100). Default is 100.</param>
    public static void SaveSKBitmapAsPNG(this SKBitmap skBitmap, string filepath, int quality = 100)

    {
        using (var output = File.OpenWrite(filepath))

        {
            using (var data = skBitmap.Encode(SKEncodedImageFormat.Png, quality))

            {
                data.SaveTo(output);
            }
        }
    }    /// <summary>
    /// Encodes an SKBitmap as PNG data and returns it as a byte array.
    /// </summary>
    /// <param name="skBitmap">The SKBitmap to encode.</param>
    /// <param name="quality">The quality of the PNG encoding (0-100). Default is 100.</param>
    /// <returns>A byte array containing the PNG-encoded image data.</returns>
    public static byte[] SaveSKBitmapAsPNG(this SKBitmap skBitmap, int quality = 100)

    {
        byte[] pngBytes;
        using (var output = new MemoryStream())

        {
            using (var data = skBitmap.Encode(SKEncodedImageFormat.Png, quality))

            {
                data.SaveTo(output);
            }

            pngBytes = output.ToArray();
        }

        return pngBytes;
    }
}