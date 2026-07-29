using Ambient.Application.Contracts;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Contracts;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace Ambient.Rpg.Engine.Infrastructure.Persistence;

/// <summary>
/// Factory for creating repository instances for a loaded world.
/// Encapsulates Infrastructure layer repository instantiation.
/// </summary>
public class WorldRepositoryFactory : IWorldRepositoryFactory
{
    private readonly IGameSettings _gameSettings;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly LiteDatabase? _sharedDatabase;
    private readonly IArcReadModelRepository? _readModelRepository;

    /// <summary>
    /// Default constructor - creates its own database (for Sandbox/standalone usage).
    /// </summary>
    public WorldRepositoryFactory(IGameSettings gameSettings, ILoggerFactory? loggerFactory = null, IArcReadModelRepository? readModelRepository = null)
    {
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _loggerFactory = loggerFactory;
        _sharedDatabase = null;
        _readModelRepository = readModelRepository;
    }

    /// <summary>
    /// Constructor with shared database injection (for Carbon/integrated usage).
    /// </summary>
    public WorldRepositoryFactory(IGameSettings gameSettings, LiteDatabase sharedDatabase, ILoggerFactory? loggerFactory = null, IArcReadModelRepository? readModelRepository = null)
    {
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _sharedDatabase = sharedDatabase;
        _loggerFactory = loggerFactory;
        _readModelRepository = readModelRepository;
    }

    /// <summary>
    /// Creates all repository instances for the specified world.
    /// Uses shared database if provided via constructor, otherwise creates its own.
    /// </summary>
    public WorldRepositories CreateRepositories(string worldRefName, IWorld world, bool isSteamAvailable)
    {
        // Use shared database if provided (Arc), otherwise create new one (Sandbox)
        WorldStateDatabase? ownedDatabase = null;
        LiteDatabase database;

        if (_sharedDatabase != null)
        {
            database = _sharedDatabase;
        }
        else
        {
            // Sandbox path: create own database
            var logger = _loggerFactory?.CreateLogger<WorldStateDatabase>();
            ownedDatabase = new WorldStateDatabase(_gameSettings, worldRefName, logger);
            database = ownedDatabase.Database;
        }

        // Create repository implementations
        var arcRepository = new ArcInstanceRepository(database);
        var avatarRepository = new GameAvatarRepository(database);
        var avatarProgressRepository = new AvatarProgressRepository(database);
        var discoveryRepository = new AvatarDiscoveryRepository(database);

        arcRepository.SetAvatarProgressRepository(avatarProgressRepository);
        if (_readModelRepository != null)
        {
            arcRepository.SetReadModelRepository(_readModelRepository);
        }

        // Create WorldStateRepository with injected dependencies.
        // No AchievementInstance collection: the avatar's persisted Achievements
        // list is the single unlock ledger (audit C2).
        var worldStateRepository = new WorldStateRepository(
            arcRepository,
            avatarRepository,
            discoveryRepository);

        // Create Steam achievement service against the database actually in use
        // (shared or owned) — passing only the owned one left it with a null
        // database on the shared path, NRE'ing on the first unlock.
        var steamAchievementService = new SteamAchievementService(database, isSteamAvailable);

        return new WorldRepositories
        {
            ArcRepository = arcRepository,
            AvatarRepository = avatarRepository,
            AvatarProgressRepository = avatarProgressRepository,
            WorldStateRepository = worldStateRepository,
            SteamAchievementService = steamAchievementService,
            Database = ownedDatabase // Will be null for Arc (using shared), non-null for Sandbox
        };
    }
}
