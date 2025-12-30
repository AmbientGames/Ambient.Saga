using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Infrastructure.GameLogic;
using Ambient.Infrastructure.Utilities;

namespace Ambient.Infrastructure.GameLogic.Loading;

public static class GameplayComponentLoader
{
    public static async Task LoadAsync(string? definitionDirectory, IWorld world)
    {
        world.WorldTemplate.Gameplay = new GameplayComponents();

        // Try to use schema validation if definition directory exists and contains schemas
        var xsdFilePath = !string.IsNullOrEmpty(definitionDirectory)
            ? Path.Combine(definitionDirectory, "Gameplay", "Gameplay.xsd")
            : null;
        var useValidation = xsdFilePath != null && File.Exists(xsdFilePath);

        await LoadGameplayData(useValidation ? xsdFilePath : null, world);
        BuildGameplayLookups(world);
    }

    private static async Task LoadGameplayData(string? xsdFilePath, IWorld world)
    {
        var config = world.WorldConfiguration;
        var worldRef = config.RefName;
        var resourcePack = config.ResourcePack;
        var ns = config.Namespace;

        world.Gameplay.Consumables = (await LoadXmlAsync<ConsumableCatalog>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Acquirables", "Consumable.xml")).Consumable ?? [];
        world.Gameplay.Spells = (await LoadXmlAsync<SpellCatalog>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Acquirables", "Spells.xml")).Spell ?? [];
        world.Gameplay.Equipment = (await LoadXmlAsync<EquipmentCatalog>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Acquirables", "Equipment.xml")).Equipment ?? [];
        world.Gameplay.QuestTokens = (await LoadXmlAsync<QuestTokens>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Acquirables", "QuestTokens.xml")).QuestToken ?? [];
        world.Gameplay.Characters = (await LoadXmlAsync<Characters>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Actors", "Characters.xml")).Character ?? [];
        world.Gameplay.CharacterAffinities = (await LoadXmlAsync<CharacterAffinities>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Actors", "CharacterAffinities.xml")).Affinity ?? [];
        world.Gameplay.CombatStances = (await LoadXmlAsync<CombatStances>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Actors", "CombatStances.xml")).CombatStance ?? [];
        world.Gameplay.LoadoutSlots = (await LoadXmlAsync<LoadoutSlots>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Combat", "LoadoutSlots.xml")).LoadoutSlot ?? [];
        world.Gameplay.Tools = (await LoadXmlAsync<ToolCatalog>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Acquirables", "Tools.xml")).Tool ?? [];
        world.Gameplay.BuildingMaterials = (await LoadXmlAsync<BuildingMaterialCatalog>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Acquirables", "BuildingMaterials.xml")).BuildingMaterial ?? [];
        world.Gameplay.DialogueTrees = (await LoadXmlAsync<DialogueTrees>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Actors", "Dialogue.xml")).DialogueTree ?? [];
        world.Gameplay.AvatarArchetypes = (await LoadXmlAsync<AvatarArchetypes>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Actors", "AvatarArchetypes.xml")).AvatarArchetype ?? [];
        world.Gameplay.Achievements = (await LoadXmlAsync<Achievements>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Achievements", "Achievements.xml")).Achievement ?? [];
        world.Gameplay.Quests = (await LoadXmlAsync<Quests>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Quests", "Quests.xml")).Quest ?? [];
        world.Gameplay.SagaArcs = (await LoadXmlAsync<SagaArcs>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Sagas.xml")).SagaArc ?? [];
        world.Gameplay.Factions = (await LoadXmlAsync<Factions>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Factions", "Factions.xml")).Faction ?? [];
        world.Gameplay.StatusEffects = (await LoadXmlAsync<StatusEffects>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Actors", "StatusEffects.xml")).StatusEffect ?? [];
        world.Gameplay.AttackTells = (await LoadXmlAsync<AttackTells>(worldRef, resourcePack, ns, xsdFilePath, "Gameplay", "Combat", "AttackTells.xml")).AttackTell ?? [];
    }

    private static async Task<T> LoadXmlAsync<T>(string worldRef, string resourcePack, string ns, string? xsdFilePath, params string[] relativePath)
    {
        var resolvedPath = ContentPathResolver.ResolveXmlPath(worldRef, resourcePack, ns, relativePath);
        if (resolvedPath == null)
        {
            throw new FileNotFoundException($"XML content not found: {string.Join("/", relativePath)}");
        }

        // Use validation if schema file is available, otherwise load without validation
        if (!string.IsNullOrEmpty(xsdFilePath))
        {
            return await XmlLoader.LoadFromXmlAsync<T>(resolvedPath, xsdFilePath);
        }
        return await XmlLoader.LoadFromXmlAsync<T>(resolvedPath);
    }

    private static void BuildGameplayLookups(IWorld world)
    {
        BuildLookup(world.Gameplay.Consumables, world.ConsumablesLookup);
        BuildLookup(world.Gameplay.Spells, world.SpellsLookup);
        BuildLookup(world.Gameplay.Tools, world.ToolsLookup);
        BuildLookup(world.Gameplay.BuildingMaterials, world.BuildingMaterialsLookup);
        BuildLookup(world.Gameplay.Characters, world.CharactersLookup);
        BuildLookup(world.Gameplay.CharacterAffinities, world.CharacterAffinitiesLookup);
        BuildLookup(world.Gameplay.CombatStances, world.CombatStancesLookup);
        BuildLookup(world.Gameplay.LoadoutSlots, world.LoadoutSlotsLookup);
        BuildLookup(world.Gameplay.Equipment, world.EquipmentLookup);
        BuildLookup(world.Gameplay.QuestTokens, world.QuestTokensLookup);
        BuildLookup(world.Gameplay.AvatarArchetypes, world.AvatarArchetypesLookup);
        BuildLookup(world.Gameplay.DialogueTrees, world.DialogueTreesLookup);
        BuildLookup(world.Gameplay.Achievements, world.AchievementsLookup);
        BuildLookup(world.Gameplay.Quests, world.QuestsLookup);
        BuildLookup(world.Gameplay.SagaArcs, world.SagaArcLookup);
        BuildLookup(world.Gameplay.Factions, world.FactionsLookup);
        BuildLookup(world.Gameplay.StatusEffects, world.StatusEffectsLookup);
        BuildLookup(world.Gameplay.AttackTells, world.AttackTellsLookup);

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

    private static void BuildSagaTriggersLookup(IWorld world)
    {
        world.SagaTriggersLookup.Clear();

        if (world.Gameplay.SagaArcs == null)
            return;

        foreach (var saga in world.Gameplay.SagaArcs)
        {
            if (!string.IsNullOrEmpty(saga.RefName))
            {
                // Triggers are now defined directly on SagaArc, no expansion needed
                var triggers = saga.SagaTrigger?.ToList() ?? new List<SagaTrigger>();

                // Validate that every trigger has at least one Spawn
                foreach (var trigger in triggers)
                {
                    if (trigger.Spawn == null || trigger.Spawn.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"SagaTrigger '{trigger.RefName}' in SagaArc '{saga.RefName}' has no Spawn elements. Every trigger must have at least one character spawn.");
                    }
                }

                world.SagaTriggersLookup[saga.RefName] = triggers;
            }
        }
    }

}
