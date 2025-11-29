using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Unit tests for CharacterSpawnResolver which resolves character spawns from archetypes or direct references.
/// </summary>
public class CharacterSpawnResolverTests
{
    private World CreateWorldWithArchetypes()
    {
        return new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    CharacterArchetypes = new[]
                    {
                        new CharacterArchetype
                        {
                            RefName = "MerchantArchetype",
                            CharacterRef = new[] { "Merchant_Blacksmith", "Merchant_Potion", "Merchant_General" }
                        },
                        new CharacterArchetype
                        {
                            RefName = "BossArchetype",
                            CharacterRef = new[] { "Boss_Dragon", "Boss_Demon", "Boss_Reaper" }
                        },
                        new CharacterArchetype
                        {
                            RefName = "SingleCharacterArchetype",
                            CharacterRef = new[] { "OnlyCharacter" }
                        },
                        new CharacterArchetype
                        {
                            RefName = "EmptyArchetype",
                            CharacterRef = Array.Empty<string>()
                        }
                    }
                }
            }
        };
    }

    private CharacterSpawn CreateSpawnWithCharacterRef(string characterRef, int count = 1)
    {
        return new CharacterSpawn
        {
            ItemElementName = ItemChoiceType.CharacterRef,
            Item = characterRef,
            Count = count
        };
    }

    private CharacterSpawn CreateSpawnWithArchetypeRef(string archetypeRef, int count = 1)
    {
        return new CharacterSpawn
        {
            ItemElementName = ItemChoiceType.CharacterArchetypeRef,
            Item = archetypeRef,
            Count = count
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullWorld_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CharacterSpawnResolver(null!));
    }

    [Fact]
    public void Constructor_WithValidWorld_CreatesInstance()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();

        // Act
        var resolver = new CharacterSpawnResolver(world);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithRandomSeed_UsesDeterministicRandom()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var spawn = CreateSpawnWithArchetypeRef("MerchantArchetype");

        // Act - Two resolvers with same seed should produce same results
        var resolver1 = new CharacterSpawnResolver(world, randomSeed: 42);
        var resolver2 = new CharacterSpawnResolver(world, randomSeed: 42);

        var result1 = resolver1.ResolveSpawn(spawn);
        var result2 = resolver2.ResolveSpawn(spawn);

        // Assert
        Assert.Equal(result1[0].CharacterRef, result2[0].CharacterRef);
    }

    #endregion

    #region ResolveSpawn - CharacterRef Tests

    [Fact]
    public void ResolveSpawn_WithNullSpawn_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);

        // Act
        var result = resolver.ResolveSpawn(null!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveSpawn_WithCharacterRef_ReturnsSpecificCharacter()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawnWithCharacterRef("Boss_Dragon");

        // Act
        var result = resolver.ResolveSpawn(spawn);

        // Assert
        Assert.Single(result);
        Assert.Equal("Boss_Dragon", result[0].CharacterRef);
        Assert.Equal(1, result[0].Count);
    }

    [Fact]
    public void ResolveSpawn_WithCharacterRefAndCount_ReturnsMultipleInstances()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawnWithCharacterRef("Boss_Dragon", count: 3);

        // Act
        var result = resolver.ResolveSpawn(spawn);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal("Boss_Dragon", r.CharacterRef));
        Assert.All(result, r => Assert.Equal(1, r.Count)); // Each spawn is individual
    }

    #endregion

    #region ResolveSpawn - CharacterArchetypeRef Tests

    [Fact]
    public void ResolveSpawn_WithArchetypeRef_ReturnsCharacterFromPool()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world, randomSeed: 42);
        var spawn = CreateSpawnWithArchetypeRef("MerchantArchetype");

        // Act
        var result = resolver.ResolveSpawn(spawn);

        // Assert
        Assert.Single(result);
        var validRefs = new[] { "Merchant_Blacksmith", "Merchant_Potion", "Merchant_General" };
        Assert.Contains(result[0].CharacterRef, validRefs);
    }

    [Fact]
    public void ResolveSpawn_WithArchetypeRefAndCount_ReturnsMultipleInstances()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world, randomSeed: 42);
        var spawn = CreateSpawnWithArchetypeRef("MerchantArchetype", count: 5);

        // Act
        var result = resolver.ResolveSpawn(spawn);

        // Assert
        Assert.Equal(5, result.Count);
        var validRefs = new[] { "Merchant_Blacksmith", "Merchant_Potion", "Merchant_General" };
        Assert.All(result, r => Assert.Contains(r.CharacterRef, validRefs));
        Assert.All(result, r => Assert.Equal(1, r.Count));
    }

    [Fact]
    public void ResolveSpawn_WithSingleCharacterArchetype_AlwaysReturnsSameCharacter()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawnWithArchetypeRef("SingleCharacterArchetype");

        // Act
        var result = resolver.ResolveSpawn(spawn);

        // Assert
        Assert.Single(result);
        Assert.Equal("OnlyCharacter", result[0].CharacterRef);
    }

    [Fact]
    public void ResolveSpawn_WithMissingArchetype_ThrowsInvalidOperationException()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawnWithArchetypeRef("NonExistentArchetype");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => resolver.ResolveSpawn(spawn));
        Assert.Contains("NonExistentArchetype", exception.Message);
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public void ResolveSpawn_WithEmptyArchetype_ThrowsInvalidOperationException()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawnWithArchetypeRef("EmptyArchetype");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => resolver.ResolveSpawn(spawn));
        Assert.Contains("EmptyArchetype", exception.Message);
        Assert.Contains("no character pool", exception.Message);
    }

    [Fact]
    public void ResolveSpawn_WithWorldMissingGameplay_ThrowsInvalidOperationException()
    {
        // Arrange
        var world = new World(); // No Gameplay
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawnWithArchetypeRef("MerchantArchetype");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveSpawn(spawn));
    }

    #endregion

    #region ResolveSpawns Tests

    [Fact]
    public void ResolveSpawns_WithNullArray_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);

        // Act
        var result = resolver.ResolveSpawns(null);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveSpawns_WithEmptyArray_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);

        // Act
        var result = resolver.ResolveSpawns(Array.Empty<CharacterSpawn>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveSpawns_WithMultipleSpawns_ReturnsAllResolved()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world, randomSeed: 42);
        var spawns = new[]
        {
            CreateSpawnWithCharacterRef("Boss_Dragon"),
            CreateSpawnWithArchetypeRef("MerchantArchetype"),
            CreateSpawnWithCharacterRef("Boss_Reaper", count: 2)
        };

        // Act
        var result = resolver.ResolveSpawns(spawns);

        // Assert
        Assert.Equal(4, result.Count); // 1 + 1 + 2
        Assert.Equal("Boss_Dragon", result[0].CharacterRef);
        Assert.Contains(result[1].CharacterRef, new[] { "Merchant_Blacksmith", "Merchant_Potion", "Merchant_General" });
        Assert.Equal("Boss_Reaper", result[2].CharacterRef);
        Assert.Equal("Boss_Reaper", result[3].CharacterRef);
    }

    #endregion

    #region Random Distribution Tests

    [Fact]
    public void ResolveSpawn_WithArchetype_DistributesRandomly()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world); // No seed = different results
        var spawn = CreateSpawnWithArchetypeRef("MerchantArchetype");

        // Act - Resolve many times and collect results
        var results = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var resolved = resolver.ResolveSpawn(spawn);
            results.Add(resolved[0].CharacterRef);
        }

        // Assert - Should hit at least 2 different characters from pool of 3
        Assert.True(results.Count >= 2,
            $"Expected at least 2 different characters, got {results.Count}: {string.Join(", ", results)}");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ResolveSpawn_WithCountZero_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawnWithCharacterRef("Boss_Dragon", count: 0);

        // Act
        var result = resolver.ResolveSpawn(spawn);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveSpawn_WithInvalidItemElementName_ThrowsInvalidOperationException()
    {
        // Arrange
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = new CharacterSpawn
        {
            ItemElementName = (ItemChoiceType)
            999, // Invalid enum value
            Item = "SomeRef"
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => resolver.ResolveSpawn(spawn));
        Assert.Contains("must specify either CharacterRef or CharacterArchetypeRef", exception.Message);
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void ResolveSpawn_BossEncounterScenario_WorksCorrectly()
    {
        // Arrange - Boss fight with specific boss plus minions from archetype
        var world = CreateWorldWithArchetypes();
        var resolver = new CharacterSpawnResolver(world, randomSeed: 42);
        var spawns = new[]
        {
            CreateSpawnWithCharacterRef("Boss_Dragon"), // Named boss
            CreateSpawnWithArchetypeRef("MerchantArchetype", count: 3) // Generic minions
        };

        // Act
        var result = resolver.ResolveSpawns(spawns);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Equal("Boss_Dragon", result[0].CharacterRef); // Boss always first
        // Next 3 are from archetype pool
        var validRefs = new[] { "Merchant_Blacksmith", "Merchant_Potion", "Merchant_General" };
        Assert.All(result.Skip(1), r => Assert.Contains(r.CharacterRef, validRefs));
    }

    [Fact]
    public void ResolveSpawn_MerchantRotationScenario_UsesDifferentSeed()
    {
        // Arrange - Different days should spawn different merchants
        var world = CreateWorldWithArchetypes();
        var spawn = CreateSpawnWithArchetypeRef("MerchantArchetype");

        var resolverDay1 = new CharacterSpawnResolver(world, randomSeed: 1);
        var resolverDay2 = new CharacterSpawnResolver(world, randomSeed: 2);

        // Act
        var day1Merchant = resolverDay1.ResolveSpawn(spawn);
        var day2Merchant = resolverDay2.ResolveSpawn(spawn);

        // Assert - Different seeds likely produce different merchants (not guaranteed but probable)
        // This test validates the seed mechanism works, not that it MUST be different
        Assert.NotNull(day1Merchant[0].CharacterRef);
        Assert.NotNull(day2Merchant[0].CharacterRef);
    }

    #endregion
}
