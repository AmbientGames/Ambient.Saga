using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace Ambient.Rpg.Rendering.DirectX;

/// <summary>
/// DirectX11 implementation of ITextureProvider.
/// Converts platform-agnostic HeightMapImageData to DirectX11 textures for ImGui rendering.
/// </summary>
public class D3D11TextureProvider : ITextureProvider
{
    private readonly Device _device;

    public D3D11TextureProvider(Device device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>
    /// Creates a DirectX11 Texture2D with ShaderResourceView from HeightMapImageData.
    /// Returns the SRV pointer for use with ImGui.Image()
    /// </summary>
    public (nint texturePtr, int width, int height, IDisposable[] resources) CreateTextureFromImageData(HeightMapImageData imageData)
    {
        if (imageData == null)
            throw new ArgumentNullException(nameof(imageData));

        var width = imageData.Width;
        var height = imageData.Height;
        var stride = imageData.Stride;
        var pixels = imageData.PixelData;

        var textureDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        };

        var dataPointer = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(pixels, 0);
        var dataBox = new SharpDX.DataBox(dataPointer, stride, 0);

        var texture = new Texture2D(_device, textureDesc, new[] { dataBox });

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

        var srv = new ShaderResourceView(_device, texture, srvDesc);

        return (srv.NativePointer, width, height, new IDisposable[] { texture, srv });
    }

    /// <summary>
    /// Disposes texture resources created by CreateTextureFromImageData
    /// </summary>
    public void DisposeTexture(IDisposable[]? resources)
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
