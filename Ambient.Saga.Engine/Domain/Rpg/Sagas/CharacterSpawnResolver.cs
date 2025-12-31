using Ambient.Domain;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas;

/// <summary>
/// Resolves CharacterSpawn definitions into concrete character references.
/// Now simplified since CharacterSpawn only contains CharacterRef (no archetypes).
/// </summary>
public class CharacterSpawnResolver
{
    private readonly IWorld _world;

    public CharacterSpawnResolver(IWorld world, int? randomSeed = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
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

        // CharacterSpawn now directly contains CharacterRef
        var characterRef = spawn.CharacterRef;

        if (string.IsNullOrEmpty(characterRef))
        {
            throw new InvalidOperationException("CharacterSpawn must have a CharacterRef");
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
}
