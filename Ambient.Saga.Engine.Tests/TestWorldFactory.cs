using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Enums;
using Ambient.Domain.Partials;
using Ambient.Infrastructure.GameLogic;
using Ambient.Infrastructure.GameLogic.Loading;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// IWorldFactory implementation for tests that load worlds via WorldAssetLoader.
/// Also provides helper methods for creating minimal test World instances.
/// </summary>
public class TestWorldFactory : IWorldFactory
{
    public IWorld CreateWorld() => new World();

    /// <summary>
    /// Creates test game settings with default values for testing.
    /// </summary>
    public static IGameSettings CreateTestGameSettings() =>
        new GameSettings(PublisherDefaults.PublisherFolder, "Saga");

    /// <summary>
    /// Creates a content path resolver for tests.
    /// </summary>
    public static IContentPathResolver CreateTestContentPathResolver() =>
        new ContentPathResolver(CreateTestGameSettings());

    /// <summary>
    /// Creates a world configuration loader for tests.
    /// </summary>
    public static WorldConfigurationLoader CreateTestWorldConfigurationLoader() =>
        new WorldConfigurationLoader(CreateTestGameSettings());

    /// <summary>
    /// Creates a gameplay component loader for tests.
    /// </summary>
    public static GameplayComponentLoader CreateTestGameplayComponentLoader() =>
        new GameplayComponentLoader(CreateTestContentPathResolver());

    /// <summary>
    /// Creates a world asset loader for tests.
    /// </summary>
    public static WorldAssetLoader CreateTestWorldAssetLoader() =>
        new WorldAssetLoader(
            new TestWorldFactory(),
            CreateTestWorldConfigurationLoader(),
            CreateTestGameplayComponentLoader(),
            CreateTestContentPathResolver());

    /// <summary>
    /// Creates a minimal valid world for testing.
    /// Note: World.Simulation/Presentation/Gameplay are read-only properties that delegate to WorldTemplate.
    /// </summary>
    public static World CreateMinimalWorld()
    {
        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Characters = Array.Empty<Character>(),
                    DialogueTrees = Array.Empty<DialogueTree>(),
                    Equipment = Array.Empty<Equipment>(),
                    Consumables = Array.Empty<Consumable>(),
                    QuestTokens = Array.Empty<QuestToken>(),
                    Factions = Array.Empty<Faction>(),
                    SagaArcs = Array.Empty<SagaArc>()
                }
            }
        };

        // Initialize lookups
        world.CharactersLookup = new Dictionary<string, Character>();
        world.DialogueTreesLookup = new Dictionary<string, DialogueTree>();
        world.EquipmentLookup = new Dictionary<string, Equipment>();
        world.ConsumablesLookup = new Dictionary<string, Consumable>();
        world.QuestTokensLookup = new Dictionary<string, QuestToken>();
        // NOTE: BuildingMaterials/Tools are now in IGameplayItemProvider in Core, not Saga
        world.SpellsLookup = new Dictionary<string, Spell>();
        world.AchievementsLookup = new Dictionary<string, Achievement>();
        world.QuestsLookup = new Dictionary<string, Quest>();
        world.CharacterAffinitiesLookup = new Dictionary<string, CharacterAffinity>();
        world.SagaArcLookup = new Dictionary<string, SagaArc>();
        world.FactionsLookup = new Dictionary<string, Faction>();

        return world;
    }

    /// <summary>
    /// Creates a test avatar with default empty inventory and specified credits.
    /// </summary>
    public static AvatarBase CreateTestAvatar(float credits = 1000f)
    {
        var avatar = new AvatarBase();
        avatar.Stats = new CharacterStats();
        avatar.Stats.Credits = credits;
        avatar.Stats.Health = 100;
        avatar.Capabilities = new ItemCollection();
        avatar.Capabilities.Equipment = Array.Empty<EquipmentEntry>();
        avatar.Capabilities.Consumables = Array.Empty<ConsumableEntry>();
        // NOTE: Blocks/Tools removed from ItemCollection - now in IGameplayItemProvider in Core
        avatar.Capabilities.Spells = Array.Empty<SpellEntry>();
        return avatar;
    }
}
