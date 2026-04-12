using Ambient.Application.Contracts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

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

/// <summary>
/// Resolves content paths with AppData and install location fallback support.
/// </summary>
public class ContentPathResolver : IContentPathResolver
{
    private readonly IGameSettings _gameSettings;
    private readonly ILogger<ContentPathResolver>? _logger;
    private readonly ConcurrentDictionary<string, string> _worldPathOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _modelPathCache = new(StringComparer.OrdinalIgnoreCase);

    private const string DefaultPack = "default";
    private const string DefaultNamespace = "ambient_games";

    public ContentPathResolver(IGameSettings gameSettings, ILogger<ContentPathResolver>? logger = null)
    {
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _logger = logger;
        _logger?.LogInformation("ContentPathResolver initialized with PublisherFolder={Publisher}, GameName={Game}",
            gameSettings.PublisherFolder, gameSettings.GameName);
    }

    public void RegisterWorldPath(string worldRef, string basePath)
    {
        _worldPathOverrides[worldRef] = basePath;
        System.Diagnostics.Debug.WriteLine($"[ContentPathResolver] Registered override for '{worldRef}': {basePath}");
    }

    public string? ResolveTexturePath(string library, string ns, string textureName)
    {
        return ResolvePath("packs/libraries", library, ns, "textures", "block", textureName + ".png");
    }

    public string? ResolveGeographicDataPath(string library, string ns, string fileName)
    {
        return ResolvePath("packs/libraries", library, ns, "geographic_data", fileName);
    }

    /// <summary>
    /// Resolves model file path with library fallback support.
    /// Resolution order: library -> default library
    /// Supports .litematic, .schematic, and .xml model files.
    /// </summary>
    public string? ResolveModelPath(string library, string ns, string modelName)
    {
        // Try each supported extension in order of preference
        string[] extensions = [".litematic", ".schematic", ".xml"];

        foreach (var ext in extensions)
        {
            var path = ResolvePath("packs/libraries", library, ns, "models", "block", modelName + ext);
            if (path != null)
                return path;
        }

        return null;
    }

    /// <summary>
    /// Resolves model file path by Category and Kind using directory-based lookup.
    /// Resolution order: models/{Category}/{Kind}/ -> models/{Category}/ -> models/Default/
    /// Returns a random model from the matching directory.
    /// </summary>
    public string? ResolveModelPathByCategoryKind(string library, string ns, string category, string? kind, Random? random = null)
    {
        // Cache key: resolved model path is deterministic for same category/kind/library/ns
        var cacheKey = $"{library}|{ns}|{category}|{kind ?? ""}";
        if (_modelPathCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var rng = random ?? new Random();
        string[] extensions = ["*.litematic", "*.schematic", "*.xml"];

        string? result = null;

        // 1. Try models/{Category}/{Kind}/ first
        if (!string.IsNullOrEmpty(kind) && kind != "Default")
        {
            var kindPath = ResolveModelDirectoryPath(library, ns, category, kind);
            result = SelectRandomModelFromDirectory(kindPath, extensions, rng);
        }

        // 2. Try models/{Category}/
        if (result == null)
        {
            var categoryPath = ResolveModelDirectoryPath(library, ns, category);
            result = SelectRandomModelFromDirectory(categoryPath, extensions, rng);
        }

        // 3. Fall back to models/Default/
        if (result == null)
        {
            var defaultPath = ResolveModelDirectoryPath(library, ns, "Default");
            result = SelectRandomModelFromDirectory(defaultPath, extensions, rng);
        }

        _modelPathCache[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// Gets the model reference (filename without extension) for a Category/Kind.
    /// </summary>
    public string? ResolveModelRefByCategoryKind(string library, string ns, string category, string? kind, Random? random = null)
    {
        var modelPath = ResolveModelPathByCategoryKind(library, ns, category, kind, random);
        return modelPath != null ? Path.GetFileNameWithoutExtension(modelPath) : null;
    }

    private string? ResolveModelDirectoryPath(string library, string ns, params string[] categoryPath)
    {
        var subPath = new[] { "models" }.Concat(categoryPath).ToArray();

        // Check Documents location first (user-created libraries)
        var documentsPath = Path.Combine(
            _gameSettings.GetDocumentsBasePath(),
            "packs/libraries", library, "assets", ns, Path.Combine(subPath));
        if (Directory.Exists(documentsPath))
            return documentsPath;

        // Fall back to install location
        var relativePath = BuildRelativePath("packs/libraries", library, ns, subPath);
        var installPath = Path.Combine(_gameSettings.InstallPath, relativePath);
        if (Directory.Exists(installPath))
            return installPath;

        // Fall back to default pack
        if (library != DefaultPack || ns != DefaultNamespace)
        {
            // Check Documents default
            var defaultDocumentsPath = Path.Combine(
                _gameSettings.GetDocumentsBasePath(),
                "packs/libraries", DefaultPack, "assets", DefaultNamespace, Path.Combine(subPath));
            if (Directory.Exists(defaultDocumentsPath))
                return defaultDocumentsPath;

            // Check install default
            var defaultRelativePath = BuildRelativePath("packs/libraries", DefaultPack, DefaultNamespace, subPath);
            var defaultInstallPath = Path.Combine(_gameSettings.InstallPath, defaultRelativePath);
            if (Directory.Exists(defaultInstallPath))
                return defaultInstallPath;
        }

        return null;
    }

    private static string? SelectRandomModelFromDirectory(string? directoryPath, string[] patterns, Random rng)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            return null;

        var modelFiles = patterns
            .SelectMany(p => Directory.GetFiles(directoryPath, p))
            .ToArray();

        if (modelFiles.Length == 0)
            return null;

        return modelFiles[rng.Next(modelFiles.Length)];
    }

    /// <summary>
    /// Resolves XML content path with world-specific override support.
    /// Resolution order: world -> library -> default library
    /// </summary>
    public string? ResolveXmlPath(string worldRef, string library, string ns, params string[] relativePath)
    {
        var xmlSubPath = new[] { "xml" }.Concat(relativePath).ToArray();

        // 1. Check world-specific location
        var worldPath = ResolveWorldPath(worldRef, ns, xmlSubPath);
        if (worldPath != null)
            return worldPath;

        // 2. Check library location
        var libraryPath = ResolvePath("packs/libraries", library, ns, xmlSubPath);
        if (libraryPath != null)
            return libraryPath;

        // 3. Fall back to default library (if not already checked)
        if (library != DefaultPack || ns != DefaultNamespace)
        {
            var defaultPath = ResolvePath("packs/libraries", DefaultPack, DefaultNamespace, xmlSubPath);
            if (defaultPath != null)
                return defaultPath;
        }

        return null;
    }

    private string? ResolveWorldPath(string worldRef, string ns, string[] subPath)
    {
        // Check override path first (e.g., downloaded server world in temp)
        if (_worldPathOverrides.TryGetValue(worldRef, out var overridePath))
        {
            var overrideFilePath = Path.Combine(overridePath, "assets", ns, Path.Combine(subPath));
            if (File.Exists(overrideFilePath))
                return overrideFilePath;
        }

        var worldRelativePath = Path.Combine(worldRef, "assets", ns, Path.Combine(subPath));

        // Check Documents location first (user-created worlds)
        var documentsPath = Path.Combine(_gameSettings.GetWorldsPath(), worldRelativePath);
        if (File.Exists(documentsPath))
            return documentsPath;

        // Fall back to install location
        var installRelativePath = Path.Combine("content", "worlds", worldRef, "assets", ns, Path.Combine(subPath));
        var installPath = Path.Combine(_gameSettings.InstallPath, installRelativePath);
        if (File.Exists(installPath))
            return installPath;

        return null;
    }

    private string? ResolvePath(string packType, string pack, string ns, params string[] subPath)
    {
        // Check Documents location first (user-created libraries)
        // Path: Documents/{Publisher}/{Game}/packs/libraries/{pack}/assets/{ns}/...
        var documentsLibraryPath = Path.Combine(
            _gameSettings.GetDocumentsBasePath(),
            packType, pack, "assets", ns, Path.Combine(subPath));
        if (File.Exists(documentsLibraryPath))
            return documentsLibraryPath;

        // Fall back to install location
        var relativePath = BuildRelativePath(packType, pack, ns, subPath);
        var installPath = Path.Combine(_gameSettings.InstallPath, relativePath);
        if (File.Exists(installPath))
            return installPath;

        // Fall back to default pack with ambient_games namespace
        if (pack != DefaultPack || ns != DefaultNamespace)
        {
            // Check Documents default
            var defaultDocumentsPath = Path.Combine(
                _gameSettings.GetDocumentsBasePath(),
                packType, DefaultPack, "assets", DefaultNamespace, Path.Combine(subPath));
            if (File.Exists(defaultDocumentsPath))
                return defaultDocumentsPath;

            // Check install default
            var defaultRelativePath = BuildRelativePath(packType, DefaultPack, DefaultNamespace, subPath);
            var defaultInstallPath = Path.Combine(_gameSettings.InstallPath, defaultRelativePath);
            if (File.Exists(defaultInstallPath))
                return defaultInstallPath;
        }

        return null;
    }

    private static string BuildRelativePath(string packType, string pack, string ns, string[] subPath)
    {
        var basePath = Path.Combine("content", packType, pack, "assets", ns);
        return Path.Combine(basePath, Path.Combine(subPath));
    }
}
