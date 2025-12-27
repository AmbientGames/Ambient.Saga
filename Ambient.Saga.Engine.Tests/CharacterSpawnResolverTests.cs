using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Unit tests for CharacterSpawnResolver which resolves character spawns from direct references.
/// </summary>
public class CharacterSpawnResolverTests
{
    private World CreateWorld()
    {
        return new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };
    }

    private CharacterSpawn CreateSpawn(string characterRef, int count = 1)
    {
        return new CharacterSpawn
        {
            CharacterRef = characterRef,
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
        var world = CreateWorld();

        // Act
        var resolver = new CharacterSpawnResolver(world);

        // Assert
        Assert.NotNull(resolver);
    }

    #endregion

    #region ResolveSpawn Tests

    [Fact]
    public void ResolveSpawn_WithNullSpawn_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateWorld();
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
        var world = CreateWorld();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawn("Boss_Dragon");

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
        var world = CreateWorld();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawn("Boss_Dragon", count: 3);

        // Act
        var result = resolver.ResolveSpawn(spawn);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal("Boss_Dragon", r.CharacterRef));
        Assert.All(result, r => Assert.Equal(1, r.Count)); // Each spawn is individual
    }

    [Fact]
    public void ResolveSpawn_WithEmptyCharacterRef_ThrowsInvalidOperationException()
    {
        // Arrange
        var world = CreateWorld();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = new CharacterSpawn { CharacterRef = "" };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => resolver.ResolveSpawn(spawn));
        Assert.Contains("CharacterRef", exception.Message);
    }

    [Fact]
    public void ResolveSpawn_WithNullCharacterRef_ThrowsInvalidOperationException()
    {
        // Arrange
        var world = CreateWorld();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = new CharacterSpawn { CharacterRef = null! };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => resolver.ResolveSpawn(spawn));
        Assert.Contains("CharacterRef", exception.Message);
    }

    #endregion

    #region ResolveSpawns Tests

    [Fact]
    public void ResolveSpawns_WithNullArray_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateWorld();
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
        var world = CreateWorld();
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
        var world = CreateWorld();
        var resolver = new CharacterSpawnResolver(world);
        var spawns = new[]
        {
            CreateSpawn("Boss_Dragon"),
            CreateSpawn("Merchant_Blacksmith"),
            CreateSpawn("Boss_Reaper", count: 2)
        };

        // Act
        var result = resolver.ResolveSpawns(spawns);

        // Assert
        Assert.Equal(4, result.Count); // 1 + 1 + 2
        Assert.Equal("Boss_Dragon", result[0].CharacterRef);
        Assert.Equal("Merchant_Blacksmith", result[1].CharacterRef);
        Assert.Equal("Boss_Reaper", result[2].CharacterRef);
        Assert.Equal("Boss_Reaper", result[3].CharacterRef);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ResolveSpawn_WithCountZero_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateWorld();
        var resolver = new CharacterSpawnResolver(world);
        var spawn = CreateSpawn("Boss_Dragon", count: 0);

        // Act
        var result = resolver.ResolveSpawn(spawn);

        // Assert
        Assert.Empty(result);
    }

    #endregion
}
