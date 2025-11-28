using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;
using Ambient.Saga.StoryGenerator.Services;
using Ambient.Saga.StoryGenerator.XmlGenerators;
using Ambient.StoryGenerator;
using System.Xml.Linq;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Generates story content (Structures, Sagas, Landmarks, QuestSignposts) from separate GenerationConfiguration files
/// </summary>
public class StoryGenerator
{
    private readonly PathGenerationService _pathService;
    private readonly XmlGeneratorRegistry _xmlGenerators;

    public StoryGenerator()
    {
        _pathService = new PathGenerationService();
        var refNameGen = new RefNameGenerator();
        var questGenerator = new QuestGenerator(refNameGen);
        var factionGenerator = new FactionGenerator(refNameGen);
        _xmlGenerators = XmlGeneratorRegistry.CreateDefault(questGenerator, factionGenerator);
    }


    /// <summary>
    /// Generates XML files for a world using separate GenerationConfiguration
    /// </summary>
    /// <param name="worldConfig">The world configuration (runtime settings)</param>
    /// <param name="generationConfig">The generation configuration (story content settings)</param>
    /// <param name="outputDirectory">Base output directory (e.g., WorldDefinitions)</param>
    /// <param name="updateWorldConfiguration">If true, updates WorldConfigurations.xml to point to generated files</param>
    /// <returns>List of generated file paths</returns>
    public List<string> GenerateStoryContent(WorldConfiguration worldConfig, GenerationConfiguration generationConfig, string outputDirectory, bool updateWorldConfiguration = true)
    {
        if (generationConfig == null)
        {
            throw new ArgumentNullException(nameof(generationConfig));
        }

        if (generationConfig.WorldRef != worldConfig.RefName)
        {
            throw new InvalidOperationException($"Generation config WorldRef '{generationConfig.WorldRef}' does not match world config RefName '{worldConfig.RefName}'");
        }

        var generatedFiles = new List<string>();
        var refName = worldConfig.RefName;
        var sourceLocations = generationConfig.SourceLocations?.ToList()  ?? new List<SourceLocation>(); 
        var generationStyle = generationConfig.GenerationStyle;
        var spacing = generationConfig.Spacing;

        // Validate generation parameters
        _pathService.ValidateGenerationParameters(worldConfig, sourceLocations, generationStyle);

        // Validate point count before generation
        var estimatedPoints = _pathService.EstimatePointCount(worldConfig, sourceLocations, generationStyle, spacing);
        if (estimatedPoints > 500)
        {
            throw new InvalidOperationException(
                $"Story generation would create {estimatedPoints} points (max 500). " +
                $"Reduce hub count, increase spacing, or use Trail style.");
        }

        // Build MST and generate narrative structure
        var refNameGenerator = new RefNameGenerator();
        List<SourceLocation> uniqueLocations;
        List<(SourceLocation from, SourceLocation to)> mstEdges;

        if (generationStyle == GenerationStyle.RadialExploration)
        {
            if (sourceLocations.Count == 0)
            {
                // Single star from spawn
                uniqueLocations = _pathService.GenerateStarLocations(worldConfig.SpawnLatitude, worldConfig.SpawnLongitude,
                    spacing, refNameGenerator, "Spawn");
                mstEdges = new List<(SourceLocation, SourceLocation)>();
            }
            else if (sourceLocations.Count == 1)
            {
                // Single star from specified location
                var hub = sourceLocations[0];
                uniqueLocations = _pathService.GenerateStarLocations(hub.Lat, hub.Lon, spacing, refNameGenerator, hub.DisplayName);
                mstEdges = new List<(SourceLocation, SourceLocation)>();
            }
            else
            {
                // Hybrid: Stars at each hub + MST trail connecting them
                var hubLocations = new List<SourceLocation>();
                foreach (var hub in sourceLocations)
                {
                    var starLocations = _pathService.GenerateStarLocations(hub.Lat, hub.Lon, spacing, refNameGenerator, hub.DisplayName);
                    hubLocations.AddRange(starLocations);
                }

                // Build trail connecting the hubs
                var mstPath = _pathService.BuildMSTPath(sourceLocations);
                var subdividedPath = _pathService.SubdividePath(mstPath, refNameGenerator, maxDistanceMeters: spacing);
                var trailLocations = _pathService.ExtractUniqueLocations(subdividedPath);

                // Combine hub locations and trail locations
                uniqueLocations = hubLocations.Concat(trailLocations).ToList();
                mstEdges = _pathService.BuildMSTEdges(mstPath);
            }
        }
        else // Trail
        {
            // Pure MST trail (no hubs)
            var mstPath = _pathService.BuildMSTPath(sourceLocations);
            var subdividedPath = _pathService.SubdividePath(mstPath, refNameGenerator, maxDistanceMeters: spacing);
            uniqueLocations = _pathService.ExtractUniqueLocations(subdividedPath);
            mstEdges = _pathService.BuildMSTEdges(mstPath);
        }

        // Determine output directory structure based on Template
        var folder = worldConfig.Template ?? "Procedural";
        var baseDir = Path.Combine(outputDirectory, folder, "Gameplay");

        // Generate comprehensive narrative structure
        var narrativeGenerator = new NarrativeGenerator(refNameGenerator);
        var narrative = narrativeGenerator.GenerateNarrative(mstEdges, uniqueLocations, refName);

        // Load theme content (for character archetypes, equipment, spells, etc.)
        var themeRef = generationConfig.Theme ?? "Default";
        var themesPath = Path.Combine(AppContext.BaseDirectory, "Themes");
        var themeLoader = new ThemeLoader(themesPath);

        ThemeContent? theme = null;
        try
        {
            theme = themeLoader.LoadThemeAsync(themeRef).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load theme '{themeRef}': {ex.Message}");
            Console.WriteLine("Falling back to Default theme...");

            if (themeRef != "Default")
            {
                theme = themeLoader.LoadThemeAsync("Default").GetAwaiter().GetResult();
            }
        }


        // Create generation context for XML generators
        var generationContext = new GenerationContext
        {
            WorldConfig = worldConfig,
            Narrative = narrative,
            RefNameGenerator = refNameGenerator,
            Theme = theme,
            UniqueLocations = uniqueLocations,
            SourceLocations = generationConfig.SourceLocations
        };

        // Generate SagaFeatures XML (unified: replaces old Structures, Landmarks, QuestSignposts)
        var sagaFeaturesPath = Path.Combine(baseDir, "Features", $"{refName}.SagaFeatures.xml");
        _xmlGenerators.Get("SagaFeatures").GenerateXml(generationContext, sagaFeaturesPath);
        generatedFiles.Add(sagaFeaturesPath);

        // Generate SagaArcs XML (with AIMetadata and SagaTriggers)
        var sagasPath = Path.Combine(baseDir, $"{refName}.Sagas.xml");
        _xmlGenerators.Get("Sagas").GenerateXml(generationContext, sagasPath);
        generatedFiles.Add(sagasPath);

        // Generate QuestTokens XML
        var questTokensPath = Path.Combine(baseDir, "Acquirables", $"{refName}.QuestTokens.xml");
        _xmlGenerators.Get("QuestTokens").GenerateXml(generationContext, questTokensPath);
        generatedFiles.Add(questTokensPath);

        // Generate Equipment XML
        var equipmentPath = Path.Combine(baseDir, "Acquirables", $"{refName}.Equipment.xml");
        _xmlGenerators.Get("Equipment").GenerateXml(generationContext, equipmentPath);
        generatedFiles.Add(equipmentPath);

        // Generate Spells XML
        var spellsPath = Path.Combine(baseDir, "Acquirables", $"{refName}.Spells.xml");
        _xmlGenerators.Get("Spells").GenerateXml(generationContext, spellsPath);
        generatedFiles.Add(spellsPath);

        // Generate Characters XML (in Actors folder!)
        var charactersPath = Path.Combine(baseDir, "Actors", $"{refName}.Characters.xml");
        _xmlGenerators.Get("Characters").GenerateXml(generationContext, charactersPath);
        generatedFiles.Add(charactersPath);

        // Generate DialogueTrees XML (in Actors folder!)
        var dialoguePath = Path.Combine(baseDir, "Actors", $"{refName}.Dialogue.xml");
        _xmlGenerators.Get("Dialogue").GenerateXml(generationContext, dialoguePath);
        generatedFiles.Add(dialoguePath);

        // Generate Achievements XML
        var achievementsPath = Path.Combine(baseDir, "Achievements", $"{refName}.Achievements.xml");
        _xmlGenerators.Get("Achievements").GenerateXml(generationContext, achievementsPath);
        generatedFiles.Add(achievementsPath);

        // Generate Consumables XML
        var consumablesPath = Path.Combine(baseDir, "Acquirables", $"{refName}.Consumable.xml");
        _xmlGenerators.Get("Consumables").GenerateXml(generationContext, consumablesPath);
        generatedFiles.Add(consumablesPath);

        // Generate CharacterArchetypes XML (in Actors folder)
        var characterArchetypesPath = Path.Combine(baseDir, "Actors", $"{refName}.CharacterArchetypes.xml");
        _xmlGenerators.Get("CharacterArchetypes").GenerateXml(generationContext, characterArchetypesPath);
        generatedFiles.Add(characterArchetypesPath);

        // Generate CharacterAffinities XML (in Actors folder)
        var characterAffinitiesPath = Path.Combine(baseDir, "Actors", $"{refName}.CharacterAffinities.xml");
        _xmlGenerators.Get("CharacterAffinities").GenerateXml(generationContext, characterAffinitiesPath);
        generatedFiles.Add(characterAffinitiesPath);

        // Generate CombatStances XML (in Actors folder)
        var combatStancesPath = Path.Combine(baseDir, "Actors", $"{refName}.CombatStances.xml");
        _xmlGenerators.Get("CombatStances").GenerateXml(generationContext, combatStancesPath);
        generatedFiles.Add(combatStancesPath);

        // Generate LoadoutSlots XML
        var loadoutSlotsPath = Path.Combine(baseDir, $"{refName}.LoadoutSlots.xml");
        _xmlGenerators.Get("LoadoutSlots").GenerateXml(generationContext, loadoutSlotsPath);
        generatedFiles.Add(loadoutSlotsPath);

        // Generate AvatarArchetypes XML (in Actors folder)
        var avatarArchetypesPath = Path.Combine(baseDir, "Actors", $"{refName}.AvatarArchetypes.xml");
        _xmlGenerators.Get("AvatarArchetypes").GenerateXml(generationContext, avatarArchetypesPath);
        generatedFiles.Add(avatarArchetypesPath);

        // Generate Quests XML
        var questsPath = Path.Combine(baseDir, "Quests", $"{refName}.Quests.xml");
        _xmlGenerators.Get("Quests").GenerateXml(generationContext, questsPath);
        generatedFiles.Add(questsPath);

        // Generate Factions XML
        var factionsPath = Path.Combine(baseDir, "Factions", $"{refName}.Factions.xml");
        _xmlGenerators.Get("Factions").GenerateXml(generationContext, factionsPath);
        generatedFiles.Add(factionsPath);

        // Generate StatusEffects XML (in Actors folder with other catalogs)
        var statusEffectsPath = Path.Combine(baseDir, "Actors", $"{refName}.StatusEffects.xml");
        _xmlGenerators.Get("StatusEffects").GenerateXml(generationContext, statusEffectsPath);
        generatedFiles.Add(statusEffectsPath);

        // Generate SagaTriggerPatterns XML
        var sagaTriggerPatternsPath = Path.Combine(baseDir, "SagaTriggerPatterns", $"{refName}.SagaTriggerPatterns.xml");
        _xmlGenerators.Get("SagaTriggerPatterns").GenerateXml(generationContext, sagaTriggerPatternsPath);
        generatedFiles.Add(sagaTriggerPatternsPath);

        // Generate Tools XML (copy of Default tools)
        var toolsPath = Path.Combine(baseDir, "Acquirables", $"{refName}.Tools.xml");
        _xmlGenerators.Get("Tools").GenerateXml(generationContext, toolsPath);
        generatedFiles.Add(toolsPath);

        // Generate BuildingMaterials XML (copy of Default materials)
        var buildingMaterialsPath = Path.Combine(baseDir, "Acquirables", $"{refName}.BuildingMaterials.xml");
        _xmlGenerators.Get("BuildingMaterials").GenerateXml(generationContext, buildingMaterialsPath);
        generatedFiles.Add(buildingMaterialsPath);

        // Update WorldConfigurations.xml to point to generated files
        if (updateWorldConfiguration)
        {
            UpdateWorldConfigurationReferences(worldConfig.RefName, outputDirectory);
        }

        return generatedFiles;
    }

    /// <summary>
    /// Updates WorldConfigurations.xml to point the specified world to its generated files
    /// </summary>
    private void UpdateWorldConfigurationReferences(string worldRefName, string outputDirectory)
    {
        var configPath = Path.Combine(outputDirectory, "WorldConfigurations.xml");
        var doc = XDocument.Load(configPath);
        XNamespace ns = "Ambient.Domain";

        var worldConfig = doc.Root?.Elements(ns + "WorldConfiguration")
            .FirstOrDefault(e => e.Attribute("RefName")?.Value == worldRefName);

        if (worldConfig == null)
        {
            throw new InvalidOperationException($"World configuration '{worldRefName}' not found in WorldConfigurations.xml");
        }

        // Update refs to point to generated files
        worldConfig.SetAttributeValue("SagaFeaturesRef", worldRefName);
        worldConfig.SetAttributeValue("SagaArcsRef", worldRefName);
        worldConfig.SetAttributeValue("QuestTokensRef", worldRefName);
        worldConfig.SetAttributeValue("EquipmentRef", worldRefName);
        worldConfig.SetAttributeValue("SpellsRef", worldRefName);
        worldConfig.SetAttributeValue("ConsumableItemsRef", worldRefName);
        worldConfig.SetAttributeValue("CharacterArchetypesRef", worldRefName);
        worldConfig.SetAttributeValue("CharacterAffinitiesRef", worldRefName);
        worldConfig.SetAttributeValue("CombatStancesRef", worldRefName);
        worldConfig.SetAttributeValue("LoadoutSlotsRef", worldRefName);
        worldConfig.SetAttributeValue("AvatarArchetypesRef", worldRefName);
        worldConfig.SetAttributeValue("QuestsRef", worldRefName);
        worldConfig.SetAttributeValue("SagaTriggerPatternsRef", worldRefName);
        worldConfig.SetAttributeValue("ToolsRef", worldRefName);
        worldConfig.SetAttributeValue("BuildingMaterialsRef", worldRefName);
        worldConfig.SetAttributeValue("CharactersRef", worldRefName);
        worldConfig.SetAttributeValue("DialogueTreesRef", worldRefName);
        worldConfig.SetAttributeValue("AchievementsRef", worldRefName);

        // Save with NewLineOnAttributes for better readability
        var settings = new System.Xml.XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            NewLineOnAttributes = true,
            Encoding = System.Text.Encoding.UTF8
        };

        using (var writer = System.Xml.XmlWriter.Create(configPath, settings))
        {
            doc.Save(writer);
        }
    }
}
