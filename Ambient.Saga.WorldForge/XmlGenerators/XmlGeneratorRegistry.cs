namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Registry for all XML content generators.
/// Manages the set of generators and provides lookup by name.
/// </summary>
public class XmlGeneratorRegistry
{
    private readonly Dictionary<string, IXmlContentGenerator> _generators = new();

    public void Register(IXmlContentGenerator generator)
    {
        _generators[generator.GeneratorName] = generator;
    }

    public IXmlContentGenerator Get(string generatorName)
    {
        return _generators.TryGetValue(generatorName, out var generator)
            ? generator
            : throw new InvalidOperationException($"No XML generator registered for '{generatorName}'");
    }

    public bool HasGenerator(string generatorName)
    {
        return _generators.ContainsKey(generatorName);
    }

    /// <summary>
    /// Creates a registry with all standard XML generators.
    /// </summary>
    public static XmlGeneratorRegistry CreateDefault(
        QuestGenerator questGenerator,
        FactionGenerator factionGenerator)
    {
        var registry = new XmlGeneratorRegistry();

        // Register all 20 XML generators
        registry.Register(new SagaFeaturesXmlGenerator());
        registry.Register(new SagasXmlGenerator());
        registry.Register(new AchievementsXmlGenerator());
        registry.Register(new QuestTokensXmlGenerator());
        registry.Register(new CharactersXmlGenerator());
        registry.Register(new DialogueXmlGenerator());
        registry.Register(new EquipmentXmlGenerator());
        registry.Register(new SpellsXmlGenerator());
        registry.Register(new ConsumablesXmlGenerator());
        registry.Register(new CharacterArchetypesXmlGenerator());
        registry.Register(new CharacterAffinitiesXmlGenerator());
        registry.Register(new CombatStancesXmlGenerator());
        registry.Register(new LoadoutSlotsXmlGenerator());
        registry.Register(new AvatarArchetypesXmlGenerator());
        registry.Register(new QuestsXmlGenerator(questGenerator));
        registry.Register(new SagaTriggerPatternsXmlGenerator());
        registry.Register(new ToolsXmlGenerator());
        registry.Register(new BuildingMaterialsXmlGenerator());
        registry.Register(new FactionsXmlGenerator(factionGenerator));
        registry.Register(new StatusEffectsXmlGenerator());

        return registry;
    }
}
