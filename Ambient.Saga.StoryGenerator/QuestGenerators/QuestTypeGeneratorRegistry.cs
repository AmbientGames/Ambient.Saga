namespace Ambient.Saga.StoryGenerator.QuestGenerators;

/// <summary>
/// Registry for quest type generators using the Strategy pattern.
/// Allows adding new quest types without modifying existing code (Open/Closed Principle).
/// </summary>
public class QuestTypeGeneratorRegistry
{
    private readonly Dictionary<QuestType, IQuestTypeGenerator> _generators = new();

    /// <summary>
    /// Register a quest type generator
    /// </summary>
    public void Register(IQuestTypeGenerator generator)
    {
        if (generator == null)
            throw new ArgumentNullException(nameof(generator));

        _generators[generator.SupportedType] = generator;
    }

    /// <summary>
    /// Get the generator for a specific quest type
    /// </summary>
    /// <param name="type">Quest type</param>
    /// <returns>Generator for that type</returns>
    /// <exception cref="InvalidOperationException">Thrown if no generator is registered for the type</exception>
    public IQuestTypeGenerator Get(QuestType type)
    {
        if (_generators.TryGetValue(type, out var generator))
            return generator;

        throw new InvalidOperationException(
            $"No quest generator registered for type '{type}'. " +
            $"Available types: {string.Join(", ", _generators.Keys)}");
    }

    /// <summary>
    /// Check if a generator is registered for a quest type
    /// </summary>
    public bool HasGenerator(QuestType type)
    {
        return _generators.ContainsKey(type);
    }

    /// <summary>
    /// Get all registered quest types
    /// </summary>
    public IReadOnlyCollection<QuestType> RegisteredTypes => _generators.Keys;

    /// <summary>
    /// Create a registry with all standard quest generators registered
    /// </summary>
    public static QuestTypeGeneratorRegistry CreateDefault(QuestGenerationContext context)
    {
        var registry = new QuestTypeGeneratorRegistry();

        // Register all standard quest type generators
        registry.Register(new CombatQuestGenerator(context));
        registry.Register(new ExplorationQuestGenerator(context));
        registry.Register(new CollectionQuestGenerator(context));
        registry.Register(new DialogueQuestGenerator(context));
        registry.Register(new HybridQuestGenerator(context));
        registry.Register(new EscortQuestGenerator(context));
        registry.Register(new DefenseQuestGenerator(context));
        registry.Register(new DiscoveryQuestGenerator(context));
        registry.Register(new PuzzleQuestGenerator(context));
        registry.Register(new CraftingQuestGenerator(context));
        registry.Register(new TradingQuestGenerator(context));

        return registry;
    }
}
