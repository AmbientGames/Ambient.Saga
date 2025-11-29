using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas;

/// <summary>
/// Resolves CharacterSpawn definitions into concrete character references.
/// Handles archetype selection, conditionals, and counts.
/// Server and client use this same logic to determine character spawns.
/// </summary>
public class CharacterSpawnResolver
{
    private readonly World _world;
    private readonly Random _random;

    public CharacterSpawnResolver(World world, int? randomSeed = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
    }

    /// <summary>
    /// Result of spawn resolution - character ref only (quest tokens handled by triggers)
    /// </summary>
    public class ResolvedSpawn
    {
        public string CharacterRef { get; init; } = string.Empty;
        public int Count { get; init; } = 1;
    }

    /// <summary>
    /// Resolves a single CharacterSpawn into concrete character reference(s).
    /// </summary>
    public List<ResolvedSpawn> ResolveSpawn(CharacterSpawn spawn)
    {
        if (spawn == null)
            return new List<ResolvedSpawn>();

        // Determine which character to spawn
        string characterRef;

        if (spawn.ItemElementName == ItemChoiceType.CharacterRef)
        {
            // Specific character
            characterRef = spawn.Item;
        }
        else if (spawn.ItemElementName == ItemChoiceType.CharacterArchetypeRef)
        {
            // Random from archetype pool
            characterRef = SelectFromArchetype(spawn.Item);
        }
        else
        {
            throw new InvalidOperationException("CharacterSpawn must specify either CharacterRef or CharacterArchetypeRef");
        }

        // Create resolved spawn(s) based on Count attribute (default 1)
        var count = spawn.Count;
        var results = new List<ResolvedSpawn>();

        for (var i = 0; i < count; i++)
        {
            results.Add(new ResolvedSpawn
            {
                CharacterRef = characterRef,
                Count = 1 // Each resolved spawn is an individual character
            });
        }

        return results;
    }

    /// <summary>
    /// Resolves all spawns in an array.
    /// </summary>
    public List<ResolvedSpawn> ResolveSpawns(CharacterSpawn[]? spawns)
    {
        if (spawns == null || spawns.Length == 0)
            return new List<ResolvedSpawn>();

        var results = new List<ResolvedSpawn>();
        foreach (var spawn in spawns)
        {
            results.AddRange(ResolveSpawn(spawn));
        }

        return results;
    }

    /// <summary>
    /// Selects a random character from a CharacterArchetype pool.
    /// </summary>
    private string SelectFromArchetype(string archetypeRef)
    {
        var archetype = _world.Gameplay?.CharacterArchetypes?
            .FirstOrDefault(ca => ca.RefName == archetypeRef);

        if (archetype == null)
        {
            throw new InvalidOperationException(
                $"CharacterArchetype '{archetypeRef}' not found in world");
        }

        if (archetype.CharacterRef == null || archetype.CharacterRef.Length == 0)
        {
            throw new InvalidOperationException(
                $"CharacterArchetype '{archetypeRef}' has no character pool");
        }

        // Randomly select one from the pool
        var index = _random.Next(archetype.CharacterRef.Length);
        return archetype.CharacterRef[index];
    }
}
