using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.Enums;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Domain.Partials;
using Ambient.Infrastructure.GameLogic;
using Ambient.Infrastructure.GameLogic.Loading;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Loading;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Services;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Ambient.Saga.UI.Tests.RealContentE2E;

/// <summary>
/// Runs the real-content E2E tests one at a time: they share the on-disk world content
/// and each opens a real LiteDB file, so parallel runs would only add flake risk.
/// </summary>
[CollectionDefinition("Real Content E2E", DisableParallelization = true)]
public class RealContentE2ECollection
{
}

/// <summary>
/// Headless host that assembles the SAME stack the ImGui game host assembles:
///
/// - REAL world content: the vendored Saga-flavor worlds (Ise/Kagoshima) copied to
///   <c>&lt;output&gt;\content\worlds</c>, loaded through the same <c>LoadWorldQuery</c>
///   the view model sends (which validates every XML file against the real schemas) —
///   NOT synthetic C# content. This is the whole point of these tests: content bugs
///   (e.g. a dialogue action attribute the schema doesn't have) must fail here, not
///   at runtime in a player's conversation.
/// - REAL MediatR pipeline with the full production behavior chain, including
///   QuestStageProgressionBehavior (mirrors Ambient.Archimedea.Schema Program.cs).
/// - REAL LiteDB database file in a temp directory, wired through the REAL
///   WorldRepositoryFactory (which hooks the avatar-progress projection into the
///   saga repository — the cross-arc token seam).
/// - REAL SagaMainViewModel constructed exactly like the game constructs it, with the
///   providers initialized the way SagaMainViewModel.InitializeWorldDatabase does on
///   world load. Only rendering/Steam-adjacent services are stubbed (they cannot run
///   headless): content generator, avatar-creation UI, archetype-selection UI.
/// </summary>
internal sealed class RealContentE2EHost : IDisposable
{
    public string TempDirectory { get; private set; } = string.Empty;
    public string DatabasePath { get; private set; } = string.Empty;

    public ServiceProvider Services { get; private set; } = null!;
    public IMediator Mediator { get; private set; } = null!;
    public IWorld World { get; private set; } = null!;
    public WorldRepositories Repositories { get; private set; } = null!;
    public SagaMainViewModel ViewModel { get; private set; } = null!;

    private LiteDatabase _database = null!;
    private WorldProvider _worldProvider = null!;
    private bool _keepDataOnDispose;

    private RealContentE2EHost()
    {
    }

    /// <summary>
    /// Creates a host over a fresh temp directory, or — when <paramref name="existingTempDirectory"/>
    /// is given — over an existing database file, simulating a full game restart (the
    /// reconstruction-from-transaction-log seam: nothing survives but the LiteDB file).
    /// </summary>
    public static async Task<RealContentE2EHost> CreateAsync(string worldRefName, string? existingTempDirectory = null)
    {
        var host = new RealContentE2EHost();

        host.TempDirectory = existingTempDirectory
            ?? Path.Combine(Path.GetTempPath(), $"AmbientSagaUiE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(host.TempDirectory);
        host.DatabasePath = Path.Combine(host.TempDirectory, "world_state.db");

        host._database = new LiteDatabase(host.DatabasePath);

        // Same settings/resolver family the sandbox host uses; install-location content
        // discovery looks in <executing dir>\content\worlds, where the build copied the
        // vendored Saga-flavor worlds.
        var gameSettings = new GameSettings(PublisherDefaults.PublisherFolder, "Saga");
        var contentPathResolver = new ContentPathResolver(gameSettings);

        var readModelRepository = new InMemorySagaReadModelRepository();

        // Providers are the same mutable singletons the game registers at startup and
        // initializes on world load (see Ambient.Archimedea.Schema Program.cs).
        var worldProvider = new WorldProvider();
        var sagaRepositoryProvider = new SagaInstanceRepositoryProvider();
        var avatarProgressRepositoryProvider = new AvatarProgressRepositoryProvider();
        var gameAvatarRepositoryProvider = new GameAvatarRepositoryProvider();
        var worldStateRepositoryProvider = new WorldStateRepositoryProvider();
        host._worldProvider = worldProvider;

        var services = new ServiceCollection();

        // Full production MediatR pipeline — all four behaviors, including the quest
        // stage auto-advance driver.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
            cfg.AddOpenBehavior(typeof(QuestStageProgressionBehavior<,>));
        });

        // Real world loading stack (LoadWorldQuery / LoadAvailableWorldConfigurationsQuery
        // handlers resolve these — the same query path SagaMainViewModel drives).
        services.AddSingleton<IGameSettings>(gameSettings);
        services.AddSingleton<IContentPathResolver>(contentPathResolver);
        services.AddSingleton<IWorldFactory, TestWorldFactory>();
        services.AddSingleton<IWorldConfigurationLoader>(new WorldConfigurationLoader(gameSettings));
        services.AddSingleton<IGameplayComponentLoader>(new GameplayComponentLoader(contentPathResolver));
        services.AddSingleton<IWorldLoader, WorldAssetLoader>();

        // Provider-backed registrations, exactly like the shipped game: handlers resolve
        // per call so the providers can be initialized after the world loads.
        services.AddSingleton(worldProvider);
        services.AddSingleton(sagaRepositoryProvider);
        services.AddSingleton(avatarProgressRepositoryProvider);
        services.AddSingleton(gameAvatarRepositoryProvider);
        services.AddSingleton(worldStateRepositoryProvider);
        services.AddTransient<IWorld>(_ => worldProvider.World);
        services.AddTransient(_ => sagaRepositoryProvider.Repository);
        services.AddTransient(_ => avatarProgressRepositoryProvider.Repository);
        services.AddTransient(_ => gameAvatarRepositoryProvider.Repository);
        services.AddTransient(_ => worldStateRepositoryProvider.Repository);
        services.AddSingleton<Func<IGameAvatarRepository>>(_ => () => gameAvatarRepositoryProvider.Repository);
        services.AddSingleton<Func<IWorld>>(_ => () => worldProvider.World);
        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();
        services.AddSingleton<ISagaReadModelRepository>(readModelRepository);

        host.Services = services.BuildServiceProvider();
        host.Mediator = host.Services.GetRequiredService<IMediator>();

        // Load the world through the SAME query the view model sends on world selection.
        host.World = await host.Mediator.Send(new LoadWorldQuery
        {
            DataDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Worlds"),
            DefinitionDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "xsd"),
            ConfigurationRefName = worldRefName
        });

        // What SagaMainViewModel.InitializeWorldDatabase does on world load: the REAL
        // repository factory over the (temp-file) LiteDB, which also wires the
        // avatar-progress projection into the saga repository.
        var repositoryFactory = new WorldRepositoryFactory(
            gameSettings, host._database, loggerFactory: null, readModelRepository: readModelRepository);
        host.Repositories = repositoryFactory.CreateRepositories(
            host.World.WorldConfiguration.RefName, host.World, isSteamAvailable: false);

        worldProvider.SetWorld(host.World);
        sagaRepositoryProvider.SetRepository(host.Repositories.SagaRepository);
        avatarProgressRepositoryProvider.SetRepository(host.Repositories.AvatarProgressRepository);
        gameAvatarRepositoryProvider.SetRepository(host.Repositories.AvatarRepository);
        worldStateRepositoryProvider.SetRepository(host.Repositories.WorldStateRepository);

        // The real main view model, constructed the way the existing view-model tests
        // construct it — but with the REAL mediator/providers this time.
        host.ViewModel = new SagaMainViewModel(
            worldProvider,
            sagaRepositoryProvider,
            avatarProgressRepositoryProvider,
            gameAvatarRepositoryProvider,
            worldStateRepositoryProvider,
            repositoryFactory,
            contentPathResolver,
            host.Mediator,
            new StubWorldContentGenerator(),
            new StubAvatarCreationService(),
            new StubArchetypeSelector());

        // World/avatar becoming available is what unlocks the child view models
        // (QuestLog etc.) — same as InitializeWorldCoreAsync setting CurrentWorld.
        host.ViewModel.CurrentWorld = host.World;

        return host;
    }

    /// <summary>
    /// Builds an avatar from a REAL archetype in the loaded world content — the same
    /// spawn path the game's avatar creation uses (AvatarSpawner over the authored
    /// SpawnStats/SpawnCapabilities).
    /// </summary>
    public AvatarEntity CreateAvatarFromWorldArchetype(string archetypeRefName, string displayName)
    {
        var archetype = World.AvatarArchetypesLookup[archetypeRefName];
        var id = Guid.NewGuid();
        var avatar = new AvatarEntity
        {
            Id = id,
            AvatarId = id,
            DisplayName = displayName,
            ArchetypeRef = archetypeRefName
        };
        AvatarSpawner.SpawnFromModelAvatar(avatar, archetype);
        ViewModel.Avatar = avatar;
        return avatar;
    }

    /// <summary>
    /// Simulates a full game restart over the same database file: everything in memory
    /// is discarded; only the LiteDB file survives. Returns a brand-new host.
    /// The current host keeps its files on dispose so the new one can open them.
    /// </summary>
    public async Task<RealContentE2EHost> RestartAsync(string worldRefName)
    {
        var tempDirectory = TempDirectory;
        _keepDataOnDispose = true;
        Dispose();
        return await CreateAsync(worldRefName, tempDirectory);
    }

    public void Dispose()
    {
        Services?.Dispose();
        _database?.Dispose();

        if (!_keepDataOnDispose)
        {
            try
            {
                if (Directory.Exists(TempDirectory))
                {
                    Directory.Delete(TempDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; never fail a test run over a locked handle.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    #region Headless stubs (rendering / Steam-adjacent only)

    private sealed class TestWorldFactory : IWorldFactory
    {
        public IWorld CreateWorld() => new World();
    }

    private sealed class StubWorldContentGenerator : IWorldContentGenerator
    {
        public bool IsAvailable => false;
        public string StatusMessage => "Stub generator";
        public Task<List<string>> GenerateWorldContentAsync(IWorldConfiguration worldConfig, string outputDirectory)
            => Task.FromResult(new List<string>());
    }

    private sealed class StubAvatarCreationService : IAvatarCreationService
    {
        public Task<AvatarEntity> CreateAvatarAsync(Guid avatarId, AvatarArchetype archetype, IWorld world)
            => Task.FromResult(new AvatarEntity { Id = avatarId, AvatarId = avatarId });
    }

    private sealed class StubArchetypeSelector : IArchetypeSelector
    {
        public Task<AvatarArchetype?> SelectArchetypeAsync(IEnumerable<AvatarArchetype> archetypes, string? currencyName)
            => Task.FromResult<AvatarArchetype?>(null);
    }

    #endregion
}
