namespace Ambient.Application.WorldCreation;

/// <summary>
/// Service for creating new worlds.
/// Orchestrates the creation of all required files and directories.
/// </summary>
public interface IWorldCreationService
{
    /// <summary>
    /// Create a new world with the given parameters.
    /// </summary>
    /// <param name="parameters">World creation parameters</param>
    /// <param name="worldsBasePath">Base path for worlds (e.g., Documents/{Publisher}/{GameName}/worlds)</param>
    /// <param name="progress">Optional progress callback</param>
    /// <returns>Result containing output path or error</returns>
    Task<WorldCreationResult> CreateWorldAsync(
        WorldCreationParameters parameters,
        string worldsBasePath,
        Action<string>? progress = null);
}

/// <summary>
/// Result of world creation operation.
/// </summary>
public class WorldCreationResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }

    public static WorldCreationResult Succeeded(string outputPath) =>
        new() { Success = true, OutputPath = outputPath };

    public static WorldCreationResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
