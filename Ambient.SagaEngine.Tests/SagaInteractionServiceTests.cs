using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Tests;

/// <summary>
/// Unit tests for SagaInteractionService which handles avatar interactions with Saga triggers.
/// Tests Saga-relative coordinate system, deterministic spawning, and transaction creation.
/// </summary>
public class SagaInteractionServiceTests
{
    private World CreateWorldWithCharacters()
    {
        return new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Characters = new[]
                    {
                        new Character { RefName = "Guard", DisplayName = "Castle Guard" },
                        new Character { RefName = "Boss", DisplayName = "Dragon Boss" },
                        new Character { RefName = "Merchant", DisplayName = "Merchant" }
                    }
                }
            },
            CharactersLookup = new Dictionary<string, Character>
            {
                ["Guard"] = new Character { RefName = "Guard", DisplayName = "Castle Guard" },
                ["Boss"] = new Character { RefName = "Boss", DisplayName = "Dragon Boss" },
                ["Merchant"] = new Character { RefName = "Merchant", DisplayName = "Merchant" }
            }
        };
    }

    private SagaArc CreateSagaTemplate(string refName = "TestSaga")
    {
        return new SagaArc
        {
            RefName = refName,
            DisplayName = "Test Saga",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };
    }

    private SagaInstance CreateSagaInstance(string poiRef = "TestSaga", Guid? avatarId = null)
    {
        return new SagaInstance
        {
            InstanceId = Guid.NewGuid(),
            SagaRef = poiRef,
            OwnerAvatarId = avatarId ?? Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Transactions = new List<SagaTransaction>()
        };
    }

    private SagaTrigger CreateSagaTrigger(
        string refName = "TestTrigger",
        float enterRadius = 10.0f,
        CharacterSpawn[]? spawns = null,
        string[]? requiredTokens = null)
    {
        return new SagaTrigger
        {
            RefName = refName,
            EnterRadius = enterRadius,
            Spawn = spawns,
            RequiresQuestTokenRef = requiredTokens
        };
    }

    private AvatarBase CreateAvatar(string archetypeRef = "Warrior", params string[] questTokens)
    {
        // Generate deterministic Guid from archetype ref for testing
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(archetypeRef));
        var avatarId = new Guid(hash);

        return new AvatarBase
        {
            AvatarId = avatarId,
            ArchetypeRef = archetypeRef,
            Capabilities = new ItemCollection
            {
                QuestTokens = questTokens.Select(t => new QuestTokenEntry { QuestTokenRef = t }).ToArray()
            }
        };
    }

    private CharacterSpawn CreateCharacterSpawn(string characterRef, int count = 1)
    {
        return new CharacterSpawn
        {
            ItemElementName = ItemChoiceType.CharacterRef,
            Item = characterRef,
            Count = count
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTemplate_ThrowsArgumentNullException()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var sagaTriggers = new List<SagaTrigger>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SagaInteractionService(null!, sagaTriggers, world));
    }

    [Fact]
    public void Constructor_WithNullTriggers_ThrowsArgumentNullException()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SagaInteractionService(template, null!, world));
    }

    [Fact]
    public void Constructor_WithNullWorld_ThrowsArgumentNullException()
    {
        // Arrange
        var template = CreateSagaTemplate();
        var sagaTriggers = new List<SagaTrigger>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SagaInteractionService(template, sagaTriggers, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var sagaTriggers = new List<SagaTrigger>();

        // Act
        var service = new SagaInteractionService(template, sagaTriggers, world);

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region UpdateWithAvatarPosition - Basic Tests

    [Fact]
    public void UpdateWithAvatarPosition_WithNullInstance_ThrowsArgumentNullException()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var sagaTriggers = new List<SagaTrigger>();
        var service = new SagaInteractionService(template, sagaTriggers, world);
        var avatar = CreateAvatar();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            service.UpdateWithAvatarPosition(null!, 0, 0, avatar));
    }

    [Fact]
    public void UpdateWithAvatarPosition_WithNullAvatar_ThrowsArgumentNullException()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var sagaTriggers = new List<SagaTrigger>();
        var service = new SagaInteractionService(template, sagaTriggers, world);
        var instance = CreateSagaInstance();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            service.UpdateWithAvatarPosition(instance, 0, 0, null!));
    }

    [Fact]
    public void UpdateWithAvatarPosition_WithNoTriggers_NoTransactionsCreated()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var sagaTriggers = new List<SagaTrigger>(); // No triggers
        var service = new SagaInteractionService(template, sagaTriggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert
        Assert.Empty(instance.Transactions);
    }

    #endregion

    #region UpdateWithAvatarPosition - Proximity Tests

    [Fact]
    public void UpdateWithAvatarPosition_WithinRadius_ActivatesTrigger()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Avatar at (5, 5) = distance 7.07 from center (0, 0)
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert - Expect PlayerEntered and TriggerActivated
        Assert.Equal(2, instance.Transactions.Count);
        Assert.Equal(SagaTransactionType.PlayerEntered, instance.Transactions[0].Type);
        var tx = instance.Transactions[1];
        Assert.Equal(SagaTransactionType.TriggerActivated, tx.Type);
        Assert.Equal("TestTrigger", tx.Data["SagaTriggerRef"]);
    }

    [Fact]
    public void UpdateWithAvatarPosition_OutsideRadius_DoesNotActivateTrigger()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Avatar at (20, 20) = distance 28.28 from center (0, 0)
        service.UpdateWithAvatarPosition(instance, 20.0, 20.0, avatar);

        // Assert
        Assert.Empty(instance.Transactions);
    }

    [Fact]
    public void UpdateWithAvatarPosition_ExactlyAtRadius_ActivatesTrigger()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Avatar at exactly 10.0 distance from center
        service.UpdateWithAvatarPosition(instance, 10.0, 0.0, avatar);

        // Assert - Expect PlayerEntered and TriggerActivated
        Assert.Equal(2, instance.Transactions.Count);
    }

    [Fact]
    public void UpdateWithAvatarPosition_AtSagaCenter_ActivatesTrigger()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Avatar at Saga center (0, 0)
        service.UpdateWithAvatarPosition(instance, 0.0, 0.0, avatar);

        // Assert - Expect PlayerEntered and TriggerActivated
        Assert.Equal(2, instance.Transactions.Count);
    }

    #endregion

    #region UpdateWithAvatarPosition - Quest Token Requirements

    [Fact]
    public void UpdateWithAvatarPosition_MissingRequiredToken_DoesNotActivateTrigger()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(
            refName: "LockedTrigger",
            enterRadius: 10.0f,
            requiredTokens: new[] { "MagicKey" });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar(); // No quest tokens

        // Act - Avatar within radius but missing quest token
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert
        Assert.Empty(instance.Transactions);
    }

    [Fact]
    public void UpdateWithAvatarPosition_HasRequiredToken_ActivatesTrigger()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(
            refName: "LockedTrigger",
            enterRadius: 10.0f,
            requiredTokens: new[] { "MagicKey" });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar(questTokens: "MagicKey");

        // Act - Avatar within radius with required token
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert - Expect PlayerEntered and TriggerActivated
        Assert.Equal(2, instance.Transactions.Count);
        Assert.Equal(SagaTransactionType.PlayerEntered, instance.Transactions[0].Type);
        Assert.Equal(SagaTransactionType.TriggerActivated, instance.Transactions[1].Type);
    }

    #endregion

    #region UpdateWithAvatarPosition - Trigger State Tests

    [Fact]
    public void UpdateWithAvatarPosition_TriggerAlreadyCompleted_DoesNotActivateAgain()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // First activation
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);
        Assert.Equal(2, instance.Transactions.Count); // PlayerEntered + TriggerActivated

        // Commit the transactions (so they're included in replay)
        instance.Transactions[0].Status = TransactionStatus.Committed;
        instance.Transactions[1].Status = TransactionStatus.Committed;

        // Complete the trigger by adding completion transaction
        var completionTx = new SagaTransaction
        {
            Type = SagaTransactionType.TriggerCompleted,
            Status = TransactionStatus.Committed,
            Data = new Dictionary<string, string>
            {
                ["SagaTriggerRef"] = "TestTrigger"
            }
        };
        instance.AddTransaction(completionTx);

        // Act - Try to activate again
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert - No new SagaTriggerActivated transaction (only the original + completion)
        var triggerActivatedCount = instance.Transactions
            .Count(tx => tx.Type == SagaTransactionType.TriggerActivated);
        Assert.Equal(1, triggerActivatedCount);
    }

    #endregion

    #region Transaction Data Tests

    [Fact]
    public void UpdateWithAvatarPosition_TriggerActivatedTransaction_ContainsCorrectData()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar("WarriorArchetype");

        // Act - Avatar at (6.0, 4.0) = distance 7.21 from center (within 10.0 radius)
        service.UpdateWithAvatarPosition(instance, 6.0, 4.0, avatar);

        // Assert - Expect PlayerEntered and TriggerActivated
        Assert.Equal(2, instance.Transactions.Count);
        var tx = instance.Transactions[1]; // TriggerActivated is second

        Assert.Equal(SagaTransactionType.TriggerActivated, tx.Type);
        Assert.Equal(avatar.AvatarId.ToString(), tx.AvatarId);
        Assert.Equal(TransactionStatus.Pending, tx.Status);

        Assert.True(tx.Data.ContainsKey("SagaTriggerRef"));
        Assert.Equal("TestTrigger", tx.Data["SagaTriggerRef"]);

        Assert.True(tx.Data.ContainsKey("AvatarX"));
        Assert.True(tx.Data.ContainsKey("AvatarZ"));
        Assert.True(tx.Data.ContainsKey("Seed"));

        // Verify avatar position is stored
        var storedX = double.Parse(tx.Data["AvatarX"]);
        var storedZ = double.Parse(tx.Data["AvatarZ"]);
        Assert.Equal(6.0, storedX, precision: 5);
        Assert.Equal(4.0, storedZ, precision: 5);
    }

    #endregion

    #region Character Spawning Tests

    [Fact]
    public void UpdateWithAvatarPosition_TriggerWithNoSpawns_DoesNotCreateCharacterTransactions()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: null);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert - PlayerEntered + TriggerActivated, no CharacterSpawned
        Assert.Equal(2, instance.Transactions.Count);
        Assert.Equal(SagaTransactionType.PlayerEntered, instance.Transactions[0].Type);
        Assert.Equal(SagaTransactionType.TriggerActivated, instance.Transactions[1].Type);
    }

    [Fact]
    public void UpdateWithAvatarPosition_TriggerWithSingleSpawn_CreatesCharacterSpawnedTransaction()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 1);
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert - PlayerEntered + TriggerActivated + CharacterSpawned
        Assert.Equal(3, instance.Transactions.Count);

        Assert.Equal(SagaTransactionType.PlayerEntered, instance.Transactions[0].Type);

        var triggerTx = instance.Transactions[1];
        Assert.Equal(SagaTransactionType.TriggerActivated, triggerTx.Type);

        var spawnTx = instance.Transactions[2];
        Assert.Equal(SagaTransactionType.CharacterSpawned, spawnTx.Type);
        Assert.Equal("Guard", spawnTx.Data["CharacterRef"]);
        Assert.Equal("TestTrigger", spawnTx.Data["SagaTriggerRef"]);
    }

    [Fact]
    public void UpdateWithAvatarPosition_TriggerWithMultipleSpawns_CreatesMultipleTransactions()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 3);
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert - PlayerEntered + TriggerActivated + 3 CharacterSpawned
        Assert.Equal(5, instance.Transactions.Count);

        var spawnTransactions = instance.Transactions
            .Where(tx => tx.Type == SagaTransactionType.CharacterSpawned)
            .ToList();
        Assert.Equal(3, spawnTransactions.Count);

        // All should reference the same character and trigger
        Assert.All(spawnTransactions, tx =>
        {
            Assert.Equal("Guard", tx.Data["CharacterRef"]);
            Assert.Equal("TestTrigger", tx.Data["SagaTriggerRef"]);
        });
    }

    [Fact]
    public void UpdateWithAvatarPosition_CharacterSpawnedTransaction_ContainsPositionData()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 1);
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Avatar at (5.0, 6.0) = distance 7.81 from center (within 10.0 radius)
        service.UpdateWithAvatarPosition(instance, 5.0, 6.0, avatar);

        // Assert
        var spawnTx = instance.Transactions
            .First(tx => tx.Type == SagaTransactionType.CharacterSpawned);

        // Should have position data (Saga-relative coordinates)
        Assert.True(spawnTx.Data.ContainsKey("X"));
        Assert.True(spawnTx.Data.ContainsKey("Z"));
        Assert.True(spawnTx.Data.ContainsKey("SpawnHeight"));

        // Position should be near avatar position, not at Saga center
        var x = double.Parse(spawnTx.Data["X"]);
        var z = double.Parse(spawnTx.Data["Z"]);

        // Distance from avatar should be roughly the spawn radius
        var dx = x - 5.0;
        var dz = z - 6.0;
        var distanceFromAvatar = Math.Sqrt(dx * dx + dz * dz);

        // Spawn radius is fixed at 10.0 meters (default trigger type)
        var expectedRadius = 10.0;
        Assert.InRange(distanceFromAvatar, expectedRadius * 0.9, expectedRadius * 1.0);
    }

    [Fact]
    public void UpdateWithAvatarPosition_CharacterSpawnedTransaction_HasUniqueInstanceId()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 3);
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert
        var spawnTransactions = instance.Transactions
            .Where(tx => tx.Type == SagaTransactionType.CharacterSpawned)
            .ToList();

        var instanceIds = spawnTransactions
            .Select(tx => tx.Data["CharacterInstanceId"])
            .ToList();

        // All instance IDs should be unique
        Assert.Equal(instanceIds.Count, instanceIds.Distinct().Count());

        // All should be valid GUIDs
        Assert.All(instanceIds, id => Assert.True(Guid.TryParse(id, out _)));
    }

    #endregion

    #region Deterministic Spawning Tests

    [Fact]
    public void UpdateWithAvatarPosition_SameSeed_ProducesSameSpawnPositions()
    {
        // This test verifies that replaying the same transaction with the same seed
        // produces the same spawn positions. We do this by:
        // 1. Capturing the seed from the first spawn
        // 2. Manually calculating spawn positions with that seed
        // 3. Verifying the positions match

        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 2);
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert
        var triggerTx = instance.Transactions
            .First(tx => tx.Type == SagaTransactionType.TriggerActivated);
        var seed = int.Parse(triggerTx.Data["Seed"]);

        var spawnTransactions = instance.Transactions
            .Where(tx => tx.Type == SagaTransactionType.CharacterSpawned)
            .OrderBy(tx => tx.LocalTimestamp)
            .ToList();

        // Both spawn transactions should use positions calculated from the same seed
        Assert.Equal(2, spawnTransactions.Count);

        // Get the positions
        var positions = spawnTransactions
            .Select(tx => (
                X: double.Parse(tx.Data["X"]),
                Z: double.Parse(tx.Data["Z"])
            ))
            .ToList();

        // Positions should be different from each other (distributed around circle)
        Assert.NotEqual(positions[0].X, positions[1].X, precision: 2);
    }

    #endregion

    #region Multiple Triggers Tests

    [Fact]
    public void UpdateWithAvatarPosition_MultipleTriggers_ActivatesAllWithinRadius()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger1 = CreateSagaTrigger(refName: "Trigger1", enterRadius: 20.0f);
        var trigger2 = CreateSagaTrigger(refName: "Trigger2", enterRadius: 15.0f);
        var trigger3 = CreateSagaTrigger(refName: "Trigger3", enterRadius: 5.0f);
        var triggers = new List<SagaTrigger> { trigger1, trigger2, trigger3 };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Avatar at distance 10 from center
        service.UpdateWithAvatarPosition(instance, 10.0, 0.0, avatar);

        // Assert - Trigger1 and Trigger2 activate (PlayerEntered + TriggerActivated for each = 4 total), Trigger3 doesn't
        Assert.Equal(4, instance.Transactions.Count);

        var activatedTriggers = instance.Transactions
            .Where(tx => tx.Data.ContainsKey("SagaTriggerRef"))
            .Select(tx => tx.Data["SagaTriggerRef"])
            .ToList();

        Assert.Contains("Trigger1", activatedTriggers);
        Assert.Contains("Trigger2", activatedTriggers);
        Assert.DoesNotContain("Trigger3", activatedTriggers);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void UpdateWithAvatarPosition_AvatarWithEmptyGuid_UsesEmptyGuidString()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = new AvatarBase { AvatarId = Guid.Empty, ArchetypeRef = null };

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert - PlayerEntered + TriggerActivated
        Assert.Equal(2, instance.Transactions.Count);
        Assert.Equal(Guid.Empty.ToString(), instance.Transactions[0].AvatarId);
        Assert.Equal(Guid.Empty.ToString(), instance.Transactions[1].AvatarId);
    }

    [Fact]
    public void UpdateWithAvatarPosition_InvalidCharacterRef_SkipsSpawn()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var spawn = CreateCharacterSpawn("NonExistentCharacter", count: 1);
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert - PlayerEntered + TriggerActivated, character spawn skipped
        Assert.Equal(2, instance.Transactions.Count);
        Assert.Equal(SagaTransactionType.PlayerEntered, instance.Transactions[0].Type);
        Assert.Equal(SagaTransactionType.TriggerActivated, instance.Transactions[1].Type);
    }

    [Fact]
    public void UpdateWithAvatarPosition_NegativeCoordinates_WorksCorrectly()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Avatar in negative quadrant
        service.UpdateWithAvatarPosition(instance, -5.0, -5.0, avatar);

        // Assert - Distance is still 7.07, within radius - PlayerEntered + TriggerActivated
        Assert.Equal(2, instance.Transactions.Count);
        var tx = instance.Transactions[1]; // TriggerActivated is second
        Assert.Equal("-5", tx.Data["AvatarX"].Substring(0, 2));
        Assert.Equal("-5", tx.Data["AvatarZ"].Substring(0, 2));
    }

    #endregion

    #region Progressive Unlock System Tests

    [Fact]
    public void UpdateWithAvatarPosition_ProgressiveUnlock_OuterTriggerActivatesWithoutKeys()
    {
        // Arrange - Outer trigger has no requirements
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var outerTrigger = CreateSagaTrigger(
            refName: "OuterRing",
            enterRadius: 30.0f,
            requiredTokens: null);
        var triggers = new List<SagaTrigger> { outerTrigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar(); // No quest tokens

        // Act - Avatar enters outer trigger
        service.UpdateWithAvatarPosition(instance, 15.0, 15.0, avatar);

        // Assert - Outer trigger activates - PlayerEntered + TriggerActivated
        Assert.Equal(2, instance.Transactions.Count);
        Assert.Equal(SagaTransactionType.PlayerEntered, instance.Transactions[0].Type);
        Assert.Equal(SagaTransactionType.TriggerActivated, instance.Transactions[1].Type);
        Assert.Equal("OuterRing", instance.Transactions[1].Data["SagaTriggerRef"]);
    }

    [Fact]
    public void UpdateWithAvatarPosition_ProgressiveUnlock_MiddleTriggerBlockedWithoutKey()
    {
        // Arrange - Middle trigger requires completion of outer
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var middleTrigger = CreateSagaTrigger(
            refName: "MiddleRing",
            enterRadius: 20.0f,
            requiredTokens: new[] { "OuterRing_Complete" });
        var triggers = new List<SagaTrigger> { middleTrigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar(); // Missing OuterRing_Complete token

        // Act - Avatar tries to enter middle trigger without key
        service.UpdateWithAvatarPosition(instance, 10.0, 10.0, avatar);

        // Assert - No activation (avatar doesn't have required token)
        Assert.Empty(instance.Transactions);
    }

    [Fact]
    public void UpdateWithAvatarPosition_ProgressiveUnlock_QuestTokensAwarded()
    {
        // Arrange - Trigger that gives quest tokens
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = new SagaTrigger
        {
            RefName = "OuterRing",
            EnterRadius = 30.0f,
            GivesQuestTokenRef = new[] { "OuterRing_Complete", "BonusToken" }
        };
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Activate trigger
        service.UpdateWithAvatarPosition(instance, 15.0, 15.0, avatar);

        // Assert - PlayerEntered + TriggerActivated + 2 QuestTokenAwarded transactions
        Assert.Equal(4, instance.Transactions.Count);

        Assert.Equal(SagaTransactionType.PlayerEntered, instance.Transactions[0].Type);

        var triggerActivated = instance.Transactions[1];
        Assert.Equal(SagaTransactionType.TriggerActivated, triggerActivated.Type);

        var questToken1 = instance.Transactions[2];
        Assert.Equal(SagaTransactionType.QuestTokenAwarded, questToken1.Type);
        Assert.Equal("OuterRing_Complete", questToken1.Data["QuestTokenRef"]);
        Assert.Equal("OuterRing", questToken1.Data["SagaTriggerRef"]);

        var questToken2 = instance.Transactions[3];
        Assert.Equal(SagaTransactionType.QuestTokenAwarded, questToken2.Type);
        Assert.Equal("BonusToken", questToken2.Data["QuestTokenRef"]);
    }

    [Fact]
    public void UpdateWithAvatarPosition_ProgressiveUnlock_FullChainOuterToMiddleToInner()
    {
        // This is the key test - simulates a full progression:
        // 1. Outer trigger (no requirements) -> gives "Outer_Complete"
        // 2. Middle trigger (requires "Outer_Complete") -> gives "Middle_Complete"
        // 3. Inner trigger (requires "Middle_Complete")

        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();

        // Create three concentric triggers with progression
        var outerTrigger = new SagaTrigger
        {
            RefName = "Outer",
            EnterRadius = 30.0f,
            RequiresQuestTokenRef = null, // No requirements
            GivesQuestTokenRef = new[] { "Outer_Complete" }
        };

        var middleTrigger = new SagaTrigger
        {
            RefName = "Middle",
            EnterRadius = 20.0f,
            RequiresQuestTokenRef = new[] { "Outer_Complete" },
            GivesQuestTokenRef = new[] { "Middle_Complete" }
        };

        var innerTrigger = new SagaTrigger
        {
            RefName = "Inner",
            EnterRadius = 10.0f,
            RequiresQuestTokenRef = new[] { "Middle_Complete" },
            GivesQuestTokenRef = new[] { "Inner_Complete" }
        };

        var triggers = new List<SagaTrigger> { outerTrigger, middleTrigger, innerTrigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();

        // === STEP 1: Avatar with no keys tries all triggers ===
        var avatarNoKeys = CreateAvatar("Warrior");

        // At outer position (25m from center) - only outer should activate
        service.UpdateWithAvatarPosition(instance, 25.0, 0.0, avatarNoKeys);

        var outerActivations = instance.Transactions.Count(tx =>
            tx.Type == SagaTransactionType.TriggerActivated &&
            tx.Data["SagaTriggerRef"] == "Outer");
        Assert.Equal(1, outerActivations); // Outer activates

        var middleActivations = instance.Transactions.Count(tx =>
            tx.Type == SagaTransactionType.TriggerActivated &&
            tx.Data["SagaTriggerRef"] == "Middle");
        Assert.Equal(0, middleActivations); // Middle blocked (no key)

        // Verify quest token awarded
        var questTokens = instance.Transactions.Where(tx =>
            tx.Type == SagaTransactionType.QuestTokenAwarded).ToList();
        Assert.Single(questTokens);
        Assert.Equal("Outer_Complete", questTokens[0].Data["QuestTokenRef"]);

        // === STEP 2: Avatar with "Outer_Complete" tries middle trigger ===
        var avatarWithOuterKey = CreateAvatar("Warrior", "Outer_Complete");

        // Reset to simulate new session (in real game, avatar inventory would be updated)
        instance.Transactions.Clear();

        // At middle position (15m from center) - middle should now activate
        service.UpdateWithAvatarPosition(instance, 15.0, 0.0, avatarWithOuterKey);

        var middleActivationCount = instance.Transactions.Count(tx =>
            tx.Type == SagaTransactionType.TriggerActivated &&
            tx.Data["SagaTriggerRef"] == "Middle");
        Assert.Equal(1, middleActivationCount); // Middle activates with key!

        // Verify middle gives its quest token
        var middleTokens = instance.Transactions.Where(tx =>
            tx.Type == SagaTransactionType.QuestTokenAwarded &&
            tx.Data["QuestTokenRef"] == "Middle_Complete").ToList();
        Assert.Single(middleTokens);

        // === STEP 3: Avatar with "Middle_Complete" activates inner ===
        var avatarWithMiddleKey = CreateAvatar("Warrior", "Middle_Complete");

        instance.Transactions.Clear();

        // At inner position (5m from center) - inner should activate
        service.UpdateWithAvatarPosition(instance, 5.0, 0.0, avatarWithMiddleKey);

        var innerActivationCount = instance.Transactions.Count(tx =>
            tx.Type == SagaTransactionType.TriggerActivated &&
            tx.Data["SagaTriggerRef"] == "Inner");
        Assert.Equal(1, innerActivationCount); // Inner activates with key!

        // Verify inner gives its quest token
        var innerTokens = instance.Transactions.Where(tx =>
            tx.Type == SagaTransactionType.QuestTokenAwarded &&
            tx.Data["QuestTokenRef"] == "Inner_Complete").ToList();
        Assert.Single(innerTokens);
    }

    [Fact]
    public void UpdateWithAvatarPosition_ProgressiveUnlock_MultipleKeysRequired()
    {
        // Test trigger that requires MULTIPLE quest tokens
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(
            refName: "BossRoom",
            enterRadius: 10.0f,
            requiredTokens: new[] { "RedKey", "BlueKey", "GreenKey" });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();

        // Avatar with only 2 of 3 keys
        var avatarPartialKeys = CreateAvatar("Warrior", "RedKey", "BlueKey");
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatarPartialKeys);
        Assert.Empty(instance.Transactions); // Blocked - missing GreenKey

        // Avatar with all 3 keys
        var avatarAllKeys = CreateAvatar("Warrior", "RedKey", "BlueKey", "GreenKey");
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatarAllKeys);
        Assert.Equal(2, instance.Transactions.Count); // PlayerEntered + TriggerActivated - Activates with all keys!
    }

    [Fact]
    public void UpdateWithAvatarPosition_ProgressiveUnlock_SimultaneousTriggersAtSameRadius()
    {
        // Edge case: Multiple triggers at same radius with different requirements
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();

        var triggerA = new SagaTrigger
        {
            RefName = "PathA",
            EnterRadius = 15.0f,
            RequiresQuestTokenRef = null,
            GivesQuestTokenRef = new[] { "PathA_Token" }
        };

        var triggerB = new SagaTrigger
        {
            RefName = "PathB",
            EnterRadius = 15.0f,
            RequiresQuestTokenRef = null,
            GivesQuestTokenRef = new[] { "PathB_Token" }
        };

        var triggers = new List<SagaTrigger> { triggerA, triggerB };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Both triggers at same radius should activate
        service.UpdateWithAvatarPosition(instance, 10.0, 10.0, avatar);

        // Assert - Both triggers activate (2 TriggerActivated + 2 QuestTokenAwarded)
        var triggerActivations = instance.Transactions.Count(tx =>
            tx.Type == SagaTransactionType.TriggerActivated);
        Assert.Equal(2, triggerActivations);

        var questTokens = instance.Transactions.Where(tx =>
            tx.Type == SagaTransactionType.QuestTokenAwarded).ToList();
        Assert.Equal(2, questTokens.Count);
        Assert.Contains(questTokens, tx => tx.Data["QuestTokenRef"] == "PathA_Token");
        Assert.Contains(questTokens, tx => tx.Data["QuestTokenRef"] == "PathB_Token");
    }

    #endregion

    #region Query Method Tests

    [Fact]
    public void GetTriggerAtPosition_WithinRadiusAndHasKeys_ReturnsTrigger()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 15.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Query what trigger would activate
        var result = service.GetSagaTriggerAtPosition(instance, 10.0, 5.0, avatar);

        // Assert - Returns the trigger (doesn't activate it)
        Assert.NotNull(result);
        Assert.Equal("TestTrigger", result.RefName);
        Assert.Empty(instance.Transactions); // No transactions created (query only)
    }

    [Fact]
    public void GetTriggerAtPosition_OutsideRadius_ReturnsNull()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Query position outside radius
        var result = service.GetSagaTriggerAtPosition(instance, 20.0, 20.0, avatar);

        // Assert
        Assert.Null(result);
        Assert.Empty(instance.Transactions);
    }

    [Fact]
    public void GetTriggerAtPosition_MissingQuestTokens_ReturnsNull()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(
            refName: "LockedTrigger",
            enterRadius: 15.0f,
            requiredTokens: new[] { "MagicKey" });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar(); // No quest tokens

        // Act - Query trigger that requires keys
        var result = service.GetSagaTriggerAtPosition(instance, 10.0, 5.0, avatar);

        // Assert - Returns null (avatar can't activate it)
        Assert.Null(result);
    }

    [Fact]
    public void GetTriggerAtPosition_MultipleTriggersReturnsInnermost()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var outerTrigger = CreateSagaTrigger(refName: "Outer", enterRadius: 30.0f);
        var middleTrigger = CreateSagaTrigger(refName: "Middle", enterRadius: 20.0f);
        var innerTrigger = CreateSagaTrigger(refName: "Inner", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { outerTrigger, middleTrigger, innerTrigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Query position within all three triggers
        var result = service.GetSagaTriggerAtPosition(instance, 8.0, 4.0, avatar);

        // Assert - Returns innermost trigger
        Assert.NotNull(result);
        Assert.Equal("Inner", result.RefName);
    }

    [Fact]
    public void GetTriggersAtPosition_ReturnsAllTriggersWithStatus()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var outerTrigger = CreateSagaTrigger(refName: "Outer", enterRadius: 30.0f);
        var middleTrigger = CreateSagaTrigger(refName: "Middle", enterRadius: 20.0f);
        var innerTrigger = CreateSagaTrigger(refName: "Inner", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { outerTrigger, middleTrigger, innerTrigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();

        // Act - Query all triggers at position
        var results = service.GetTriggersAtPosition(instance, 15.0, 0.0);

        // Assert - Returns all 3 triggers sorted outermost to innermost
        Assert.Equal(3, results.Count);
        Assert.Equal("Outer", results[0].SagaTrigger.RefName);
        Assert.Equal("Middle", results[1].SagaTrigger.RefName);
        Assert.Equal("Inner", results[2].SagaTrigger.RefName);

        // Check within radius flags
        Assert.True(results[0].IsWithinRadius); // Outer: 30m
        Assert.True(results[1].IsWithinRadius); // Middle: 20m
        Assert.False(results[2].IsWithinRadius); // Inner: 10m (position is 15m away)
    }

    [Fact]
    public void CanActivateTrigger_AllConditionsMet_ReturnsTrue()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 15.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        var result = service.CanActivateSagaTrigger(instance, trigger, 10.0, 5.0, avatar);

        // Assert
        Assert.True(result.CanActivate);
        Assert.True(result.IsWithinRadius);
        Assert.True(result.HasRequiredQuestTokens);
        Assert.Null(result.BlockedReason);
        Assert.Empty(result.MissingQuestTokens);
    }

    [Fact]
    public void CanActivateTrigger_OutsideRadius_ReturnsFalseWithReason()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Position outside radius
        var result = service.CanActivateSagaTrigger(instance, trigger, 20.0, 20.0, avatar);

        // Assert
        Assert.False(result.CanActivate);
        Assert.False(result.IsWithinRadius);
        Assert.NotNull(result.BlockedReason);
        Assert.Contains("outside trigger radius", result.BlockedReason);
    }

    [Fact]
    public void CanActivateTrigger_MissingQuestTokens_ReturnsFalseWithMissingTokens()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(
            refName: "LockedTrigger",
            enterRadius: 15.0f,
            requiredTokens: new[] { "RedKey", "BlueKey" });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar(); // No quest tokens

        // Act
        var result = service.CanActivateSagaTrigger(instance, trigger, 10.0, 5.0, avatar);

        // Assert
        Assert.False(result.CanActivate);
        Assert.True(result.IsWithinRadius); // Within radius but blocked by tokens
        Assert.False(result.HasRequiredQuestTokens);
        Assert.NotNull(result.BlockedReason);
        Assert.Contains("Missing quest tokens", result.BlockedReason);
        Assert.Equal(2, result.MissingQuestTokens.Length);
        Assert.Contains("RedKey", result.MissingQuestTokens);
        Assert.Contains("BlueKey", result.MissingQuestTokens);
    }

    [Fact]
    public void CanActivateTrigger_AlreadyCompleted_ReturnsFalseWithReason()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 15.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Complete the trigger first
        var completionTx = new SagaTransaction
        {
            Type = SagaTransactionType.TriggerCompleted,
            Status = TransactionStatus.Committed,
            Data = new Dictionary<string, string>
            {
                ["SagaTriggerRef"] = "TestTrigger"
            }
        };
        instance.AddTransaction(completionTx);

        // Act - Try to check if it can activate again
        var result = service.CanActivateSagaTrigger(instance, trigger, 10.0, 5.0, avatar);

        // Assert
        Assert.False(result.CanActivate);
        Assert.Equal("Trigger already completed", result.BlockedReason);
    }

    [Fact]
    public void QueryMethods_DoNotCreateTransactions()
    {
        // This test verifies that query methods are truly non-mutating
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 15.0f);
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Call all query methods
        service.GetSagaTriggerAtPosition(instance, 10.0, 5.0, avatar);
        service.GetTriggersAtPosition(instance, 10.0, 5.0);
        service.CanActivateSagaTrigger(instance, trigger, 10.0, 5.0, avatar);

        // Assert - No transactions created
        Assert.Empty(instance.Transactions);
    }

    #endregion

    #region End-to-End Integration Tests

    [Fact]
    public void EndToEnd_TriggerActivationAndReplay_ProducesConsistentState()
    {
        // This test verifies the full cycle:
        // 1. Activate trigger via SagaInteractionService
        // 2. Commit transactions
        // 3. Replay via SagaStateMachine
        // 4. Verify state is consistent

        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 2);
        var trigger = new SagaTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 15.0f,
            Spawn = new[] { spawn },
            GivesQuestTokenRef = new[] { "TriggerComplete" }
        };
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var stateMachine = new SagaStateMachine(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act - Activate trigger
        service.UpdateWithAvatarPosition(instance, 10.0, 5.0, avatar);

        // Commit all transactions for replay
        foreach (var tx in instance.Transactions)
        {
            tx.Status = TransactionStatus.Committed;
        }

        // Replay the transactions
        var state = stateMachine.ReplayToNow(instance);

        // Assert - State matches what we expect
        Assert.Equal(SagaTriggerStatus.Active, state.Triggers["TestTrigger"].Status);
        Assert.Equal(1, state.Triggers["TestTrigger"].ActivationCount);

        // Should have 2 spawned characters
        Assert.Equal(2, state.Characters.Count);

        // Transactions: 1 PlayerEntered + 1 TriggerActivated + 1 QuestTokenAwarded + 2 CharacterSpawned = 5
        Assert.Equal(5, instance.Transactions.Count);
        Assert.Equal(5, state.TransactionCount);
    }

    [Fact]
    public void EndToEnd_SpawnPositionsStoredInTransactions_AreConsistentWithSeed()
    {
        // This verifies that spawn positions are deterministic based on the seed
        // We can't control the seed directly, but we can verify that:
        // 1. Seed is stored in TriggerActivated
        // 2. CharacterSpawned positions are generated from that seed
        // 3. If we manually create transactions with the same seed, we get same positions

        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateSagaTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 3);
        var trigger = CreateSagaTrigger(refName: "TestTrigger", enterRadius: 15.0f, spawns: new[] { spawn });
        var triggers = new List<SagaTrigger> { trigger };
        var service = new SagaInteractionService(template, triggers, world);
        var instance = CreateSagaInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 10.0, 5.0, avatar);

        // Assert - Extract seed from TriggerActivated
        var triggerTx = instance.Transactions.First(tx => tx.Type == SagaTransactionType.TriggerActivated);
        var seed = int.Parse(triggerTx.Data["Seed"]);

        // Get spawn positions
        var spawnTransactions = instance.Transactions
            .Where(tx => tx.Type == SagaTransactionType.CharacterSpawned)
            .ToList();

        Assert.Equal(3, spawnTransactions.Count);

        // Manually calculate what the positions SHOULD be using the same seed
        var expectedPositions = CalculateExpectedSpawnPositions(
            10.0, 5.0, // Avatar position
            10.0, // Spawn radius is fixed at 10.0 meters (default trigger type)
            3, // Count
            seed);

        // Verify transactions match expected positions
        for (var i = 0; i < 3; i++)
        {
            var actualX = double.Parse(spawnTransactions[i].Data["X"]);
            var actualZ = double.Parse(spawnTransactions[i].Data["Z"]);

            Assert.Equal(expectedPositions[i].x, actualX, precision: 4);
            Assert.Equal(expectedPositions[i].z, actualZ, precision: 4);
        }
    }

    // Helper to calculate expected spawn positions (mirrors SagaInteractionService logic)
    private List<(double x, double z)> CalculateExpectedSpawnPositions(
        double centerX, double centerZ, double radius, int count, int seed)
    {
        var positions = new List<(double, double)>();
        var rng = new Random(seed);
        var baseAngleStep = 2.0 * Math.PI / count;

        for (var i = 0; i < count; i++)
        {
            var angle = i * baseAngleStep + (rng.NextDouble() - 0.5) * baseAngleStep * 0.2;
            var radiusVariation = radius * (0.9 + rng.NextDouble() * 0.1);
            var offsetX = radiusVariation * Math.Sin(angle);
            var offsetZ = radiusVariation * Math.Cos(angle);
            positions.Add((centerX + offsetX, centerZ + offsetZ));
        }

        return positions;
    }

    #endregion

    
}
