using Ambient.Application.Contracts;
using Microsoft.Extensions.Logging;

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
}

/// <summary>
/// Resolves content paths with AppData and install location fallback support.
/// </summary>
public class ContentPathResolver : IContentPathResolver
{
    private readonly IGameSettings _gameSettings;
    private readonly ILogger<ContentPathResolver>? _logger;

    private const string DefaultPack = "default";
    private const string DefaultNamespace = "ambient_games";

    public ContentPathResolver(IGameSettings gameSettings, ILogger<ContentPathResolver>? logger = null)
    {
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _logger = logger;
        _logger?.LogInformation("ContentPathResolver initialized with PublisherFolder={Publisher}, GameName={Game}",
            gameSettings.PublisherFolder, gameSettings.GameName);
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
        var rng = random ?? new Random();
        string[] extensions = ["*.litematic", "*.schematic", "*.xml"];

        // 1. Try models/{Category}/{Kind}/ first
        if (!string.IsNullOrEmpty(kind) && kind != "Default")
        {
            var kindPath = ResolveModelDirectoryPath(library, ns, category, kind);
            var modelPath = SelectRandomModelFromDirectory(kindPath, extensions, rng);
            if (modelPath != null)
                return modelPath;
        }

        // 2. Try models/{Category}/
        var categoryPath = ResolveModelDirectoryPath(library, ns, category);
        var categoryModelPath = SelectRandomModelFromDirectory(categoryPath, extensions, rng);
        if (categoryModelPath != null)
            return categoryModelPath;

        // 3. Fall back to models/Default/
        var defaultPath = ResolveModelDirectoryPath(library, ns, "Default");
        return SelectRandomModelFromDirectory(defaultPath, extensions, rng);
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
        var relativePath = BuildRelativePath("packs/libraries", library, ns, subPath);

        // Check %APPDATA% location first
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            _gameSettings.PublisherFolder, _gameSettings.GameName, relativePath);
        if (Directory.Exists(appDataPath))
            return appDataPath;

        // Fall back to install location
        var installPath = Path.Combine(_gameSettings.InstallPath, relativePath);
        if (Directory.Exists(installPath))
            return installPath;

        // Fall back to default pack
        if (library != DefaultPack || ns != DefaultNamespace)
        {
            var defaultRelativePath = BuildRelativePath("packs/libraries", DefaultPack, DefaultNamespace, subPath);

            var defaultAppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                _gameSettings.PublisherFolder, _gameSettings.GameName, defaultRelativePath);
            if (Directory.Exists(defaultAppDataPath))
                return defaultAppDataPath;

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
    /// Resolution order: {worldRef}_generated -> world -> library -> default library
    /// </summary>
    public string? ResolveXmlPath(string worldRef, string library, string ns, params string[] relativePath)
    {
        var xmlSubPath = new[] { "xml" }.Concat(relativePath).ToArray();

        // 1. Check generated world folder first (highest priority - procedurally generated content)
        var generatedWorldRef = worldRef.ToLowerInvariant() + "_generated";
        var generatedPath = ResolveWorldPath(generatedWorldRef, ns, xmlSubPath);
        if (generatedPath != null)
            return generatedPath;

        // 2. Check world-specific location
        var worldPath = ResolveWorldPath(worldRef, ns, xmlSubPath);
        if (worldPath != null)
            return worldPath;

        // 3. Check library location
        var libraryPath = ResolvePath("packs/libraries", library, ns, xmlSubPath);
        if (libraryPath != null)
            return libraryPath;

        // 4. Fall back to default library (if not already checked)
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
        var relativePath = Path.Combine("content", "worlds", worldRef, "assets", ns, Path.Combine(subPath));

        // Check %APPDATA% location first
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            _gameSettings.PublisherFolder, _gameSettings.GameName, relativePath);
        if (File.Exists(appDataPath))
            return appDataPath;

        // Fall back to install location
        var installPath = Path.Combine(_gameSettings.InstallPath, relativePath);
        if (File.Exists(installPath))
            return installPath;

        return null;
    }

    private string? ResolvePath(string packType, string pack, string ns, params string[] subPath)
    {
        var relativePath = BuildRelativePath(packType, pack, ns, subPath);

        // Check %APPDATA% location first
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            _gameSettings.PublisherFolder, _gameSettings.GameName, relativePath);
        if (File.Exists(appDataPath))
            return appDataPath;

        // Fall back to install location
        var installPath = Path.Combine(_gameSettings.InstallPath, relativePath);
        if (File.Exists(installPath))
            return installPath;

        // Fall back to default pack with ambient_games namespace
        if (pack != DefaultPack || ns != DefaultNamespace)
        {
            var defaultRelativePath = BuildRelativePath(packType, DefaultPack, DefaultNamespace, subPath);

            var defaultAppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                _gameSettings.PublisherFolder, _gameSettings.GameName, defaultRelativePath);
            if (File.Exists(defaultAppDataPath))
                return defaultAppDataPath;

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
