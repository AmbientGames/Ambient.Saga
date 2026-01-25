using Ambient.Domain.Contracts;
using Ambient.Domain.ValueObjects;
using System.Xml.Serialization;

namespace Ambient.Domain.Partials;

/// <summary>
/// Main world entity combining schema properties with extensive lookup dictionaries, time management, and catalog lookup methods for all world elements.
/// </summary>
public partial class World : IWorld
{
    /// <summary>
    /// Optional block provider for games that include block/voxel systems.
    /// Set by the application to provide block lookup functionality.
    /// </summary>
    [XmlIgnore]
    public IBlockProvider? BlockProvider { get; set; }
    /// <summary>
    /// World identifier property.
    /// </summary>
    public Guid Id { get; set; }
    /// <summary>
    /// UTC start time in ticks for world time calculations.
    /// </summary>
    public long UtcStartTick { get; set; }

    [XmlIgnore] public IWorldConfiguration WorldConfiguration { get; set; }
    [XmlIgnore] public IWorldConfiguration[] AvailableWorldConfigurations { get; set; }
    [XmlIgnore] public IWorldTemplate WorldTemplate { get; set; }
    [XmlIgnore] public bool IsProcedural { get; set; } = true;
    [XmlIgnore] public GameplayComponents Gameplay => WorldTemplate.Gameplay;

    [XmlIgnore] public Dictionary<string, Tool> ToolsLookup { get; set; } = new Dictionary<string, Tool>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, BuildingMaterial> BuildingMaterialsLookup { get; set; } = new Dictionary<string, BuildingMaterial>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, Consumable> ConsumablesLookup { get; set; } = new Dictionary<string, Consumable>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, Spell> SpellsLookup { get; set; } = new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, Character> CharactersLookup { get; set; } = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, Equipment> EquipmentLookup { get; set; } = new Dictionary<string, Equipment>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, QuestToken> QuestTokensLookup { get; set; } = new Dictionary<string, QuestToken>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, Achievement> AchievementsLookup { get; set; } = new Dictionary<string, Achievement>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, Quest> QuestsLookup { get; set; } = new Dictionary<string, Quest>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, AvatarArchetype> AvatarArchetypesLookup { get; set; } = new Dictionary<string, AvatarArchetype>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, DialogueTree> DialogueTreesLookup { get; set; } = new Dictionary<string, DialogueTree>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, CharacterAffinity> CharacterAffinitiesLookup { get; set; } = new Dictionary<string, CharacterAffinity>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, CombatStance> CombatStancesLookup { get; set; } = new Dictionary<string, CombatStance>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, LoadoutSlot> LoadoutSlotsLookup { get; set; } = new Dictionary<string, LoadoutSlot>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, SagaArc> SagaArcLookup { get; set; } = new Dictionary<string, SagaArc>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, List<SagaTrigger>> SagaTriggersLookup { get; set; } = new Dictionary<string, List<SagaTrigger>>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, Faction> FactionsLookup { get; set; } = new Dictionary<string, Faction>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, StatusEffect> StatusEffectsLookup { get; set; } = new Dictionary<string, StatusEffect>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public Dictionary<string, AttackTell> AttackTellsLookup { get; set; } = new Dictionary<string, AttackTell>(StringComparer.OrdinalIgnoreCase);
    [XmlIgnore] public int BlocksBeneathSeaLevel { get; set; } = 64; // this is a todo... coordinate converter requires this - we probably need an interface to do this right.
    [XmlIgnore] public double VerticalScale { get; set; }
    [XmlIgnore] public double VerticalShift { get; set; }
    [XmlIgnore] public GeoTiffMetadata HeightMapMetadata { get; set; }
    [XmlIgnore] public int HeightMapSpawnPixelX { get; set; }
    [XmlIgnore] public int HeightMapSpawnPixelY { get; set; }
    [XmlIgnore] public double HeightMapLatitudeScale { get; set; }
    [XmlIgnore] public double HeightMapLongitudeScale { get; set; }

    public World()
    {
        // these are mostly for tests, it seems
        WorldConfiguration = new WorldConfiguration();
        WorldTemplate = new WorldTemplate();
    }

    /// <summary>
    /// Looks up a Tool object by its RefName using the efficient ToolsLookup dictionary.
    /// </summary>
    /// <param name="toolRefName">The RefName of the tool to find</param>
    /// <returns>The Tool object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the tool is not found</exception>
    public Tool GetToolByRefName(string toolRefName)
    {
        if (ToolsLookup.TryGetValue(toolRefName, out var tool))
        {
            return tool;
        }

        throw new InvalidOperationException($"Tool with RefName '{toolRefName}' not found in Tools catalog");
    }

    /// <summary>
    /// Tries to look up a Tool object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="toolRefName">The RefName of the tool to find</param>
    /// <returns>The Tool object with the specified RefName, or null if not found</returns>
    public Tool? TryGetToolByRefName(string toolRefName)
    {
        ToolsLookup.TryGetValue(toolRefName, out var tool);
        return tool;
    }

    /// <summary>
    /// Looks up a Material object by its RefName using the efficient MaterialsLookup dictionary.
    /// </summary>
    /// <param name="buildingMaterialRefName">The RefName of the material to find</param>
    /// <returns>The Material object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the material is not found</exception>
    public BuildingMaterial GetBuildingMaterialByRefName(string buildingMaterialRefName)
    {
        if (BuildingMaterialsLookup.TryGetValue(buildingMaterialRefName, out var material))
        {
            return material;
        }

        throw new InvalidOperationException($"Material with RefName '{buildingMaterialRefName}' not found in Materials catalog");
    }

    /// <summary>
    /// Tries to look up a Material object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="buildingMaterialRefName">The RefName of the material to find</param>
    /// <returns>The Material object with the specified RefName, or null if not found</returns>
    public BuildingMaterial? TryGetBuildingMaterialByRefName(string buildingMaterialRefName)
    {
        BuildingMaterialsLookup.TryGetValue(buildingMaterialRefName, out var material);
        return material;
    }

    /// <summary>
    /// Looks up a Consumable object by its RefName using the efficient ConsumablesLookup dictionary.
    /// </summary>
    /// <param name="consumableRefName">The RefName of the consumable to find</param>
    /// <returns>The Consumable object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the consumable is not found</exception>
    public Consumable GetConsumableByRefName(string consumableRefName)
    {
        if (ConsumablesLookup.TryGetValue(consumableRefName, out var consumable))
        {
            return consumable;
        }

        throw new InvalidOperationException($"Consumable with RefName '{consumableRefName}' not found in Consumables catalog");
    }

    /// <summary>
    /// Tries to look up a Consumable object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="consumableRefName">The RefName of the consumable to find</param>
    /// <returns>The Consumable object with the specified RefName, or null if not found</returns>
    public Consumable? TryGetConsumableByRefName(string consumableRefName)
    {
        ConsumablesLookup.TryGetValue(consumableRefName, out var consumable);
        return consumable;
    }

    /// <summary>
    /// Looks up an Equipment object by its RefName using the efficient EquipmentLookup dictionary.
    /// </summary>
    /// <param name="equipmentRefName">The RefName of the equipment to find</param>
    /// <returns>The Equipment object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the equipment is not found</exception>
    public Equipment GetEquipmentByRefName(string equipmentRefName)
    {
        if (EquipmentLookup.TryGetValue(equipmentRefName, out var equipment))
        {
            return equipment;
        }

        throw new InvalidOperationException($"Equipment with RefName '{equipmentRefName}' not found in Equipment catalog");
    }

    /// <summary>
    /// Tries to look up an Equipment object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="equipmentRefName">The RefName of the equipment to find</param>
    /// <returns>The Equipment object with the specified RefName, or null if not found</returns>
    public Equipment? TryGetEquipmentByRefName(string equipmentRefName)
    {
        EquipmentLookup.TryGetValue(equipmentRefName, out var equipment);
        return equipment;
    }

    /// <summary>
    /// Looks up a Spell object by its RefName using the efficient SpellsLookup dictionary.
    /// </summary>
    /// <param name="spellRefName">The RefName of the spell to find</param>
    /// <returns>The Spell object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the spell is not found</exception>
    public Spell GetSpellByRefName(string spellRefName)
    {
        if (SpellsLookup.TryGetValue(spellRefName, out var spell))
        {
            return spell;
        }

        throw new InvalidOperationException($"Spell with RefName '{spellRefName}' not found in Spells catalog");
    }

    /// <summary>
    /// Tries to look up a Spell object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="spellRefName">The RefName of the spell to find</param>
    /// <returns>The Spell object with the specified RefName, or null if not found</returns>
    public Spell? TryGetSpellByRefName(string spellRefName)
    {
        SpellsLookup.TryGetValue(spellRefName, out var spell);
        return spell;
    }

    /// <summary>
    /// Looks up a Character object by its RefName using the efficient CharactersLookup dictionary.
    /// </summary>
    /// <param name="characterRefName">The RefName of the character to find</param>
    /// <returns>The Character object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the character is not found</exception>
    public Character GetCharacterByRefName(string characterRefName)
    {
        if (CharactersLookup.TryGetValue(characterRefName, out var character))
        {
            return character;
        }

        throw new InvalidOperationException($"Character with RefName '{characterRefName}' not found in Characters catalog");
    }

    /// <summary>
    /// Tries to look up a Character object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="characterRefName">The RefName of the character to find</param>
    /// <returns>The Character object with the specified RefName, or null if not found</returns>
    public Character? TryGetCharacterByRefName(string characterRefName)
    {
        CharactersLookup.TryGetValue(characterRefName, out var character);
        return character;
    }

    /// <summary>
    /// Looks up a Saga object by its RefName using the efficient SagasLookup dictionary.
    /// </summary>
    /// <param name="sagaArcRefName">The RefName of the saga to find</param>
    /// <returns>The Saga object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the saga is not found</exception>
    public SagaArc GetSagaArcByRefName(string sagaArcRefName)
    {
        if (SagaArcLookup.TryGetValue(sagaArcRefName, out var saga))
        {
            return saga;
        }

        throw new InvalidOperationException($"Saga with RefName '{sagaArcRefName}' not found in Sagas catalog");
    }

    /// <summary>
    /// Tries to look up a Saga object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="sagaArcRefName">The RefName of the saga to find</param>
    /// <returns>The Saga object with the specified RefName, or null if not found</returns>
    public SagaArc? TryGetSagaArcByRefName(string sagaArcRefName)
    {
        SagaArcLookup.TryGetValue(sagaArcRefName, out var saga);
        return saga;
    }

    /// <summary>
    /// Looks up a QuestToken object by its RefName using the efficient QuestTokensLookup dictionary.
    /// </summary>
    /// <param name="QuestTokenRefName">The RefName of the quest key to find</param>
    /// <returns>The QuestToken object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the quest key is not found</exception>
    public QuestToken GetQuestTokenByRefName(string QuestTokenRefName)
    {
        if (QuestTokensLookup.TryGetValue(QuestTokenRefName, out var QuestToken))
        {
            return QuestToken;
        }

        throw new InvalidOperationException($"QuestToken with RefName '{QuestTokenRefName}' not found in QuestTokens catalog");
    }

    /// <summary>
    /// Tries to look up a QuestToken object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="QuestTokenRefName">The RefName of the quest key to find</param>
    /// <returns>The QuestToken object with the specified RefName, or null if not found</returns>
    public QuestToken? TryGetQuestTokenByRefName(string QuestTokenRefName)
    {
        QuestTokensLookup.TryGetValue(QuestTokenRefName, out var QuestToken);
        return QuestToken;
    }

    /// <summary>
    /// Looks up an Achievement object by its RefName.
    /// </summary>
    /// <param name="achievementRefName">The RefName of the achievement to find</param>
    /// <returns>The Achievement object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the achievement is not found</exception>
    public Achievement GetAchievementByRefName(string achievementRefName)
    {
        if (AchievementsLookup.TryGetValue(achievementRefName, out var achievement))
        {
            return achievement;
        }

        throw new InvalidOperationException($"Achievement with RefName '{achievementRefName}' not found in Achievements catalog");
    }

    /// <summary>
    /// Tries to look up an Achievement object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="achievementRefName">The RefName of the achievement to find</param>
    /// <returns>The Achievement object with the specified RefName, or null if not found</returns>
    public Achievement? TryGetAchievementByRefName(string achievementRefName)
    {
        AchievementsLookup.TryGetValue(achievementRefName, out var achievement);
        return achievement;
    }

    /// <summary>
    /// Looks up a Quest object by its RefName.
    /// </summary>
    /// <param name="questRefName">The RefName of the quest to find</param>
    /// <returns>The Quest object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the quest is not found</exception>
    public Quest GetQuestByRefName(string questRefName)
    {
        if (QuestsLookup.TryGetValue(questRefName, out var quest))
        {
            return quest;
        }

        throw new InvalidOperationException($"Quest with RefName '{questRefName}' not found in Quests catalog");
    }

    /// <summary>
    /// Tries to look up a Quest object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="questRefName">The RefName of the quest to find</param>
    /// <returns>The Quest object with the specified RefName, or null if not found</returns>
    public Quest? TryGetQuestByRefName(string questRefName)
    {
        QuestsLookup.TryGetValue(questRefName, out var quest);
        return quest;
    }

    /// <summary>
    /// Looks up a CharacterAffinity object by its RefName.
    /// </summary>
    /// <param name="characterAffinityRefName">The RefName of the character affinity to find</param>
    /// <returns>The CharacterAffinity object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the character affinity is not found</exception>
    public CharacterAffinity GetCharacterAffinityByRefName(string characterAffinityRefName)
    {
        if (CharacterAffinitiesLookup.TryGetValue(characterAffinityRefName, out var characterAffinity))
        {
            return characterAffinity;
        }

        throw new InvalidOperationException($"CharacterAffinity with RefName '{characterAffinityRefName}' not found in CharacterAffinities catalog");
    }

    /// <summary>
    /// Tries to look up a CharacterAffinity object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="characterAffinityRefName">The RefName of the character affinity to find</param>
    /// <returns>The CharacterAffinity object with the specified RefName, or null if not found</returns>
    public CharacterAffinity? TryGetCharacterAffinityByRefName(string characterAffinityRefName)
    {
        CharacterAffinitiesLookup.TryGetValue(characterAffinityRefName, out var characterAffinity);
        return characterAffinity;
    }

    /// <summary>
    /// Looks up a CombatStance object by its RefName.
    /// </summary>
    /// <param name="combatStanceRefName">The RefName of the combat stance to find</param>
    /// <returns>The CombatStance object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the combat stance is not found</exception>
    public CombatStance GetCombatStanceByRefName(string combatStanceRefName)
    {
        if (CombatStancesLookup.TryGetValue(combatStanceRefName, out var combatStance))
        {
            return combatStance;
        }

        throw new InvalidOperationException($"CombatStance with RefName '{combatStanceRefName}' not found in CombatStances catalog");
    }

    /// <summary>
    /// Tries to look up a CombatStance object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="combatStanceRefName">The RefName of the combat stance to find</param>
    /// <returns>The CombatStance object with the specified RefName, or null if not found</returns>
    public CombatStance? TryGetCombatStanceByRefName(string combatStanceRefName)
    {
        CombatStancesLookup.TryGetValue(combatStanceRefName, out var combatStance);
        return combatStance;
    }

    /// <summary>
    /// Looks up a LoadoutSlot object by its RefName.
    /// </summary>
    /// <param name="loadoutSlotRefName">The RefName of the loadout slot to find</param>
    /// <returns>The LoadoutSlot object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the loadout slot is not found</exception>
    public LoadoutSlot GetLoadoutSlotByRefName(string loadoutSlotRefName)
    {
        if (LoadoutSlotsLookup.TryGetValue(loadoutSlotRefName, out var loadoutSlot))
        {
            return loadoutSlot;
        }

        throw new InvalidOperationException($"LoadoutSlot with RefName '{loadoutSlotRefName}' not found in LoadoutSlots catalog");
    }

    /// <summary>
    /// Tries to look up a LoadoutSlot object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="loadoutSlotRefName">The RefName of the loadout slot to find</param>
    /// <returns>The LoadoutSlot object with the specified RefName, or null if not found</returns>
    public LoadoutSlot? TryGetLoadoutSlotByRefName(string loadoutSlotRefName)
    {
        LoadoutSlotsLookup.TryGetValue(loadoutSlotRefName, out var loadoutSlot);
        return loadoutSlot;
    }

    /// <summary>
    /// Looks up a Faction object by its RefName using the efficient FactionsLookup dictionary.
    /// </summary>
    /// <param name="factionRefName">The RefName of the faction to find</param>
    /// <returns>The Faction object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the faction is not found</exception>
    public Faction GetFactionByRefName(string factionRefName)
    {
        if (FactionsLookup.TryGetValue(factionRefName, out var faction))
        {
            return faction;
        }

        throw new InvalidOperationException($"Faction with RefName '{factionRefName}' not found in Factions catalog");
    }

    /// <summary>
    /// Tries to look up a Faction object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="factionRefName">The RefName of the faction to find</param>
    /// <returns>The Faction object with the specified RefName, or null if not found</returns>
    public Faction? TryGetFactionByRefName(string factionRefName)
    {
        FactionsLookup.TryGetValue(factionRefName, out var faction);
        return faction;
    }

    /// <summary>
    /// Looks up a StatusEffect object by its RefName.
    /// </summary>
    /// <param name="statusEffectRefName">The RefName of the status effect to find</param>
    /// <returns>The StatusEffect object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the status effect is not found</exception>
    public StatusEffect GetStatusEffectByRefName(string statusEffectRefName)
    {
        if (StatusEffectsLookup.TryGetValue(statusEffectRefName, out var statusEffect))
        {
            return statusEffect;
        }

        throw new InvalidOperationException($"StatusEffect with RefName '{statusEffectRefName}' not found in StatusEffects catalog");
    }

    /// <summary>
    /// Tries to look up a StatusEffect object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="statusEffectRefName">The RefName of the status effect to find</param>
    /// <returns>The StatusEffect object with the specified RefName, or null if not found</returns>
    public StatusEffect? TryGetStatusEffectByRefName(string statusEffectRefName)
    {
        StatusEffectsLookup.TryGetValue(statusEffectRefName, out var statusEffect);
        return statusEffect;
    }

    /// <summary>
    /// Looks up an AttackTell object by its RefName.
    /// </summary>
    /// <param name="attackTellRefName">The RefName of the attack tell to find</param>
    /// <returns>The AttackTell object with the specified RefName</returns>
    /// <exception cref="InvalidOperationException">Thrown if the attack tell is not found</exception>
    public AttackTell GetAttackTellByRefName(string attackTellRefName)
    {
        if (AttackTellsLookup.TryGetValue(attackTellRefName, out var attackTell))
        {
            return attackTell;
        }

        throw new InvalidOperationException($"AttackTell with RefName '{attackTellRefName}' not found in AttackTells catalog");
    }

    /// <summary>
    /// Tries to look up an AttackTell object by its RefName. Returns null if not found.
    /// </summary>
    /// <param name="attackTellRefName">The RefName of the attack tell to find</param>
    /// <returns>The AttackTell object with the specified RefName, or null if not found</returns>
    public AttackTell? TryGetAttackTellByRefName(string attackTellRefName)
    {
        AttackTellsLookup.TryGetValue(attackTellRefName, out var attackTell);
        return attackTell;
    }

}