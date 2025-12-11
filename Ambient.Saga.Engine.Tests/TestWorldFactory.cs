using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// IWorldFactory implementation for tests that load worlds via WorldAssetLoader.
/// Also provides helper methods for creating minimal test World instances.
/// </summary>
public class TestWorldFactory : IWorldFactory
{
    public IWorld CreateWorld() => new World();

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
        world.BuildingMaterialsLookup = new Dictionary<string, BuildingMaterial>();
        world.ToolsLookup = new Dictionary<string, Tool>();
        world.SpellsLookup = new Dictionary<string, Spell>();
        world.AchievementsLookup = new Dictionary<string, Achievement>();
        world.QuestsLookup = new Dictionary<string, Quest>();
        world.CharacterAffinitiesLookup = new Dictionary<string, CharacterAffinity>();
        world.CharacterArchetypesLookup = new Dictionary<string, CharacterArchetype>();
        world.SagaFeaturesLookup = new Dictionary<string, SagaFeature>();
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
        avatar.Capabilities.Blocks = Array.Empty<BlockEntry>();
        avatar.Capabilities.Tools = Array.Empty<ToolEntry>();
        avatar.Capabilities.Spells = Array.Empty<SpellEntry>();
        return avatar;
    }
}
