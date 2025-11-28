using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.GameLogic;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Contracts.Cqrs;

namespace Ambient.SagaEngine.Contracts;

/// <summary>
/// Factory for creating repository instances for a loaded world.
/// Abstracts Infrastructure layer repository creation from Presentation layer.
/// </summary>
public interface IWorldRepositoryFactory
{
    /// <summary>
    /// Creates all repository instances for the specified world.
    /// </summary>
    /// <param name="gameName">Game name for database path</param>
    /// <param name="worldRefName">World configuration RefName (database name)</param>
    /// <param name="world">Loaded world instance</param>
    /// <param name="isSteamAvailable">Whether Steam is available for achievement sync</param>
    /// <returns>Repository instances and database</returns>
    WorldRepositories CreateRepositories(string gameName, string worldRefName, World world, bool isSteamAvailable);
}

/// <summary>
/// Container for all repository instances created by the factory.
/// </summary>
public record WorldRepositories
{
    public required ISagaInstanceRepository SagaRepository { get; init; }
    public required IGameAvatarRepository AvatarRepository { get; init; }
    public required IWorldStateRepository WorldStateRepository { get; init; }
    public required ISteamAchievementService SteamAchievementService { get; init; }
    public required IDisposable Database { get; init; }
}
