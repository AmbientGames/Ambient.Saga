using Ambient.Application.Utilities;

namespace Ambient.Application.Contracts;

/// <summary>
/// Provides game-specific configuration settings.
/// Register as singleton in DI container at application startup.
/// </summary>
public interface IGameSettings
{
    /// <summary>
    /// The publisher folder name used in AppData paths (e.g., "AmbientGames").
    /// </summary>
    string PublisherFolder { get; }

    /// <summary>
    /// The game name used for save directories and identification (e.g., "Saga").
    /// </summary>
    string GameName { get; }

    /// <summary>
    /// The base install path where content is located (e.g., the application directory).
    /// For production, this is typically the executing directory.
    /// For tests, this can be overridden to point to a shared content location.
    /// </summary>
    string InstallPath { get; }

    /// <summary>
    /// Gets the base AppData path for this game.
    /// Returns: %APPDATA%/{PublisherFolder}/{GameName}
    /// </summary>
    string GetAppDataBasePath();

    /// <summary>
    /// Gets the content path within AppData for this game.
    /// Returns: %APPDATA%/{PublisherFolder}/{GameName}/content
    /// </summary>
    string GetAppDataContentPath();

    /// <summary>
    /// Gets the base Documents path for user-facing content.
    /// Returns: Documents/{PublisherFolder}/{GameName}
    /// </summary>
    string GetDocumentsBasePath();

    /// <summary>
    /// Gets the worlds path within Documents.
    /// Returns: Documents/{PublisherFolder}/{GameName}/worlds
    /// </summary>
    string GetWorldsPath();

    /// <summary>
    /// Gets the LocalAppData path for app-internal data (logs, saves, cache).
    /// Returns: %LOCALAPPDATA%/{PublisherFolder}/{GameName}
    /// </summary>
    string GetLocalAppDataPath();

    /// <summary>
    /// Gets the logs path.
    /// Returns: %LOCALAPPDATA%/{PublisherFolder}/{GameName}/logs
    /// </summary>
    string GetLogsPath();

    /// <summary>
    /// Gets the saves path.
    /// Returns: %LOCALAPPDATA%/{PublisherFolder}/{GameName}/saves
    /// </summary>
    string GetSavesPath();
}

/// <summary>
/// Default implementation of IGameSettings.
/// </summary>
public class GameSettings : IGameSettings
{
    public GameSettings(string publisherFolder, string gameName, string? installPath = null)
    {
        PublisherFolder = publisherFolder ?? throw new ArgumentNullException(nameof(publisherFolder));
        GameName = gameName ?? throw new ArgumentNullException(nameof(gameName));
        InstallPath = installPath ?? FileManager.GetExecutingDirectoryName();
    }

    public string PublisherFolder { get; }
    public string GameName { get; }
    public string InstallPath { get; }

    public string GetAppDataBasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            PublisherFolder,
            GameName);
    }

    public string GetAppDataContentPath()
    {
        return Path.Combine(GetAppDataBasePath(), "content");
    }

    public string GetDocumentsBasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            PublisherFolder,
            GameName);
    }

    public string GetWorldsPath()
    {
        return Path.Combine(GetDocumentsBasePath(), "worlds");
    }

    public string GetLocalAppDataPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PublisherFolder,
            GameName);
    }

    public string GetLogsPath()
    {
        return Path.Combine(GetLocalAppDataPath(), "logs");
    }

    public string GetSavesPath()
    {
        return Path.Combine(GetLocalAppDataPath(), "saves");
    }
}
