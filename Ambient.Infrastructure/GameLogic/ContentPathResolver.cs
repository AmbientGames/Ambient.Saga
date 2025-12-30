using Ambient.Application.Utilities;

namespace Ambient.Infrastructure.GameLogic;

public static class ContentPathResolver
{
    private const string AppDataFolder = "AmbientGames";
    private const string GameFolder = "Schema";
    private const string DefaultPack = "default";
    private const string DefaultNamespace = "ambient_games";

    public static string? ResolveTexturePath(string resourcePack, string ns, string textureName)
    {
        return ResolvePath("resourcepacks", resourcePack, ns, "textures", "block", textureName + ".png");
    }

    public static string? ResolveGeographicDataPath(string contentPack, string ns, string fileName)
    {
        return ResolvePath("contentpacks", contentPack, ns, "geographic_data", fileName);
    }

    /// <summary>
    /// Resolves XML content path with world-specific override support.
    /// Resolution order: world -> resourcepack -> default resourcepack
    /// </summary>
    public static string? ResolveXmlPath(string worldRef, string resourcePack, string ns, params string[] relativePath)
    {
        var xmlSubPath = new[] { "xml" }.Concat(relativePath).ToArray();

        // 1. Check world-specific location first (highest priority)
        var worldPath = ResolveWorldPath(worldRef, ns, xmlSubPath);
        if (worldPath != null)
            return worldPath;

        // 2. Check resourcepack location
        var resourcePackPath = ResolvePath("resourcepacks", resourcePack, ns, xmlSubPath);
        if (resourcePackPath != null)
            return resourcePackPath;

        // 3. Fall back to default resourcepack (if not already checked)
        if (resourcePack != DefaultPack || ns != DefaultNamespace)
        {
            var defaultPath = ResolvePath("resourcepacks", DefaultPack, DefaultNamespace, xmlSubPath);
            if (defaultPath != null)
                return defaultPath;
        }

        return null;
    }

    private static string? ResolveWorldPath(string worldRef, string ns, string[] subPath)
    {
        var relativePath = Path.Combine("content", "worlds", worldRef, "assets", ns, Path.Combine(subPath));

        // Check %APPDATA% location first
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataFolder, GameFolder, relativePath);
        if (File.Exists(appDataPath))
            return appDataPath;

        // Fall back to install location
        var installPath = Path.Combine(FileManager.GetExecutingDirectoryName(), relativePath);
        if (File.Exists(installPath))
            return installPath;

        return null;
    }

    private static string? ResolvePath(string packType, string pack, string ns, params string[] subPath)
    {
        var relativePath = BuildRelativePath(packType, pack, ns, subPath);

        // Check %APPDATA% location first
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataFolder, GameFolder, relativePath);
        if (File.Exists(appDataPath))
            return appDataPath;

        // Fall back to install location
        var installPath = Path.Combine(FileManager.GetExecutingDirectoryName(), relativePath);
        if (File.Exists(installPath))
            return installPath;

        // Fall back to default pack with ambient_games namespace
        if (pack != DefaultPack || ns != DefaultNamespace)
        {
            var defaultRelativePath = BuildRelativePath(packType, DefaultPack, DefaultNamespace, subPath);

            var defaultAppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataFolder, GameFolder, defaultRelativePath);
            if (File.Exists(defaultAppDataPath))
                return defaultAppDataPath;

            var defaultInstallPath = Path.Combine(FileManager.GetExecutingDirectoryName(), defaultRelativePath);
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
