namespace Ambient.Infrastructure.GameLogic;

/// <summary>
/// Interface for resolving content paths with AppData and install location fallback.
/// </summary>
public interface IContentPathResolver
{
    string? ResolveTexturePath(string library, string ns, string textureName);
    string? ResolveGeographicDataPath(string library, string ns, string fileName);
    string? ResolveModelPath(string library, string ns, string modelName);
    string? ResolveModelPathByCategoryKind(string library, string ns, string category, string? kind, Random? random = null);
    string? ResolveModelRefByCategoryKind(string library, string ns, string category, string? kind, Random? random = null);
    string? ResolveXmlPath(string worldRef, string library, string ns, params string[] relativePath);

    /// <summary>
    /// Registers an override base path for a world (e.g., a temp extraction directory).
    /// The resolver checks this path first before Documents/Install.
    /// </summary>
    void RegisterWorldPath(string worldRef, string basePath) { }
}
