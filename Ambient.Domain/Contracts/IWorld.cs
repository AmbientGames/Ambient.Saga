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
    /// Set by the application to provide block lookup functionality.
    /// </summary>
    IBlockProvider? BlockProvider { get; set; }

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
    Dictionary<string, CharacterAffinity> CharacterAffinitiesLookup { get; set; }
    Dictionary<string, CombatStance> CombatStancesLookup { get; set; }
    Dictionary<string, LoadoutSlot> LoadoutSlotsLookup { get; set; }
    System.Collections.Concurrent.ConcurrentDictionary<string, SagaArc> SagaArcLookup { get; set; }
    System.Collections.Concurrent.ConcurrentDictionary<string, List<SagaTrigger>> SagaTriggersLookup { get; set; }
    Dictionary<string, Faction> FactionsLookup { get; set; }
    Dictionary<string, StatusEffect> StatusEffectsLookup { get; set; }
    Dictionary<string, AttackTell> AttackTellsLookup { get; set; }
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

    public Quest GetQuestByRefName(string questRefName);
    public Quest? TryGetQuestByRefName(string questRefName);

    public CharacterAffinity GetCharacterAffinityByRefName(string characterAffinityRefName);
    public CharacterAffinity? TryGetCharacterAffinityByRefName(string characterAffinityRefName);

    public CombatStance GetCombatStanceByRefName(string combatStanceRefName);
    public CombatStance? TryGetCombatStanceByRefName(string combatStanceRefName);

    public LoadoutSlot GetLoadoutSlotByRefName(string loadoutSlotRefName);
    public LoadoutSlot? TryGetLoadoutSlotByRefName(string loadoutSlotRefName);

    public Faction GetFactionByRefName(string factionRefName);
    public Faction? TryGetFactionByRefName(string factionRefName);

    /// <summary>
    /// Resolves an item ref to its catalog entry across every tradeable family (equipment,
    /// consumables, tools, spells, building materials, blocks). The single place a ref becomes an
    /// item, so every caller resolves the same way regardless of type.
    /// </summary>
    ITradeable? TryGetTradeableByRefName(string refName)
    {
        if (EquipmentLookup.TryGetValue(refName, out var equipment)) return equipment;
        if (ConsumablesLookup.TryGetValue(refName, out var consumable)) return consumable;
        if (ToolsLookup.TryGetValue(refName, out var tool)) return tool;
        if (SpellsLookup.TryGetValue(refName, out var spell)) return spell;
        if (BuildingMaterialsLookup.TryGetValue(refName, out var material)) return material;
        return BlockProvider?.GetBlockByRefName(refName);
    }

    /// <summary>
    /// The display name for any inventory ref, uniformly: unfolds a variant (if any), resolves the
    /// item, and asks it for that variant's name. Blocks, weapons, potions all go through here —
    /// there is no per-type name lookup and no special case for blocks.
    /// </summary>
    string? GetItemDisplayName(string itemRef)
    {
        var (baseRef, variant) = ItemRefManager.Split(itemRef);
        var item = TryGetTradeableByRefName(baseRef);
        if (item == null) return null;

        // Compose the variant label with the base name in the one place that decodes the ref
        // (e.g. "Blue" + "Wool"). Items with no variant labels fall straight through to DisplayName.
        var labels = item.VariantNames;
        if (variant > 0 && labels != null && variant < labels.Count && !string.IsNullOrEmpty(labels[variant]))
            return $"{labels[variant]} {item.DisplayName}";
        return item.DisplayName;
    }

    public StatusEffect GetStatusEffectByRefName(string statusEffectRefName);
    public StatusEffect? TryGetStatusEffectByRefName(string statusEffectRefName);

    public AttackTell GetAttackTellByRefName(string attackTellRefName);
    public AttackTell? TryGetAttackTellByRefName(string attackTellRefName);

    /// <summary>
    /// Registers a saga arc into the runtime dictionaries so it is processed
    /// identically to XML-defined arcs. Used by consumers to inject server-sourced
    /// arcs (e.g. avatar shopkeepers) at runtime.
    /// Idempotent: no-op if the arc's RefName already exists. Throws on invalid input
    /// (null arc, empty RefName, or triggers missing spawns).
    /// </summary>
    void RegisterSagaArc(SagaArc arc);
}