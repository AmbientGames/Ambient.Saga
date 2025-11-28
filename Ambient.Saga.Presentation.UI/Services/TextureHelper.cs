using Ambient.Saga.Presentation.UI.Models;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace Ambient.Saga.Presentation.UI.Services;

/// <summary>
/// Helper for converting platform-agnostic image data to DirectX11 textures for ImGui rendering
/// </summary>
public static class TextureHelper
{
    /// <summary>
    /// Converts HeightMapImageData to a DirectX11 Texture2D with ShaderResourceView
    /// Returns the SRV pointer for use with ImGui.Image()
    /// </summary>
    public static (nint texturePtr, int width, int height, IDisposable[] resources) CreateTextureFromImageData(
        SharpDX.Direct3D11.Device device,
        HeightMapImageData imageData)
    {
        if (imageData == null)
            throw new ArgumentNullException(nameof(imageData));

        var width = imageData.Width;
        var height = imageData.Height;
        var stride = imageData.Stride;
        var pixels = imageData.PixelData;

        // Create DirectX11 Texture2D
        var textureDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, // BGRA format
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        };

        // Pin pixel data and create texture
        var dataPointer = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(pixels, 0);
        var dataBox = new SharpDX.DataBox(dataPointer, stride, 0);

        var texture = new Texture2D(device, textureDesc, new[] { dataBox });

        // Create ShaderResourceView
        var srvDesc = new ShaderResourceViewDescription
        {
            Format = textureDesc.Format,
            Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
            Texture2D = new ShaderResourceViewDescription.Texture2DResource
            {
                MipLevels = 1,
                MostDetailedMip = 0
            }
        };

        var srv = new ShaderResourceView(device, texture, srvDesc);

        // Return SRV pointer and resources to dispose later
        return (srv.NativePointer, width, height, new IDisposable[] { texture, srv });
    }

    /// <summary>
    /// Disposes texture resources created by CreateTextureFromBitmap
    /// </summary>
    public static void DisposeTexture(IDisposable[] resources)
    {
        if (resources != null)
        {
            foreach (var resource in resources)
            {
                resource?.Dispose();
            }
        }
    }
}
