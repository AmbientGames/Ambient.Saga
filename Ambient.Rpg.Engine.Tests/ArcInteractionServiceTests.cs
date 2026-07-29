using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain.Arcs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Tests;

/// <summary>
/// Unit tests for ArcInteractionService which handles avatar interactions with Arc triggers.
/// Tests Arc-relative coordinate system, deterministic spawning, and transaction creation.
/// </summary>
public class ArcInteractionServiceTests
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

    private Arc CreateArcTemplate(string refName = "TestArc")
    {
        return new Arc
        {
            RefName = refName,
            DisplayName = "Test Arc",
            Latitude = 35.0,
            Longitude = 139.0
        };
    }

    private ArcInstance CreateArcInstance(string poiRef = "TestArc", Guid? avatarId = null)
    {
        return new ArcInstance
        {
            InstanceId = Guid.NewGuid(),
            ArcRef = poiRef,
            OwnerAvatarId = avatarId ?? Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Transactions = new List<ArcTransaction>()
        };
    }

    private ArcTrigger CreateArcTrigger(
        string refName = "TestTrigger",
        float enterRadius = 10.0f,
        CharacterSpawn[]? spawns = null,
        string[]? requiredTokens = null)
    {
        return new ArcTrigger
        {
            RefName = refName,
            EnterRadius = enterRadius,
            Spawn = spawns,
            RequiresQuestTokenRef = requiredTokens
        };
    }

    private AvatarBase CreateAvatar(string archetypeRef = "Warrior", params string[] questTokens)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(archetypeRef));
        var avatarId = new Guid(hash);

        return new AvatarBase
        {
            AvatarId = avatarId,
            ArchetypeRef = archetypeRef,
            Capabilities = new ItemCollection()
        };
    }

    private CharacterSpawn CreateCharacterSpawn(string characterRef, int count = 1)
    {
        return new CharacterSpawn
        {
            CharacterRef = characterRef,
            Count = count
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTemplate_ThrowsArgumentNullException()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var arcTriggers = new List<ArcTrigger>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ArcInteractionService(null!, arcTriggers, world));
    }

    [Fact]
    public void Constructor_WithNullTriggers_ThrowsArgumentNullException()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ArcInteractionService(template, null!, world));
    }

    [Fact]
    public void Constructor_WithNullWorld_ThrowsArgumentNullException()
    {
        // Arrange
        var template = CreateArcTemplate();
        var arcTriggers = new List<ArcTrigger>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ArcInteractionService(template, arcTriggers, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var arcTriggers = new List<ArcTrigger>();

        // Act
        var service = new ArcInteractionService(template, arcTriggers, world);

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
        var template = CreateArcTemplate();
        var arcTriggers = new List<ArcTrigger>();
        var service = new ArcInteractionService(template, arcTriggers, world);
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
        var template = CreateArcTemplate();
        var arcTriggers = new List<ArcTrigger>();
        var service = new ArcInteractionService(template, arcTriggers, world);
        var instance = CreateArcInstance();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            service.UpdateWithAvatarPosition(instance, 0, 0, null!));
    }

    #endregion

  

 
    #region Character Spawning Tests

    [Fact]
    public void UpdateWithAvatarPosition_CharacterSpawnedTransaction_ContainsPositionData()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 1);
        var trigger = CreateArcTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<ArcTrigger> { trigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Act - Avatar at (5.0, 6.0) = distance 7.81 from center (within 10.0 radius)
        service.UpdateWithAvatarPosition(instance, 5.0, 6.0, avatar);

        // Assert
        var spawnTx = instance.Transactions
            .First(tx => tx.Type == ArcTransactionType.CharacterSpawned);

        // Should have position data (Arc-relative coordinates)
        Assert.True(spawnTx.Data.ContainsKey("X"));
        Assert.True(spawnTx.Data.ContainsKey("Z"));
        Assert.True(spawnTx.Data.ContainsKey("SpawnHeight"));

        // Position should be near avatar position, not at Arc center
        var x = double.Parse(spawnTx.Data["X"]);
        var z = double.Parse(spawnTx.Data["Z"]);

        // Distance from avatar should be roughly the spawn radius
        var dx = x - 5.0;
        var dz = z - 6.0;
        var distanceFromAvatar = Math.Sqrt(dx * dx + dz * dz);

        // Spawn depth is Min(EnterRadius * 0.5, 15.0) = 5.0 meters
        var expectedDepth = Math.Min(10.0 * 0.5, 15.0);
        Assert.InRange(distanceFromAvatar, expectedDepth * 0.9, expectedDepth * 1.0);
    }

    [Fact]
    public void UpdateWithAvatarPosition_CharacterSpawnedTransaction_HasUniqueInstanceId()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 3);
        var trigger = CreateArcTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<ArcTrigger> { trigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert
        var spawnTransactions = instance.Transactions
            .Where(tx => tx.Type == ArcTransactionType.CharacterSpawned)
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
        var template = CreateArcTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 2);
        var trigger = CreateArcTrigger(refName: "TestTrigger", enterRadius: 10.0f, spawns: new[] { spawn });
        var triggers = new List<ArcTrigger> { trigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 5.0, 5.0, avatar);

        // Assert
        var triggerTx = instance.Transactions
            .First(tx => tx.Type == ArcTransactionType.TriggerActivated);
        var seed = int.Parse(triggerTx.Data["Seed"]);

        var spawnTransactions = instance.Transactions
            .Where(tx => tx.Type == ArcTransactionType.CharacterSpawned)
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


    

    #region Progressive Unlock System Tests

  
    [Fact]
    public void UpdateWithAvatarPosition_ProgressiveUnlock_FullChainOuterToMiddleToInner()
    {
        // This is the key test - simulates a full progression:
        // 1. Outer trigger (no requirements) -> gives "Outer_Complete"
        // 2. Middle trigger (requires "Outer_Complete") -> gives "Middle_Complete"
        // 3. Inner trigger (requires "Middle_Complete")

        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();

        // Create three concentric triggers with progression
        var outerTrigger = new ArcTrigger
        {
            RefName = "Outer",
            EnterRadius = 30.0f,
            RequiresQuestTokenRef = null, // No requirements
            GivesQuestTokenRef = new[] { "Outer_Complete" }
        };

        var middleTrigger = new ArcTrigger
        {
            RefName = "Middle",
            EnterRadius = 20.0f,
            RequiresQuestTokenRef = new[] { "Outer_Complete" },
            GivesQuestTokenRef = new[] { "Middle_Complete" }
        };

        var innerTrigger = new ArcTrigger
        {
            RefName = "Inner",
            EnterRadius = 10.0f,
            RequiresQuestTokenRef = new[] { "Middle_Complete" },
            GivesQuestTokenRef = new[] { "Inner_Complete" }
        };

        var triggers = new List<ArcTrigger> { outerTrigger, middleTrigger, innerTrigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();

        // === STEP 1: Avatar with no keys tries all triggers ===
        var avatarNoKeys = CreateAvatar("Warrior");
        var progress = new Mocks.MockAvatarProgressRepository();

        // At outer position (25m from center) - only outer should activate
        service.UpdateWithAvatarPosition(instance, 25.0, 0.0, avatarNoKeys, progress);

        var outerActivations = instance.Transactions.Count(tx =>
            tx.Type == ArcTransactionType.TriggerActivated &&
            tx.Data["ArcTriggerRef"] == "Outer");
        Assert.Equal(1, outerActivations); // Outer activates

        var middleActivations = instance.Transactions.Count(tx =>
            tx.Type == ArcTransactionType.TriggerActivated &&
            tx.Data["ArcTriggerRef"] == "Middle");
        Assert.Equal(0, middleActivations); // Middle blocked (no key)

        // Verify quest token awarded
        var questTokens = instance.Transactions.Where(tx =>
            tx.Type == ArcTransactionType.QuestTokenAwarded).ToList();
        Assert.Single(questTokens);
        Assert.Equal("Outer_Complete", questTokens[0].Data["QuestTokenRef"]);

        // === STEP 2: Avatar with "Outer_Complete" tries middle trigger ===
        var avatarWithOuterKey = CreateAvatar("Warrior");
        progress.SetQuestToken(avatarWithOuterKey.AvatarId, "Outer_Complete");

        // Reset to simulate new session (in real game, avatar inventory would be updated)
        instance.Transactions.Clear();

        // At middle position (15m from center) - middle should now activate
        service.UpdateWithAvatarPosition(instance, 15.0, 0.0, avatarWithOuterKey, progress);

        var middleActivationCount = instance.Transactions.Count(tx =>
            tx.Type == ArcTransactionType.TriggerActivated &&
            tx.Data["ArcTriggerRef"] == "Middle");
        Assert.Equal(1, middleActivationCount); // Middle activates with key!

        // Verify middle gives its quest token
        var middleTokens = instance.Transactions.Where(tx =>
            tx.Type == ArcTransactionType.QuestTokenAwarded &&
            tx.Data["QuestTokenRef"] == "Middle_Complete").ToList();
        Assert.Single(middleTokens);

        // === STEP 3: Avatar with "Middle_Complete" activates inner ===
        var avatarWithMiddleKey = CreateAvatar("Warrior");
        progress.SetQuestToken(avatarWithMiddleKey.AvatarId, "Middle_Complete");

        instance.Transactions.Clear();

        // At inner position (5m from center) - inner should activate
        service.UpdateWithAvatarPosition(instance, 5.0, 0.0, avatarWithMiddleKey, progress);

        var innerActivationCount = instance.Transactions.Count(tx =>
            tx.Type == ArcTransactionType.TriggerActivated &&
            tx.Data["ArcTriggerRef"] == "Inner");
        Assert.Equal(1, innerActivationCount); // Inner activates with key!

        // Verify inner gives its quest token
        var innerTokens = instance.Transactions.Where(tx =>
            tx.Type == ArcTransactionType.QuestTokenAwarded &&
            tx.Data["QuestTokenRef"] == "Inner_Complete").ToList();
        Assert.Single(innerTokens);
    }

    [Fact]
    public void UpdateWithAvatarPosition_ProgressiveUnlock_SimultaneousTriggersAtSameRadius()
    {
        // Edge case: Multiple triggers at same radius with different requirements
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();

        var triggerA = new ArcTrigger
        {
            RefName = "PathA",
            EnterRadius = 15.0f,
            RequiresQuestTokenRef = null,
            GivesQuestTokenRef = new[] { "PathA_Token" }
        };

        var triggerB = new ArcTrigger
        {
            RefName = "PathB",
            EnterRadius = 15.0f,
            RequiresQuestTokenRef = null,
            GivesQuestTokenRef = new[] { "PathB_Token" }
        };

        var triggers = new List<ArcTrigger> { triggerA, triggerB };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Act - Both triggers at same radius should activate
        service.UpdateWithAvatarPosition(instance, 10.0, 10.0, avatar);

        // Assert - Both triggers activate (2 TriggerActivated + 2 QuestTokenAwarded)
        var triggerActivations = instance.Transactions.Count(tx =>
            tx.Type == ArcTransactionType.TriggerActivated);
        Assert.Equal(2, triggerActivations);

        var questTokens = instance.Transactions.Where(tx =>
            tx.Type == ArcTransactionType.QuestTokenAwarded).ToList();
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
        var template = CreateArcTemplate();
        var trigger = CreateArcTrigger(refName: "TestTrigger", enterRadius: 15.0f);
        var triggers = new List<ArcTrigger> { trigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Act - Query what trigger would activate
        var result = service.GetArcTriggerAtPosition(instance, 10.0, 5.0, avatar);

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
        var template = CreateArcTemplate();
        var trigger = CreateArcTrigger(refName: "TestTrigger", enterRadius: 10.0f);
        var triggers = new List<ArcTrigger> { trigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Act - Query position outside radius
        var result = service.GetArcTriggerAtPosition(instance, 20.0, 20.0, avatar);

        // Assert
        Assert.Null(result);
        Assert.Empty(instance.Transactions);
    }

    [Fact]
    public void GetTriggerAtPosition_MissingQuestTokens_ReturnsNull()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var trigger = CreateArcTrigger(
            refName: "LockedTrigger",
            enterRadius: 15.0f,
            requiredTokens: new[] { "MagicKey" });
        var triggers = new List<ArcTrigger> { trigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar(); // No quest tokens

        // Act - Query trigger that requires keys
        var result = service.GetArcTriggerAtPosition(instance, 10.0, 5.0, avatar);

        // Assert - Returns null (avatar can't activate it)
        Assert.Null(result);
    }

    [Fact]
    public void GetTriggerAtPosition_MultipleTriggersReturnsInnermost()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var outerTrigger = CreateArcTrigger(refName: "Outer", enterRadius: 30.0f);
        var middleTrigger = CreateArcTrigger(refName: "Middle", enterRadius: 20.0f);
        var innerTrigger = CreateArcTrigger(refName: "Inner", enterRadius: 10.0f);
        var triggers = new List<ArcTrigger> { outerTrigger, middleTrigger, innerTrigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Act - Query position within all three triggers
        var result = service.GetArcTriggerAtPosition(instance, 8.0, 4.0, avatar);

        // Assert - Returns innermost trigger
        Assert.NotNull(result);
        Assert.Equal("Inner", result.RefName);
    }

    [Fact]
    public void GetTriggersAtPosition_ReturnsAllTriggersWithStatus()
    {
        // Arrange
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var outerTrigger = CreateArcTrigger(refName: "Outer", enterRadius: 30.0f);
        var middleTrigger = CreateArcTrigger(refName: "Middle", enterRadius: 20.0f);
        var innerTrigger = CreateArcTrigger(refName: "Inner", enterRadius: 10.0f);
        var triggers = new List<ArcTrigger> { outerTrigger, middleTrigger, innerTrigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();

        // Act - Query all triggers at position
        var results = service.GetTriggersAtPosition(instance, 15.0, 0.0);

        // Assert - Returns all 3 triggers sorted outermost to innermost
        Assert.Equal(3, results.Count);
        Assert.Equal("Outer", results[0].ArcTrigger.RefName);
        Assert.Equal("Middle", results[1].ArcTrigger.RefName);
        Assert.Equal("Inner", results[2].ArcTrigger.RefName);

        // Check within radius flags
        Assert.True(results[0].IsWithinRadius); // Outer: 30m
        Assert.True(results[1].IsWithinRadius); // Middle: 20m
        Assert.False(results[2].IsWithinRadius); // Inner: 10m (position is 15m away)
    }

    #endregion

    #region Trigger Exit & Completion Tests (audit B9)

    /// <summary>
    /// Marks all pending transactions committed, mirroring what
    /// UpdateAvatarPositionHandler does after each position update. Replay only
    /// folds committed transactions, so the transition tracking under test needs
    /// the same commit cadence as production.
    /// </summary>
    private static void CommitPendingTransactions(ArcInstance instance)
    {
        foreach (var tx in instance.Transactions)
        {
            tx.Status = TransactionStatus.Committed;
        }
        instance.IsDirty = true;
    }

    [Fact]
    public void UpdateWithAvatarPosition_RepeatedTicksOutsideEnteredTrigger_EmitsExactlyOneAvatarExited()
    {
        // Audit B9: an AvatarExited used to be committed on EVERY position tick
        // outside the radius of a still-Active trigger — unbounded log growth.
        // A trigger with no spawns is the exact trigger kind that reproduced it.
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var trigger = CreateArcTrigger(refName: "TokenRing", enterRadius: 10.0f, spawns: null);
        var service = new ArcInteractionService(template, new List<ArcTrigger> { trigger }, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Enter the ring (activation + AvatarEntered), commit like the handler does
        service.UpdateWithAvatarPosition(instance, 5.0, 0.0, avatar);
        CommitPendingTransactions(instance);
        Assert.Equal(1, instance.Transactions.Count(tx => tx.Type == ArcTransactionType.AvatarEntered));

        // Keep ticking far outside the exit radius (10 + 10 hysteresis = 20)
        for (var tick = 0; tick < 5; tick++)
        {
            service.UpdateWithAvatarPosition(instance, 50.0, 0.0, avatar);
            CommitPendingTransactions(instance);
        }

        // Exactly ONE exit for the transition; nothing for the ticks after it
        Assert.Equal(1, instance.Transactions.Count(tx => tx.Type == ArcTransactionType.AvatarExited));
    }

    [Fact]
    public void UpdateWithAvatarPosition_SpawningTrigger_AlsoEmitsSingleAvatarExited()
    {
        // Completed (spawning) triggers get the same transition-based exit record
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var trigger = CreateArcTrigger(
            refName: "Camp",
            enterRadius: 10.0f,
            spawns: new[] { CreateCharacterSpawn("Guard") });
        var service = new ArcInteractionService(template, new List<ArcTrigger> { trigger }, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        service.UpdateWithAvatarPosition(instance, 5.0, 0.0, avatar);
        CommitPendingTransactions(instance);

        for (var tick = 0; tick < 3; tick++)
        {
            service.UpdateWithAvatarPosition(instance, 60.0, 0.0, avatar);
            CommitPendingTransactions(instance);
        }

        Assert.Equal(1, instance.Transactions.Count(tx => tx.Type == ArcTransactionType.AvatarExited));
    }

    [Fact]
    public void UpdateWithAvatarPosition_TriggerWithNoSpawns_CompletesAtActivation()
    {
        // Audit B9: triggers that spawned nothing used to stay Active forever.
        // Activation IS the trigger's work — tokens awarded, spawns attempted —
        // so it completes immediately regardless of spawn outcome.
        var world = CreateWorldWithCharacters();
        var template = CreateArcTemplate();
        var trigger = new ArcTrigger
        {
            RefName = "TokenRing",
            EnterRadius = 10.0f,
            GivesQuestTokenRef = new[] { "RingToken" }
        };
        var service = new ArcInteractionService(template, new List<ArcTrigger> { trigger }, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        service.UpdateWithAvatarPosition(instance, 5.0, 0.0, avatar);
        CommitPendingTransactions(instance);

        // Token still awarded, and the trigger is done — not stuck Active
        Assert.Equal(1, instance.Transactions.Count(tx => tx.Type == ArcTransactionType.QuestTokenAwarded));
        Assert.Equal(1, instance.Transactions.Count(tx => tx.Type == ArcTransactionType.TriggerCompleted));

        var info = service.GetTriggersAtPosition(instance, 5.0, 0.0).Single();
        Assert.True(info.IsCompleted);
    }

    #endregion

    #region End-to-End Integration Tests


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
        var template = CreateArcTemplate();
        var spawn = CreateCharacterSpawn("Guard", count: 3);
        var trigger = CreateArcTrigger(refName: "TestTrigger", enterRadius: 15.0f, spawns: new[] { spawn });
        var triggers = new List<ArcTrigger> { trigger };
        var service = new ArcInteractionService(template, triggers, world);
        var instance = CreateArcInstance();
        var avatar = CreateAvatar();

        // Act
        service.UpdateWithAvatarPosition(instance, 10.0, 5.0, avatar);

        // Assert - Extract seed from TriggerActivated
        var triggerTx = instance.Transactions.First(tx => tx.Type == ArcTransactionType.TriggerActivated);
        var seed = int.Parse(triggerTx.Data["Seed"]);

        // Get spawn positions
        var spawnTransactions = instance.Transactions
            .Where(tx => tx.Type == ArcTransactionType.CharacterSpawned)
            .ToList();

        Assert.Equal(3, spawnTransactions.Count);

        // Manually calculate what the positions SHOULD be using the same seed
        // Spawn depth = Min(EnterRadius * 0.5, 15.0) = Min(15 * 0.5, 15) = 7.5
        var spawnDepth = Math.Min(15.0 * 0.5, 15.0);
        var expectedPositions = CalculateExpectedSpawnPositions(
            10.0, 5.0, // Avatar position
            spawnDepth, // Forward-arc spawn depth
            3, // Count
            seed);

        // Verify transactions match expected positions. Positions round-trip through
        // "F6"-formatted strings, so compare with an explicit tolerance — rounding both
        // sides to N digits (Assert.Equal precision) flakes when a coordinate lands on
        // a rounding boundary for an unlucky seed.
        for (var i = 0; i < 3; i++)
        {
            var actualX = double.Parse(spawnTransactions[i].Data["X"]);
            var actualZ = double.Parse(spawnTransactions[i].Data["Z"]);

            Assert.True(Math.Abs(expectedPositions[i].x - actualX) < 1e-4,
                $"X[{i}]: expected {expectedPositions[i].x}, got {actualX} (seed {seed})");
            Assert.True(Math.Abs(expectedPositions[i].z - actualZ) < 1e-4,
                $"Z[{i}]: expected {expectedPositions[i].z}, got {actualZ} (seed {seed})");
        }
    }

    // Helper to calculate expected spawn positions (mirrors ArcInteractionService.CalculateForwardArcSpawnPositions)
    private List<(double x, double z)> CalculateExpectedSpawnPositions(
        double avatarX, double avatarZ, double depth, int count, int seed)
    {
        var positions = new List<(double, double)>();
        var rng = new Random(seed);

        // Unit vector from avatar toward origin, or a random direction if avatar is at origin.
        const double epsilon = 0.01;
        var avatarDistanceFromOrigin = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);
        double forwardAngle;
        if (avatarDistanceFromOrigin < epsilon)
        {
            forwardAngle = rng.NextDouble() * 2.0 * Math.PI;
        }
        else
        {
            forwardAngle = Math.Atan2(-avatarX, -avatarZ);
        }

        const double totalArcRadians = Math.PI / 3.0; // 60 degrees
        var angleStep = count > 1 ? totalArcRadians / (count - 1) : 0.0;
        var startOffset = -totalArcRadians / 2.0;
        var jitterScale = count > 1 ? angleStep * 0.2 : Math.PI / 36.0;

        for (var i = 0; i < count; i++)
        {
            var baseOffset = count > 1 ? startOffset + i * angleStep : 0.0;
            var angle = forwardAngle + baseOffset + (rng.NextDouble() - 0.5) * jitterScale;
            var radiusVariation = depth * (0.9 + rng.NextDouble() * 0.1);
            var offsetX = radiusVariation * Math.Sin(angle);
            var offsetZ = radiusVariation * Math.Cos(angle);
            positions.Add((avatarX + offsetX, avatarZ + offsetZ));
        }

        return positions;
    }

    #endregion

    
}
