using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Tests.Rpg.Arcs;

/// <summary>
/// Unit tests for ArcStateMachine replay logic.
/// Verifies deterministic, idempotent state derivation from transaction logs.
/// </summary>
public class ArcStateMachineTests
{
    private readonly Arc _testArc;
    private readonly List<ArcTrigger> _testArcTriggers;
    private readonly World _testWorld;
    private readonly ArcStateMachine _stateMachine;

    public ArcStateMachineTests()
    {
        // Create minimal test Arc template
        _testArc = new Arc
        {
            RefName = "TestArc",
            DisplayName = "Test Arc",
            Latitude = 35.0,
            Longitude = 139.0
        };

        // Create test triggers
        _testArcTriggers = new List<ArcTrigger>
        {
            new ArcTrigger
            {
                RefName = "approach",
                DisplayName = "Approach Trigger",
                EnterRadius = 50.0f
            },
            new ArcTrigger
            {
                RefName = "inner",
                DisplayName = "Inner Trigger",
                EnterRadius = 10.0f
            }
        };

        // Create minimal test world with character lookup
        _testWorld = new World
        {
            CharactersLookup = new Dictionary<string, Character>
            {
                ["TestBoss"] = new Character
                {
                    RefName = "TestBoss",
                    DisplayName = "Test Boss",
                    Stats = new CharacterStats { Health = 100, Mana = 50 },
                    Capabilities = new ItemCollection()
                },
                ["Boss"] = new Character
                {
                    RefName = "Boss",
                    DisplayName = "Boss",
                    Stats = new CharacterStats { Health = 100, Mana = 50 },
                    Capabilities = new ItemCollection()
                }
            }
        };

        _stateMachine = new ArcStateMachine(_testArc, _testArcTriggers, _testWorld);
    }

    [Fact]
    public void ReplayToNow_EmptyTransactionLog_ReturnsInitialState()
    {
        // Arrange
        var instance = new ArcInstance
        {
            ArcRef = "TestArc",
            InstanceType = ArcInstanceType.SinglePlayer
        };

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal("TestArc", state.ArcRef);
        Assert.Equal(ArcStatus.Undiscovered, state.Status);
        Assert.Null(state.FirstDiscoveredAt);
        Assert.Equal(2, state.Triggers.Count);
        Assert.True(state.Triggers.ContainsKey("approach"));
        Assert.True(state.Triggers.ContainsKey("inner"));
        Assert.Equal(ArcTriggerStatus.Inactive, state.Triggers["approach"].Status);
    }

    [Fact]
    public void ReplayToNow_ArcDiscoveredTransaction_UpdatesStatusAndTimestamp()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var discoveryTime = DateTime.UtcNow;

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.ArcDiscovered,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            LocalTimestamp = discoveryTime,
            ServerTimestamp = discoveryTime,
            SequenceNumber = 1
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(ArcStatus.Active, state.Status);
        Assert.Equal(discoveryTime, state.FirstDiscoveredAt);
        Assert.Contains("Avatar1", state.DiscoveredByAvatars);
    }

    [Fact]
    public void ReplayToNow_CharacterSpawnedAndDefeated_TracksLifecycle()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();
        var spawnTime = DateTime.UtcNow;
        var defeatTime = spawnTime.AddMinutes(5);

        // Character spawned
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            ServerTimestamp = spawnTime,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["ArcTriggerRef"] = "approach",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Character defeated
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDefeated,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = defeatTime,
            SequenceNumber = 2,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString()
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.True(state.Characters. ContainsKey(characterId.ToString()));
        var character = state.Characters[characterId.ToString()];
        Assert.Equal("TestBoss", character.CharacterRef);
        Assert.False(character.IsAlive);
        Assert.Equal(0.0, character.CurrentHealth);
        Assert.Equal(spawnTime, character.SpawnedAt);
        Assert.Equal(defeatTime, character.DefeatedAt);
    }

    [Fact]
    public void ReplayToNow_CharacterDamaged_TracksHealthAndDamageByAvatar()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        // Spawn
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            ServerTimestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Avatar1 damages 30%
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDamaged,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = DateTime.UtcNow.AddSeconds(1),
            SequenceNumber = 2,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["Damage"] = "0.3"
            }
        });

        // Avatar2 damages 50%
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDamaged,
            AvatarId = "Avatar2",
            Status = TransactionStatus.Committed,
            ServerTimestamp = DateTime.UtcNow.AddSeconds(2),
            SequenceNumber = 3,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["Damage"] = "0.5"
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var character = state.Characters[characterId.ToString()];
        Assert.Equal(0.2, character.CurrentHealth, precision: 2);  // 100% - 30% - 50% = 20%
        Assert.True(character.IsAlive);  // Still alive with 20% health
        Assert.Equal(0.3, character.DamageByAvatar["Avatar1"]);
        Assert.Equal(0.5, character.DamageByAvatar["Avatar2"]);
    }

    [Fact]
    public void ReplayToNow_CharacterHealed_RestoresHealth()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDamaged,
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString(), ["Damage"] = "0.6" }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterHealed,
            Status = TransactionStatus.Committed,
            SequenceNumber = 3,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString(), ["Healing"] = "0.3" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(0.7, state.Characters[characterId.ToString()].CurrentHealth, precision: 2);
        Assert.True(state.Characters[characterId.ToString()].IsAlive);
    }

    [Fact]
    public void ReplayToNow_TriggerActivated_UpdatesTriggerState()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var activationTime = DateTime.UtcNow;

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TriggerActivated,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = activationTime,
            SequenceNumber = 1,
            Data = new() { ["ArcTriggerRef"] = "approach" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var trigger = state.Triggers["approach"];
        Assert.Equal(ArcTriggerStatus.Active, trigger.Status);
        Assert.Equal(1, trigger.ActivationCount);
        Assert.Equal(activationTime, trigger.FirstActivatedAt);
        Assert.Equal(activationTime, trigger.LastActivatedAt);
        Assert.Contains("Avatar1", trigger.TriggeredByAvatars);
    }

    [Fact]
    public void ReplayToNow_TriggerActivatedMultipleTimes_IncrementsCount()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TriggerActivated,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new() { ["ArcTriggerRef"] = "approach" }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TriggerActivated,
            AvatarId = "Avatar2",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["ArcTriggerRef"] = "approach" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var trigger = state.Triggers["approach"];
        Assert.Equal(2, trigger.ActivationCount);
        Assert.Contains("Avatar1", trigger.TriggeredByAvatars);
        Assert.Contains("Avatar2", trigger.TriggeredByAvatars);
    }

    [Fact]
    public void ReplayToNow_TriggerCompleted_UpdatesStatusAndTimestamp()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var completionTime = DateTime.UtcNow;

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TriggerCompleted,
            Status = TransactionStatus.Committed,
            ServerTimestamp = completionTime,
            SequenceNumber = 1,
            Data = new() { ["ArcTriggerRef"] = "approach" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var trigger = state.Triggers["approach"];
        Assert.Equal(ArcTriggerStatus.Completed, trigger.Status);
        Assert.Equal(completionTime, trigger.CompletedAt);
    }

    [Fact]
    public void ReplayToNow_ArcCompleted_UpdatesStatusAndAvatars()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var completionTime = DateTime.UtcNow;

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.ArcCompleted,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = completionTime,
            SequenceNumber = 1
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(ArcStatus.Completed, state.Status);
        Assert.Equal(completionTime, state.CompletedAt);
        Assert.Contains("Avatar1", state.CompletedByAvatars);
    }

    [Fact]
    public void ReplayToNow_Deterministic_AlwaysProducesSameResult()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "Boss",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDamaged,
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString(), ["Damage"] = "0.5" }
        });

        // Act - Replay multiple times
        var state1 = _stateMachine.ReplayToNow(instance);
        var state2 = _stateMachine.ReplayToNow(instance);
        var state3 = _stateMachine.ReplayToNow(instance);

        // Assert - All replays produce identical results
        Assert.Equal(state1.Characters[characterId.ToString()].CurrentHealth, state2.Characters[characterId.ToString()].CurrentHealth);
        Assert.Equal(state2.Characters[characterId.ToString()].CurrentHealth, state3.Characters[characterId.ToString()].CurrentHealth);
        Assert.Equal(state1.TransactionCount, state2.TransactionCount);
        Assert.Equal(state2.TransactionCount, state3.TransactionCount);
    }

    [Fact]
    public void ReplayToTimestamp_OnlyIncludesTransactionsBeforeTimestamp()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow;

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "Boss",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDamaged,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddMinutes(1),
            SequenceNumber = 2,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString(), ["Damage"] = "0.3" }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDefeated,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddMinutes(2),
            SequenceNumber = 3,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString() }
        });

        // Act - Replay to 1.5 minutes (after damage, before defeat)
        var state = _stateMachine.ReplayToTimestamp(instance, baseTime.AddSeconds(90));

        // Assert
        Assert.True(state.Characters. ContainsKey(characterId.ToString()));
        Assert.Equal(0.7, state.Characters[characterId.ToString()].CurrentHealth, precision: 2);
        Assert.True(state.Characters[characterId.ToString()].IsAlive);  // Defeat hasn't happened yet
    }

    [Fact]
    public void ReplayToSequence_OnlyIncludesTransactionsUpToSequenceNumber()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "Boss",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDamaged,
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString(), ["Damage"] = "0.5" }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDefeated,
            Status = TransactionStatus.Committed,
            SequenceNumber = 3,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString() }
        });

        // Act - Replay only first 2 transactions
        var state = _stateMachine.ReplayToSequence(instance, 2);

        // Assert
        Assert.Equal(2, state.TransactionCount);
        Assert.Equal(0.5, state.Characters[characterId.ToString()].CurrentHealth);
        Assert.True(state.Characters[characterId.ToString()].IsAlive);  // Not defeated yet
    }

    [Fact]
    public void ReplayToNow_OnlyCommittedTransactions_IgnoresPending()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.ArcDiscovered,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TriggerActivated,
            Status = TransactionStatus.Pending,  // Not committed yet
            SequenceNumber = 2,
            Data = new() { ["ArcTriggerRef"] = "approach" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(ArcStatus.Active, state.Status);  // Discovery processed
        Assert.Equal(ArcTriggerStatus.Inactive, state.Triggers["approach"].Status);  // Trigger not processed (pending)
        Assert.Equal(1, state.TransactionCount);  // Only 1 committed transaction
    }

    [Fact]
    public void ReplayToNow_ComplexScenario_BossDefeatWithMultipleAvatars()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var bossId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow;

        // Arc discovered
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.ArcDiscovered,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime,
            SequenceNumber = 1
        });

        // Trigger activated
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TriggerActivated,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(5),
            SequenceNumber = 2,
            Data = new() { ["ArcTriggerRef"] = "approach" }
        });

        // Boss spawned
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(10),
            SequenceNumber = 3,
            Data = new()
            {
                ["CharacterInstanceId"] = bossId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["ArcTriggerRef"] = "approach",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Avatar1 damages 40%
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDamaged,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(15),
            SequenceNumber = 4,
            Data = new() { ["CharacterInstanceId"] = bossId.ToString(), ["Damage"] = "0.4" }
        });

        // Avatar2 enters and damages 60%
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.AvatarEntered,
            AvatarId = "Avatar2",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(20),
            SequenceNumber = 5
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDamaged,
            AvatarId = "Avatar2",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(25),
            SequenceNumber = 6,
            Data = new() { ["CharacterInstanceId"] = bossId.ToString(), ["Damage"] = "0.6" }
        });

        // Boss defeated
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterDefeated,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(30),
            SequenceNumber = 7,
            Data = new() { ["CharacterInstanceId"] = bossId.ToString() }
        });

        // Arc completed
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.ArcCompleted,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(35),
            SequenceNumber = 8
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(ArcStatus.Completed, state.Status);
        Assert.Contains("Avatar1", state.DiscoveredByAvatars);
        Assert.Contains("Avatar1", state.CompletedByAvatars);

        var trigger = state.Triggers["approach"];
        Assert.Equal(ArcTriggerStatus.Active, trigger.Status);
        Assert.Equal(1, trigger.ActivationCount);

        var boss = state.Characters[bossId.ToString()];
        Assert.False(boss.IsAlive);
        Assert.Equal(0.0, boss.CurrentHealth);
        Assert.Equal(0.4, boss.DamageByAvatar["Avatar1"]);
        Assert.Equal(0.6, boss.DamageByAvatar["Avatar2"]);
        Assert.Equal("approach", boss.SpawnedByTriggerRef);

        Assert.Equal(8, state.TransactionCount);
    }

    [Fact]
    public void ReplayToNow_QuestTokenAwarded_TrackedInState()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };

        // Trigger activated and awarded quest token
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TriggerActivated,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new() { ["ArcTriggerRef"] = "approach" }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.QuestTokenAwarded,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new()
            {
                ["QuestTokenRef"] = "ApproachComplete",
                ["ArcTriggerRef"] = "approach",
                ["Reason"] = "Trigger 'approach' activated"
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(ArcStatus.Undiscovered, state.Status);
        Assert.Equal(ArcTriggerStatus.Active, state.Triggers["approach"].Status);
        Assert.Equal(2, state.TransactionCount);
        // Quest tokens are tracked in avatar inventory, not Arc state
        // This test just verifies the transaction is processed without errors
    }

    [Fact]
    public void ReplayToNow_MultipleQuestTokensAwarded_AllProcessed()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.QuestTokenAwarded,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new() { ["QuestTokenRef"] = "Token1", ["ArcTriggerRef"] = "approach" }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.QuestTokenAwarded,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["QuestTokenRef"] = "Token2", ["ArcTriggerRef"] = "approach" }
        });

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.QuestTokenAwarded,
            AvatarId = "Avatar2",
            Status = TransactionStatus.Committed,
            SequenceNumber = 3,
            Data = new() { ["QuestTokenRef"] = "Token1", ["ArcTriggerRef"] = "inner" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert - All transactions processed successfully
        Assert.Equal(3, state.TransactionCount);
    }

    #region Phase 2: Character Trait Spawning Tests

    [Fact]
    public void ReplayToNow_CharacterSpawnedWithHostileTrait_TraitCopiedToState()
    {
        // Arrange - Add a character with Hostile trait to the world
        var characterWithTrait = new Character
        {
            RefName = "HostileEnemy",
            DisplayName = "Hostile Enemy",
            Stats = new CharacterStats { Health = 100, Mana = 50 },
            Capabilities = new ItemCollection(),
            Traits = new CharacterTrait[]
            {
                new CharacterTrait { Name = CharacterTraitType.Hostile }
            }
        };
        _testWorld.CharactersLookup["HostileEnemy"] = characterWithTrait;

        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "HostileEnemy",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var character = state.Characters[characterId.ToString()];
        Assert.True(character.Traits.ContainsKey("Hostile"));
        Assert.Null(character.Traits["Hostile"]); // Boolean flag trait has null value
    }

    [Fact]
    public void ReplayToNow_CharacterSpawnedWithNumericTrait_TraitValueCopied()
    {
        // Arrange - Add a character with Aggression numeric trait
        var characterWithTrait = new Character
        {
            RefName = "AggressiveEnemy",
            DisplayName = "Aggressive Enemy",
            Stats = new CharacterStats { Health = 100, Mana = 50 },
            Capabilities = new ItemCollection(),
            Traits = new CharacterTrait[]
            {
                new CharacterTrait { Name = CharacterTraitType.Aggression, Value = 75, ValueSpecified = true }
            }
        };
        _testWorld.CharactersLookup["AggressiveEnemy"] = characterWithTrait;

        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "AggressiveEnemy",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var character = state.Characters[characterId.ToString()];
        Assert.True(character.Traits.ContainsKey("Aggression"));
        Assert.Equal(75, character.Traits["Aggression"]);
    }

    [Fact]
    public void ReplayToNow_CharacterSpawnedWithMultipleTraits_AllTraitsCopied()
    {
        // Arrange - Add a character with multiple traits
        var characterWithTraits = new Character
        {
            RefName = "BossFightEnemy",
            DisplayName = "Boss Fight Enemy",
            Stats = new CharacterStats { Health = 500, Mana = 200 },
            Capabilities = new ItemCollection(),
            Traits = new CharacterTrait[]
            {
                new CharacterTrait { Name = CharacterTraitType.Hostile },
                new CharacterTrait { Name = CharacterTraitType.BossFight },
                new CharacterTrait { Name = CharacterTraitType.Aggression, Value = 90, ValueSpecified = true },
                new CharacterTrait { Name = CharacterTraitType.FleeThreshold, Value = 10, ValueSpecified = true }
            }
        };
        _testWorld.CharactersLookup["BossFightEnemy"] = characterWithTraits;

        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "BossFightEnemy",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var character = state.Characters[characterId.ToString()];
        Assert.Equal(4, character.Traits.Count);
        Assert.True(character.Traits.ContainsKey("Hostile"));
        Assert.True(character.Traits.ContainsKey("BossFight"));
        Assert.True(character.Traits.ContainsKey("Aggression"));
        Assert.True(character.Traits.ContainsKey("FleeThreshold"));
        Assert.Null(character.Traits["Hostile"]);
        Assert.Null(character.Traits["BossFight"]);
        Assert.Equal(90, character.Traits["Aggression"]);
        Assert.Equal(10, character.Traits["FleeThreshold"]);
    }

    [Fact]
    public void ReplayToNow_CharacterSpawnedWithNoTraits_TraitsDictionaryEmpty()
    {
        // Arrange - Use existing TestBoss which has no traits
        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var character = state.Characters[characterId.ToString()];
        Assert.Empty(character.Traits);
    }

    [Fact]
    public void ReplayToNow_CharacterSpawnedWithFriendlyTrait_NotHostile()
    {
        // Arrange - Add a friendly NPC
        var friendlyNpc = new Character
        {
            RefName = "FriendlyMerchant",
            DisplayName = "Friendly Merchant",
            Stats = new CharacterStats { Health = 100, Mana = 0 },
            Capabilities = new ItemCollection(),
            Traits = new CharacterTrait[]
            {
                new CharacterTrait { Name = CharacterTraitType.Friendly },
                new CharacterTrait { Name = CharacterTraitType.WillTrade },
                new CharacterTrait { Name = CharacterTraitType.TradeDiscount, Value = 15, ValueSpecified = true }
            }
        };
        _testWorld.CharactersLookup["FriendlyMerchant"] = friendlyNpc;

        var instance = new ArcInstance { ArcRef = "TestArc" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "FriendlyMerchant",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var character = state.Characters[characterId.ToString()];
        Assert.True(character.Traits.ContainsKey("Friendly"));
        Assert.True(character.Traits.ContainsKey("WillTrade"));
        Assert.True(character.Traits.ContainsKey("TradeDiscount"));
        Assert.False(character.Traits.ContainsKey("Hostile")); // Should NOT have Hostile
        Assert.Equal(15, character.Traits["TradeDiscount"]);
    }

    #endregion

    #region Trigger Occupancy Folds (audit B9)

    [Fact]
    public void ReplayToNow_AvatarEnteredThenExited_TogglesTriggerOccupancy()
    {
        // Arrange
        var instance = new ArcInstance { ArcRef = "TestArc" };

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.AvatarEntered,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new() { ["TriggerRef"] = "approach" }
        });

        // Act / Assert - entered: the trigger's ring is occupied
        var entered = _stateMachine.ReplayToNow(instance);
        Assert.Contains("Avatar1", entered.Triggers["approach"].OccupyingAvatars);

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.AvatarExited,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["TriggerRef"] = "approach" }
        });

        // Act / Assert - exited: occupancy cleared (this is what gates exit
        // emission in ArcInteractionService, so exits are once-per-transition)
        var exited = _stateMachine.ReplayToNow(instance);
        Assert.DoesNotContain("Avatar1", exited.Triggers["approach"].OccupyingAvatars);
    }

    #endregion

    #region Snapshot Resume Template Merge (audit C4)

    [Fact]
    public void Replay_SnapshotPredatesTemplateTrigger_MergesAndActivatesNewTrigger()
    {
        // Arrange - snapshot was taken when the arc template only had "approach";
        // the current template also has "inner" (added by a content update)
        var oldMachine = new ArcStateMachine(
            _testArc,
            new List<ArcTrigger> { _testArcTriggers[0] }, // "approach" only
            _testWorld);

        var snapshotTx = oldMachine.CreateSnapshotTransaction(new ArcInstance { ArcRef = "TestArc" });
        snapshotTx.Status = TransactionStatus.Committed;
        snapshotTx.SequenceNumber = 1;

        var instance = new ArcInstance { ArcRef = "TestArc" };
        instance.AddTransaction(snapshotTx);

        // A trigger unknown to the snapshot activates after the snapshot
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TriggerActivated,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["ArcTriggerRef"] = "inner" }
        });

        // Act - replay with the CURRENT template (approach + inner)
        var state = _stateMachine.ReplayToNow(instance);

        // Assert - "inner" was merged into the restored snapshot state and its
        // activation applied instead of being silently discarded
        Assert.True(state.Triggers.ContainsKey("inner"));
        Assert.Equal(ArcTriggerStatus.Active, state.Triggers["inner"].Status);
        Assert.Equal(1, state.Triggers["inner"].ActivationCount);
        Assert.Contains("Avatar1", state.Triggers["inner"].TriggeredByAvatars);

        // The snapshot's own trigger is untouched
        Assert.True(state.Triggers.ContainsKey("approach"));
        Assert.Equal(ArcTriggerStatus.Inactive, state.Triggers["approach"].Status);
    }

    #endregion
}
