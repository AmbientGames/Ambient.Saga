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
