using Ambient.Domain.ValueObjects;
using System.Xml.Serialization;

namespace Ambient.Domain.Contracts;

/// <summary>
/// Defines the contract for world properties.
/// </summary>
public interface IWorld
{
    /// <summary>
    /// Array of available world configurations for this world.
    /// </summary>
    IWorldConfiguration[] AvailableWorldConfigurations { get; set; }

    IWorldConfiguration WorldConfiguration { get; set; }

    /// <summary>
    /// Optional block provider for games that include block/voxel systems.
    /// Returns null by default - implemented by game-specific domain projects.
    /// </summary>
    IBlockProvider? BlockProvider => null;

    bool IsProcedural { get; set; }
    double VerticalShift { get; set; }
    double VerticalScale { get; set; }
    int HeightMapSpawnPixelX { get; set; }
    GeoTiffMetadata HeightMapMetadata { get; set; }
    double HeightMapLatitudeScale { get; set; }
    int BlocksBeneathSeaLevel { get; set; }
    int HeightMapSpawnPixelY { get; set; }
    double HeightMapLongitudeScale { get; set; }

    Dictionary<string, Tool> ToolsLookup { get; set; }
    Dictionary<string, BuildingMaterial> BuildingMaterialsLookup { get; set; }
    Dictionary<string, Consumable> ConsumablesLookup { get; set; }
    Dictionary<string, Spell> SpellsLookup { get; set; }
    Dictionary<string, Character> CharactersLookup { get; set; }
    Dictionary<string, Equipment> EquipmentLookup { get; set; }
    Dictionary<string, QuestToken> QuestTokensLookup { get; set; }
    Dictionary<string, Achievement> AchievementsLookup { get; set; }
    Dictionary<string, Quest> QuestsLookup { get; set; }
    Dictionary<string, AvatarArchetype> AvatarArchetypesLookup { get; set; }
    Dictionary<string, DialogueTree> DialogueTreesLookup { get; set; }
    Dictionary<string, CharacterArchetype> CharacterArchetypesLookup { get; set; }
    Dictionary<string, CharacterAffinity> CharacterAffinitiesLookup { get; set; }
    Dictionary<string, CombatStance> CombatStancesLookup { get; set; }
    Dictionary<string, LoadoutSlot> LoadoutSlotsLookup { get; set; }
    Dictionary<string, SagaTriggerPattern> SagaTriggerPatternsLookup { get; set; }
    Dictionary<string, SagaFeature> SagaFeaturesLookup { get; set; }
    Dictionary<string, SagaArc> SagaArcLookup { get; set; }
    Dictionary<string, List<SagaTrigger>> SagaTriggersLookup { get; set; }
    Dictionary<string, Faction> FactionsLookup { get; set; }
    Dictionary<string, StatusEffect> StatusEffectsLookup { get; set; }
    GameplayComponents Gameplay { get; }
    long UtcStartTick { get; set; }
    IWorldTemplate WorldTemplate { get; set; }

    // remove these - the usage is an abomination:
    public Tool GetToolByRefName(string toolRefName);
    public Tool? TryGetToolByRefName(string toolRefName);

    public BuildingMaterial GetBuildingMaterialByRefName(string buildingMaterialRefName);
    public BuildingMaterial? TryGetBuildingMaterialByRefName(string buildingMaterialRefName);

    public Consumable GetConsumableByRefName(string consumableRefName);
    public Consumable? TryGetConsumableByRefName(string consumableRefName);

    public Equipment GetEquipmentByRefName(string equipmentRefName);
    public Equipment? TryGetEquipmentByRefName(string equipmentRefName);

    public Spell GetSpellByRefName(string spellRefName);
    public Spell? TryGetSpellByRefName(string spellRefName);

    public Character GetCharacterByRefName(string characterRefName);
    public Character? TryGetCharacterByRefName(string characterRefName);

    public SagaArc GetSagaArcByRefName(string sagaArcRefName);
    public SagaArc? TryGetSagaArcByRefName(string sagaArcRefName);

    public QuestToken GetQuestTokenByRefName(string QuestTokenRefName);
    public QuestToken? TryGetQuestTokenByRefName(string QuestTokenRefName);

    public Achievement GetAchievementByRefName(string achievementRefName);
    public Achievement? TryGetAchievementByRefName(string achievementRefName);

    public SagaFeature GetSagaFeatureByRefName(string featureRefName);
    public SagaFeature? TryGetSagaFeatureByRefName(string featureRefName);
    public SagaFeature? TryGetSagaFeatureByRefNameAndType(string featureRefName, SagaFeatureType type);

    public Quest GetQuestByRefName(string questRefName);
    public Quest? TryGetQuestByRefName(string questRefName);

    public SagaTriggerPattern GetTriggerPatternByRefName(string sagaTriggerPatternRefName);
    public SagaTriggerPattern? TryGetTriggerPatternByRefName(string sagaTriggerPatternRefName);

    public CharacterAffinity GetCharacterAffinityByRefName(string characterAffinityRefName);
    public CharacterAffinity? TryGetCharacterAffinityByRefName(string characterAffinityRefName);

    public CombatStance GetCombatStanceByRefName(string combatStanceRefName);
    public CombatStance? TryGetCombatStanceByRefName(string combatStanceRefName);

    public LoadoutSlot GetLoadoutSlotByRefName(string loadoutSlotRefName);
    public LoadoutSlot? TryGetLoadoutSlotByRefName(string loadoutSlotRefName);

    public Faction GetFactionByRefName(string factionRefName);
    public Faction? TryGetFactionByRefName(string factionRefName);

    public StatusEffect GetStatusEffectByRefName(string statusEffectRefName);
    public StatusEffect? TryGetStatusEffectByRefName(string statusEffectRefName);
}