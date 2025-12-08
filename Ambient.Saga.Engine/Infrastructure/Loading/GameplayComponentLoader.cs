using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.Utilities;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;

namespace Ambient.Saga.Engine.Infrastructure.Loading;

internal static class GameplayComponentLoader
{
    public static async Task LoadAsync(string dataDirectory, string definitionDirectory, World world)
    {
        var xsdFilePath = Path.Combine(definitionDirectory, "Gameplay", "Gameplay.xsd");
        world.WorldTemplate.Gameplay = new GameplayComponents();
        
        await LoadGameplayData(dataDirectory, xsdFilePath, world);
        BuildGameplayLookups(world);
    }

    private static async Task LoadGameplayData(string dataDirectory, string xsdFilePath, World world)
    {
        var config = world.WorldConfiguration;

        // Resolve refs - "Default" means use the WorldConfiguration's RefName
        var defaultRef = config.RefName;

        var consumableItemsRef = ResolveRef(config.ConsumableItemsRef, defaultRef);
        var spellsRef = ResolveRef(config.SpellsRef, defaultRef);
        var equipmentRef = ResolveRef(config.EquipmentRef, defaultRef);
        var questTokensRef = ResolveRef(config.QuestTokensRef, defaultRef);
        var charactersRef = ResolveRef(config.CharactersRef, defaultRef);
        var characterArchetypesRef = ResolveRef(config.CharacterArchetypesRef, defaultRef);
        var characterAffinitiesRef = ResolveRef(config.CharacterAffinitiesRef, defaultRef);
        var combatStancesRef = ResolveRef(config.CombatStancesRef, defaultRef);
        var loadoutSlotsRef = ResolveRef(config.LoadoutSlotsRef, defaultRef);
        var toolsRef = ResolveRef(config.ToolsRef, defaultRef);
        var materialsRef = ResolveRef(config.BuildingMaterialsRef, defaultRef);
        var dialogueTreesRef = ResolveRef(config.DialogueTreesRef, defaultRef);
        var avatarArchetypesRef = ResolveRef(config.AvatarArchetypesRef, defaultRef);
        var sagaFeaturesRef = ResolveRef(config.SagaFeaturesRef, defaultRef);
        var achievementsRef = ResolveRef(config.AchievementsRef, defaultRef);
        var questsRef = ResolveRef(config.QuestsRef, defaultRef);
        var sagaTriggerPatternsRef = ResolveRef(config.SagaTriggerPatternsRef, defaultRef);
        var sagasRef = ResolveRef(config.SagaArcsRef, defaultRef);
        var factionsRef = ResolveRef(config.FactionsRef, defaultRef);

        // StatusEffects uses the same ref pattern as CharacterAffinities (same folder)
        var statusEffectsRef = characterAffinitiesRef;

        world.Gameplay.Consumables = (await XmlLoader.LoadFromXmlAsync<ConsumableCatalog>(Path.Combine(dataDirectory, "Gameplay", "Acquirables", $"{consumableItemsRef}.Consumable.xml"), xsdFilePath)).Consumable ?? [];
        world.Gameplay.Spells = (await XmlLoader.LoadFromXmlAsync<SpellCatalog>(Path.Combine(dataDirectory, "Gameplay", "Acquirables", $"{spellsRef}.Spells.xml"), xsdFilePath)).Spell ?? [];
        world.Gameplay.Equipment = (await XmlLoader.LoadFromXmlAsync<EquipmentCatalog>(Path.Combine(dataDirectory, "Gameplay", "Acquirables", $"{equipmentRef}.Equipment.xml"), xsdFilePath)).Equipment ?? [];
        world.Gameplay.QuestTokens = (await XmlLoader.LoadFromXmlAsync<QuestTokens>(Path.Combine(dataDirectory, "Gameplay", "Acquirables", $"{questTokensRef}.QuestTokens.xml"), xsdFilePath)).QuestToken ?? [];
        world.Gameplay.Characters = (await XmlLoader.LoadFromXmlAsync<Characters>(Path.Combine(dataDirectory, "Gameplay", "Actors", $"{charactersRef}.Characters.xml"), xsdFilePath)).Character ?? [];
        world.Gameplay.CharacterArchetypes = (await XmlLoader.LoadFromXmlAsync<CharacterArchetypes>(Path.Combine(dataDirectory, "Gameplay", "Actors", $"{characterArchetypesRef}.CharacterArchetypes.xml"), xsdFilePath)).CharacterArchetype ?? [];
        world.Gameplay.CharacterAffinities = (await XmlLoader.LoadFromXmlAsync<CharacterAffinities>(Path.Combine(dataDirectory, "Gameplay", "Actors", $"{characterAffinitiesRef}.CharacterAffinities.xml"), xsdFilePath)).Affinity ?? [];
        world.Gameplay.CombatStances = (await XmlLoader.LoadFromXmlAsync<CombatStances>(Path.Combine(dataDirectory, "Gameplay", "Actors", $"{combatStancesRef}.CombatStances.xml"), xsdFilePath)).CombatStance ?? [];
        world.Gameplay.LoadoutSlots = (await XmlLoader.LoadFromXmlAsync<LoadoutSlots>(Path.Combine(dataDirectory, "Gameplay", $"{loadoutSlotsRef}.LoadoutSlots.xml"), xsdFilePath)).LoadoutSlot ?? [];
        world.Gameplay.Tools = (await XmlLoader.LoadFromXmlAsync<ToolCatalog>(Path.Combine(dataDirectory, "Gameplay", "Acquirables", $"{toolsRef}.Tools.xml"), xsdFilePath)).Tool ?? [];
        world.Gameplay.BuildingMaterials = (await XmlLoader.LoadFromXmlAsync<BuildingMaterialCatalog>(Path.Combine(dataDirectory, "Gameplay", "Acquirables", $"{materialsRef}.BuildingMaterials.xml"), xsdFilePath)).BuildingMaterial ?? [];
        world.Gameplay.DialogueTrees = (await XmlLoader.LoadFromXmlAsync<DialogueTrees>(Path.Combine(dataDirectory, "Gameplay", "Actors", $"{dialogueTreesRef}.Dialogue.xml"), xsdFilePath)).DialogueTree ?? [];
        world.Gameplay.AvatarArchetypes = (await XmlLoader.LoadFromXmlAsync<AvatarArchetypes>(Path.Combine(dataDirectory, "Gameplay", "Actors", $"{avatarArchetypesRef}.AvatarArchetypes.xml"), xsdFilePath)).AvatarArchetype ?? [];
        world.Gameplay.SagaFeatures = (await XmlLoader.LoadFromXmlAsync<SagaFeatures>(Path.Combine(dataDirectory, "Gameplay", "Features", $"{sagaFeaturesRef}.SagaFeatures.xml"), xsdFilePath)).SagaFeature ?? [];
        world.Gameplay.Achievements = (await XmlLoader.LoadFromXmlAsync<Achievements>(Path.Combine(dataDirectory, "Gameplay", "Achievements", $"{achievementsRef}.Achievements.xml"), xsdFilePath)).Achievement ?? [];
        world.Gameplay.Quests = (await XmlLoader.LoadFromXmlAsync<Quests>(Path.Combine(dataDirectory, "Gameplay", "Quests", $"{questsRef}.Quests.xml"), xsdFilePath)).Quest ?? [];
        world.Gameplay.SagaTriggerPatterns = (await XmlLoader.LoadFromXmlAsync<SagaTriggerPatterns>(Path.Combine(dataDirectory, "Gameplay", "SagaTriggerPatterns", $"{sagaTriggerPatternsRef}.SagaTriggerPatterns.xml"), xsdFilePath)).SagaTriggerPattern ?? [];
        world.Gameplay.SagaArcs = (await XmlLoader.LoadFromXmlAsync<SagaArcs>(Path.Combine(dataDirectory, "Gameplay", $"{sagasRef}.Sagas.xml"), xsdFilePath)).SagaArc ?? [];
        world.Gameplay.Factions = (await XmlLoader.LoadFromXmlAsync<Factions>(Path.Combine(dataDirectory, "Gameplay", "Factions", $"{factionsRef}.Factions.xml"), xsdFilePath)).Faction ?? [];

        // Load StatusEffects if the file exists (optional component)
        var statusEffectsPath = Path.Combine(dataDirectory, "Gameplay", "Actors", $"{statusEffectsRef}.StatusEffects.xml");
        if (File.Exists(statusEffectsPath))
        {
            world.Gameplay.StatusEffects = (await XmlLoader.LoadFromXmlAsync<StatusEffects>(statusEffectsPath, xsdFilePath)).StatusEffect ?? [];
        }

        ApplySagaSpawnOffsets(world);
    }

    private static void ApplySagaSpawnOffsets(World world)
    {
        if (world.WorldConfiguration?.Item is ProceduralSettings)
        {
            var spawnLat = world.WorldConfiguration.SpawnLatitude;
            var spawnLon = world.WorldConfiguration.SpawnLongitude;

            foreach (var saga in world.Gameplay.SagaArcs)
            {
                saga.LatitudeZ += spawnLat;
                saga.LongitudeX += spawnLon;
            }
        }
    }

    private static void BuildGameplayLookups(World world)
    {
        BuildLookup(world.Gameplay.Consumables, world.ConsumablesLookup);
        BuildLookup(world.Gameplay.Spells, world.SpellsLookup);
        BuildLookup(world.Gameplay.Tools, world.ToolsLookup);
        BuildLookup(world.Gameplay.BuildingMaterials, world.BuildingMaterialsLookup);
        BuildLookup(world.Gameplay.Characters, world.CharactersLookup);
        BuildLookup(world.Gameplay.CharacterArchetypes, world.CharacterArchetypesLookup);
        BuildLookup(world.Gameplay.CharacterAffinities, world.CharacterAffinitiesLookup);
        BuildLookup(world.Gameplay.CombatStances, world.CombatStancesLookup);
        BuildLookup(world.Gameplay.LoadoutSlots, world.LoadoutSlotsLookup);
        BuildLookup(world.Gameplay.Equipment, world.EquipmentLookup);
        BuildLookup(world.Gameplay.QuestTokens, world.QuestTokensLookup);
        BuildLookup(world.Gameplay.AvatarArchetypes, world.AvatarArchetypesLookup);
        BuildLookup(world.Gameplay.DialogueTrees, world.DialogueTreesLookup);
        BuildLookup(world.Gameplay.SagaFeatures, world.SagaFeaturesLookup);
        BuildLookup(world.Gameplay.Achievements, world.AchievementsLookup);
        BuildLookup(world.Gameplay.Quests, world.QuestsLookup);
        BuildLookup(world.Gameplay.SagaTriggerPatterns, world.SagaTriggerPatternsLookup);
        BuildLookup(world.Gameplay.SagaArcs, world.SagaArcLookup);
        BuildLookup(world.Gameplay.Factions, world.FactionsLookup);
        if (world.Gameplay.StatusEffects != null)
        {
            BuildLookup(world.Gameplay.StatusEffects, world.StatusEffectsLookup);
        }

        // Expand all Saga triggers (TriggerPatternRef -> List<SagaTrigger>)
        BuildSagaTriggersLookup(world);
    }

    private static void BuildLookup<T>(IEnumerable<T> items, Dictionary<string, T> lookup) where T : class
    {
        lookup.Clear();

        if (items == null)
            return;

        foreach (var item in items)
        {
            var refNameProperty = item.GetType().GetProperty("RefName");
            if (refNameProperty != null)
            {
                var refName = refNameProperty.GetValue(item) as string;
                if (!string.IsNullOrEmpty(refName))
                {
                    if (lookup.ContainsKey(refName))
                    {
                        var typeName = typeof(T).Name;
                        throw new InvalidOperationException($"Duplicate RefName '{refName}' found in {typeName} catalog. RefName values must be globally unique within each catalog.");
                    }
                    lookup[refName] = item;
                }
            }
        }
    }

    private static void BuildSagaTriggersLookup(World world)
    {
        world.SagaTriggersLookup.Clear();

        if (world.Gameplay.SagaArcs == null)
            return;

        foreach (var saga in world.Gameplay.SagaArcs)
        {
            if (!string.IsNullOrEmpty(saga.RefName))
            {
                var expandedTriggers = TriggerExpander.ExpandTriggersForSaga(saga, world);
                world.SagaTriggersLookup[saga.RefName] = expandedTriggers;
            }
        }
    }

    private static string ResolveRef(string? refValue, string defaultRef)
    {
        // "Default" or null/empty means use the WorldConfiguration's RefName
        if (string.IsNullOrEmpty(refValue) || refValue == "Default")
            return defaultRef;
        return refValue;
    }
}