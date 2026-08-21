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

    /// <summary>
    /// EVERY model matching a Category/Kind, sorted, as names. Generation calls this to
    /// write the world's model set down; nothing at runtime should be enumerating a
    /// directory, because the answer changes whenever a file is added and differs
    /// between machines with their own content packs.
    /// </summary>
    IReadOnlyList<string> EnumerateModelRefsByCategoryKind(string library, string ns, string category, string? kind)
        => Array.Empty<string>();
    string? ResolveXmlPath(string worldRef, string library, string ns, params string[] relativePath);

    /// <summary>
    /// Registers an override base path for a world (e.g., a temp extraction directory).
    /// The resolver checks this path first before Documents/Install.
    /// </summary>
    void RegisterWorldPath(string worldRef, string basePath) { }
}
