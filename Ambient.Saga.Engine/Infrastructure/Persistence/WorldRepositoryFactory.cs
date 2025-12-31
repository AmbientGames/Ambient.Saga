using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Domain.Achievements;
using LiteDB;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// Factory for creating repository instances for a loaded world.
/// Encapsulates Infrastructure layer repository instantiation.
/// </summary>
public class WorldRepositoryFactory : IWorldRepositoryFactory
{
    private readonly LiteDatabase? _sharedDatabase;

    /// <summary>
    /// Default constructor - creates its own database (for Sandbox/standalone usage).
    /// </summary>
    public WorldRepositoryFactory()
    {
        _sharedDatabase = null;
    }

    /// <summary>
    /// Constructor with shared database injection (for Carbon/integrated usage).
    /// </summary>
    public WorldRepositoryFactory(LiteDatabase sharedDatabase)
    {
        _sharedDatabase = sharedDatabase;
    }

    /// <summary>
    /// Creates all repository instances for the specified world.
    /// Uses shared database if provided via constructor, otherwise creates its own.
    /// </summary>
    public WorldRepositories CreateRepositories(string gameName, string worldRefName, IWorld world, bool isSteamAvailable)
    {
        // Use shared database if provided (Saga), otherwise create new one (Sandbox)
        WorldStateDatabase? ownedDatabase = null;
        LiteDatabase database;

        if (_sharedDatabase != null)
        {
            database = _sharedDatabase;
        }
        else
        {
            // Sandbox path: create own database
            ownedDatabase = new WorldStateDatabase(gameName, worldRefName);
            database = ownedDatabase.Database;
        }

        // Create repository implementations
        var sagaRepository = new SagaInstanceRepository(database);
        var avatarRepository = new GameAvatarRepository(database);
        var achievementRepository = new LiteDbRepository<AchievementInstance>(database, "Achievements");
        var discoveryRepository = new PlayerDiscoveryRepository(database);

        // Create WorldStateRepository with injected dependencies
        var worldStateRepository = new WorldStateRepository(
            sagaRepository,
            avatarRepository,
            achievementRepository,
            discoveryRepository,
            world);

        // Create Steam achievement service (use owned database if we created one, otherwise null)
        var steamAchievementService = new SteamAchievementService(ownedDatabase, isSteamAvailable);

        return new WorldRepositories
        {
            SagaRepository = sagaRepository,
            AvatarRepository = avatarRepository,
            WorldStateRepository = worldStateRepository,
            SteamAchievementService = steamAchievementService,
            Database = ownedDatabase // Will be null for Saga (using shared), non-null for Sandbox
        };
    }
}
