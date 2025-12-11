using Ambient.Saga.Presentation.UI.Models;

namespace Ambient.Saga.Presentation.UI.Services;

/// <summary>
/// Platform-agnostic interface for creating textures from image data.
/// Implementations provide the graphics API-specific texture creation (DirectX11, OpenGL, Vulkan, etc.)
/// </summary>
public interface ITextureProvider
{
    /// <summary>
    /// Creates a texture from HeightMapImageData for use with ImGui.Image()
    /// </summary>
    /// <param name="imageData">Platform-agnostic image data (BGRA pixels)</param>
    /// <returns>Texture pointer for ImGui, dimensions, and disposable resources</returns>
    (nint texturePtr, int width, int height, IDisposable[] resources) CreateTextureFromImageData(HeightMapImageData imageData);

    /// <summary>
    /// Disposes texture resources created by CreateTextureFromImageData
    /// </summary>
    void DisposeTexture(IDisposable[]? resources);
}
