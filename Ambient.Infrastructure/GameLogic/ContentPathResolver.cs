using Ambient.Application.Contracts;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Ambient.Infrastructure.GameLogic;

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
        _logger?.LogInformation("ContentPathResolver initialized with PublisherFolder={Publisher}, ProductRef={Product}",
            gameSettings.PublisherFolder, gameSettings.ProductRef);
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
    /// Every model matching a Category/Kind, by name, sorted. Same directory search as
    /// the single-pick version — Kind, then Category, then Default — but it returns the
    /// whole set rather than choosing from it, so generation can record what a world may
    /// build and runtime never has to look.
    /// </summary>
    public IReadOnlyList<string> EnumerateModelRefsByCategoryKind(string library, string ns, string category, string? kind)
    {
        string[] extensions = ["*.litematic", "*.schematic", "*.xml"];

        // "Default" is a real Kind directory — models/Waypoint/Default holds models, while
        // models/Waypoint holds only the Kind folders. Treating the name as "no kind" and
        // skipping it looks one level too high and finds nothing.
        var directory =
            (!string.IsNullOrEmpty(kind) ? ResolveModelDirectoryPath(library, ns, category, kind) : null)
            ?? ResolveModelDirectoryPath(library, ns, category)
            ?? ResolveModelDirectoryPath(library, ns, "Default");

        if (directory == null || !Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return extensions
            .SelectMany(p => Directory.GetFiles(directory, p))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// One model name for a Category/Kind, chosen from the SAME enumeration generation
    /// records, so the pick and the recorded set can never disagree.
    ///
    /// Deliberately not routed through <see cref="ResolveModelPathByCategoryKind"/>: that
    /// caches its answer per library|ns|category|kind, which would hand every caller of a
    /// Category the first caller's model no matter what <paramref name="random"/> said —
    /// making a seeded, per-arc pick silently uniform.
    /// </summary>
    public string? ResolveModelRefByCategoryKind(string library, string ns, string category, string? kind, Random? random = null)
    {
        var models = EnumerateModelRefsByCategoryKind(library, ns, category, kind);
        if (models.Count == 0)
        {
            return null;
        }

        return models[(random ?? new Random()).Next(models.Count)];
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
