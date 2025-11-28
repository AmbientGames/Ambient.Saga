using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Contracts;
using Ambient.SagaEngine.Domain.Achievements;

namespace Ambient.SagaEngine.Infrastructure.Persistence;

/// <summary>
/// Factory for creating repository instances for a loaded world.
/// Encapsulates Infrastructure layer repository instantiation.
/// </summary>
public class WorldRepositoryFactory : IWorldRepositoryFactory
{
    /// <summary>
    /// Creates all repository instances for the specified world.
    /// </summary>
    public WorldRepositories CreateRepositories(string gameName, string worldRefName, World world, bool isSteamAvailable)
    {
        // Create database for this world (uses world config RefName as database name)
        var database = new WorldStateDatabase(gameName, worldRefName);

        // Create repository implementations
        var sagaRepository = new SagaInstanceRepository(database.Database);
        var avatarRepository = new GameAvatarRepository(database.Database);
        var achievementRepository = new LiteDbRepository<AchievementInstance>(database.Database, "Achievements");
        var discoveryRepository = new PlayerDiscoveryRepository(database.Database);

        // Create WorldStateRepository with injected dependencies
        var worldStateRepository = new WorldStateRepository(
            sagaRepository,
            avatarRepository,
            achievementRepository,
            discoveryRepository,
            world);

        // Create Steam achievement service
        var steamAchievementService = new SteamAchievementService(database, isSteamAvailable);

        return new WorldRepositories
        {
            SagaRepository = sagaRepository,
            AvatarRepository = avatarRepository,
            WorldStateRepository = worldStateRepository,
            SteamAchievementService = steamAchievementService,
            Database = database
        };
    }
}
