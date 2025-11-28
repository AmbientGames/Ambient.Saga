using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Tests.Rpg.Sagas;

/// <summary>
/// Unit tests for SagaStateMachine replay logic.
/// Verifies deterministic, idempotent state derivation from transaction logs.
/// </summary>
public class SagaStateMachineTests
{
    private readonly SagaArc _testSagaArc;
    private readonly List<SagaTrigger> _testSagaTriggers;
    private readonly World _testWorld;
    private readonly SagaStateMachine _stateMachine;

    public SagaStateMachineTests()
    {
        // Create minimal test Saga template
        _testSagaArc = new SagaArc
        {
            RefName = "TestSaga",
            DisplayName = "Test Saga",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };

        // Create test triggers
        _testSagaTriggers = new List<SagaTrigger>
        {
            new SagaTrigger
            {
                RefName = "approach",
                DisplayName = "Approach Trigger",
                EnterRadius = 50.0f
            },
            new SagaTrigger
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

        _stateMachine = new SagaStateMachine(_testSagaArc, _testSagaTriggers, _testWorld);
    }

    [Fact]
    public void ReplayToNow_EmptyTransactionLog_ReturnsInitialState()
    {
        // Arrange
        var instance = new SagaInstance
        {
            SagaRef = "TestSaga",
            InstanceType = SagaInstanceType.SinglePlayer
        };

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal("TestSaga", state.SagaRef);
        Assert.Equal(SagaStatus.Undiscovered, state.Status);
        Assert.Null(state.FirstDiscoveredAt);
        Assert.Equal(2, state.Triggers.Count);
        Assert.True(state.Triggers.ContainsKey("approach"));
        Assert.True(state.Triggers.ContainsKey("inner"));
        Assert.Equal(SagaTriggerStatus.Inactive, state.Triggers["approach"].Status);
    }

    [Fact]
    public void ReplayToNow_SagaDiscoveredTransaction_UpdatesStatusAndTimestamp()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var discoveryTime = DateTime.UtcNow;

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.SagaDiscovered,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            LocalTimestamp = discoveryTime,
            ServerTimestamp = discoveryTime,
            SequenceNumber = 1
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(SagaStatus.Active, state.Status);
        Assert.Equal(discoveryTime, state.FirstDiscoveredAt);
        Assert.Contains("Player1", state.DiscoveredByAvatars);
    }

    [Fact]
    public void ReplayToNow_CharacterSpawnedAndDefeated_TracksLifecycle()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var characterId = Guid.NewGuid();
        var spawnTime = DateTime.UtcNow;
        var defeatTime = spawnTime.AddMinutes(5);

        // Character spawned
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            ServerTimestamp = spawnTime,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["SagaTriggerRef"] = "approach",
                ["LatitudeZ"] = "35.0",
                ["LongitudeX"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Character defeated
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDefeated,
            AvatarId = "Player1",
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
    public void ReplayToNow_CharacterDamaged_TracksHealthAndDamageByPlayer()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var characterId = Guid.NewGuid();

        // Spawn
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            ServerTimestamp = DateTime.UtcNow,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["LatitudeZ"] = "35.0",
                ["LongitudeX"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Player1 damages 30%
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDamaged,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = DateTime.UtcNow.AddSeconds(1),
            SequenceNumber = 2,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["Damage"] = "0.3"
            }
        });

        // Player2 damages 50%
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDamaged,
            AvatarId = "Player2",
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
        Assert.Equal(0.3, character.DamageByPlayer["Player1"]);
        Assert.Equal(0.5, character.DamageByPlayer["Player2"]);
    }

    [Fact]
    public void ReplayToNow_CharacterHealed_RestoresHealth()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["LatitudeZ"] = "35.0",
                ["LongitudeX"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDamaged,
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString(), ["Damage"] = "0.6" }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterHealed,
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
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var activationTime = DateTime.UtcNow;

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.TriggerActivated,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = activationTime,
            SequenceNumber = 1,
            Data = new() { ["SagaTriggerRef"] = "approach" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var trigger = state.Triggers["approach"];
        Assert.Equal(SagaTriggerStatus.Active, trigger.Status);
        Assert.Equal(1, trigger.ActivationCount);
        Assert.Equal(activationTime, trigger.FirstActivatedAt);
        Assert.Equal(activationTime, trigger.LastActivatedAt);
        Assert.Contains("Player1", trigger.TriggeredByAvatars);
    }

    [Fact]
    public void ReplayToNow_TriggerActivatedMultipleTimes_IncrementsCount()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.TriggerActivated,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new() { ["SagaTriggerRef"] = "approach" }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.TriggerActivated,
            AvatarId = "Player2",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["SagaTriggerRef"] = "approach" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var trigger = state.Triggers["approach"];
        Assert.Equal(2, trigger.ActivationCount);
        Assert.Contains("Player1", trigger.TriggeredByAvatars);
        Assert.Contains("Player2", trigger.TriggeredByAvatars);
    }

    [Fact]
    public void ReplayToNow_TriggerCompleted_UpdatesStatusAndTimestamp()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var completionTime = DateTime.UtcNow;

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.TriggerCompleted,
            Status = TransactionStatus.Committed,
            ServerTimestamp = completionTime,
            SequenceNumber = 1,
            Data = new() { ["SagaTriggerRef"] = "approach" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        var trigger = state.Triggers["approach"];
        Assert.Equal(SagaTriggerStatus.Completed, trigger.Status);
        Assert.Equal(completionTime, trigger.CompletedAt);
    }

    [Fact]
    public void ReplayToNow_SagaCompleted_UpdatesStatusAndAvatars()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var completionTime = DateTime.UtcNow;

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.SagaCompleted,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = completionTime,
            SequenceNumber = 1
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(SagaStatus.Completed, state.Status);
        Assert.Equal(completionTime, state.CompletedAt);
        Assert.Contains("Player1", state.CompletedByAvatars);
    }

    [Fact]
    public void ReplayToNow_Deterministic_AlwaysProducesSameResult()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "Boss",
                ["LatitudeZ"] = "35.0",
                ["LongitudeX"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDamaged,
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
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var characterId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow;

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "Boss",
                ["LatitudeZ"] = "35.0",
                ["LongitudeX"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDamaged,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddMinutes(1),
            SequenceNumber = 2,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString(), ["Damage"] = "0.3" }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDefeated,
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
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var characterId = Guid.NewGuid();

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = characterId.ToString(),
                ["CharacterRef"] = "Boss",
                ["LatitudeZ"] = "35.0",
                ["LongitudeX"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDamaged,
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["CharacterInstanceId"] = characterId.ToString(), ["Damage"] = "0.5" }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDefeated,
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
        var instance = new SagaInstance { SagaRef = "TestSaga" };

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.SagaDiscovered,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.TriggerActivated,
            Status = TransactionStatus.Pending,  // Not committed yet
            SequenceNumber = 2,
            Data = new() { ["SagaTriggerRef"] = "approach" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(SagaStatus.Active, state.Status);  // Discovery processed
        Assert.Equal(SagaTriggerStatus.Inactive, state.Triggers["approach"].Status);  // Trigger not processed (pending)
        Assert.Equal(1, state.TransactionCount);  // Only 1 committed transaction
    }

    [Fact]
    public void ReplayToNow_ComplexScenario_BossDefeatWithMultiplePlayers()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };
        var bossId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow;

        // Saga discovered
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.SagaDiscovered,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime,
            SequenceNumber = 1
        });

        // Trigger activated
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.TriggerActivated,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(5),
            SequenceNumber = 2,
            Data = new() { ["SagaTriggerRef"] = "approach" }
        });

        // Boss spawned
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(10),
            SequenceNumber = 3,
            Data = new()
            {
                ["CharacterInstanceId"] = bossId.ToString(),
                ["CharacterRef"] = "TestBoss",
                ["SagaTriggerRef"] = "approach",
                ["LatitudeZ"] = "35.0",
                ["LongitudeX"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Player1 damages 40%
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDamaged,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(15),
            SequenceNumber = 4,
            Data = new() { ["CharacterInstanceId"] = bossId.ToString(), ["Damage"] = "0.4" }
        });

        // Player2 enters and damages 60%
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.PlayerEntered,
            AvatarId = "Player2",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(20),
            SequenceNumber = 5
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDamaged,
            AvatarId = "Player2",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(25),
            SequenceNumber = 6,
            Data = new() { ["CharacterInstanceId"] = bossId.ToString(), ["Damage"] = "0.6" }
        });

        // Boss defeated
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.CharacterDefeated,
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(30),
            SequenceNumber = 7,
            Data = new() { ["CharacterInstanceId"] = bossId.ToString() }
        });

        // Saga completed
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.SagaCompleted,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            ServerTimestamp = baseTime.AddSeconds(35),
            SequenceNumber = 8
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(SagaStatus.Completed, state.Status);
        Assert.Contains("Player1", state.DiscoveredByAvatars);
        Assert.Contains("Player1", state.CompletedByAvatars);

        var trigger = state.Triggers["approach"];
        Assert.Equal(SagaTriggerStatus.Active, trigger.Status);
        Assert.Equal(1, trigger.ActivationCount);

        var boss = state.Characters[bossId.ToString()];
        Assert.False(boss.IsAlive);
        Assert.Equal(0.0, boss.CurrentHealth);
        Assert.Equal(0.4, boss.DamageByPlayer["Player1"]);
        Assert.Equal(0.6, boss.DamageByPlayer["Player2"]);
        Assert.Equal("approach", boss.SpawnedByTriggerRef);

        Assert.Equal(8, state.TransactionCount);
    }

    [Fact]
    public void ReplayToNow_QuestTokenAwarded_TrackedInState()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };

        // Trigger activated and awarded quest token
        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.TriggerActivated,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new() { ["SagaTriggerRef"] = "approach" }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.QuestTokenAwarded,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new()
            {
                ["QuestTokenRef"] = "ApproachComplete",
                ["SagaTriggerRef"] = "approach",
                ["Reason"] = "Trigger 'approach' activated"
            }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(SagaStatus.Undiscovered, state.Status);
        Assert.Equal(SagaTriggerStatus.Active, state.Triggers["approach"].Status);
        Assert.Equal(2, state.TransactionCount);
        // Quest tokens are tracked in avatar inventory, not Saga state
        // This test just verifies the transaction is processed without errors
    }

    [Fact]
    public void ReplayToNow_MultipleQuestTokensAwarded_AllProcessed()
    {
        // Arrange
        var instance = new SagaInstance { SagaRef = "TestSaga" };

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.QuestTokenAwarded,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new() { ["QuestTokenRef"] = "Token1", ["SagaTriggerRef"] = "approach" }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.QuestTokenAwarded,
            AvatarId = "Player1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new() { ["QuestTokenRef"] = "Token2", ["SagaTriggerRef"] = "approach" }
        });

        instance.AddTransaction(new SagaTransaction
        {
            Type = SagaTransactionType.QuestTokenAwarded,
            AvatarId = "Player2",
            Status = TransactionStatus.Committed,
            SequenceNumber = 3,
            Data = new() { ["QuestTokenRef"] = "Token1", ["SagaTriggerRef"] = "inner" }
        });

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert - All transactions processed successfully
        Assert.Equal(3, state.TransactionCount);
    }
}
