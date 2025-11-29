using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Evaluation;
using Ambient.Saga.Engine.Domain.Rpg.Reputation;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for the full Faction/Reputation system.
/// Tests dialogue conditions, transaction logging, spillover, and achievement integration.
/// </summary>
public class FactionReputationIntegrationTests : IDisposable
{
    private readonly WorldFactory _worldFactory;
    private World _world = null!;

    public FactionReputationIntegrationTests()
    {
        _worldFactory = new WorldFactory();
    }

    public void Dispose()
    {
    }

    [Fact]
    public void ReputationChanged_Transaction_UpdatesFactionReputation()
    {
        // Arrange
        _world = _worldFactory.CreateMinimalWorld();

        // Add faction to world
        var faction = new Faction
        {
            RefName = "KNIGHTS_OF_VALOR",
            DisplayName = "Knights of Valor",
            Category = FactionCategory.Military,
            StartingReputation = 0
        };
        _world.FactionsLookup["KNIGHTS_OF_VALOR"] = faction;

        // Create Saga instance and state machine
        var instance = new SagaInstance
        {
            InstanceId = Guid.NewGuid(),
            SagaRef = "TEST_SAGA",
            OwnerAvatarId = Guid.NewGuid()
        };

        var sagaTemplate = new SagaArc
        {
            RefName = "TEST_SAGA",
            DisplayName = "Test Saga"
        };

        var stateMachine = new SagaStateMachine(sagaTemplate, new List<SagaTrigger>(), _world);

        // Create ReputationChanged transaction
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ReputationChanged,
            AvatarId = "avatar_1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["FactionRef"] = "KNIGHTS_OF_VALOR",
                ["ReputationChange"] = "100"
            }
        };

        instance.AddTransaction(transaction);

        // Act
        var state = stateMachine.ReplayToNow(instance);

        // Assert
        Assert.True(state.FactionReputation.ContainsKey("KNIGHTS_OF_VALOR"));
        Assert.Equal(100, state.FactionReputation["KNIGHTS_OF_VALOR"]);
        Assert.Equal(ReputationLevel.Neutral, ReputationManager.GetReputationLevel(100));
    }

    [Fact]
    public void ReputationSpillover_AlliedFactions_ReceivesBonusReputation()
    {
        // Arrange
        _world = _worldFactory.CreateMinimalWorld();

        var knightsFaction = new Faction
        {
            RefName = "KNIGHTS_OF_VALOR",
            DisplayName = "Knights of Valor",
            Category = FactionCategory.Military,
            StartingReputation = 0,
            Relationships = new[]
            {
                new FactionRelationship
                {
                    FactionRef = "CITY_OF_HAVEN",
                    RelationshipType = FactionRelationshipRelationshipType.Allied,
                    SpilloverPercent = 0.25f
                },
                new FactionRelationship
                {
                    FactionRef = "DARK_CULTISTS",
                    RelationshipType = FactionRelationshipRelationshipType.Enemy,
                    SpilloverPercent = 0.5f
                }
            }
        };

        var cityFaction = new Faction
        {
            RefName = "CITY_OF_HAVEN",
            DisplayName = "City of Haven",
            Category = FactionCategory.City
        };

        var cultistsFaction = new Faction
        {
            RefName = "DARK_CULTISTS",
            DisplayName = "Dark Cultists",
            Category = FactionCategory.Criminal
        };

        _world.FactionsLookup["KNIGHTS_OF_VALOR"] = knightsFaction;
        _world.FactionsLookup["CITY_OF_HAVEN"] = cityFaction;
        _world.FactionsLookup["DARK_CULTISTS"] = cultistsFaction;

        var currentReputation = new Dictionary<string, int>
        {
            ["KNIGHTS_OF_VALOR"] = 0,
            ["CITY_OF_HAVEN"] = 0,
            ["DARK_CULTISTS"] = 0
        };

        // Act
        var changes = ReputationManager.ApplyReputationChange(
            currentReputation,
            _world.FactionsLookup,
            "KNIGHTS_OF_VALOR",
            1000);

        // Assert
        Assert.Equal(1000, changes["KNIGHTS_OF_VALOR"]);   // Direct gain
        Assert.Equal(250, changes["CITY_OF_HAVEN"]);       // 25% Allied spillover
        Assert.Equal(-500, changes["DARK_CULTISTS"]);      // 50% Enemy loss

        Assert.Equal(1000, currentReputation["KNIGHTS_OF_VALOR"]);
        Assert.Equal(250, currentReputation["CITY_OF_HAVEN"]);
        Assert.Equal(-500, currentReputation["DARK_CULTISTS"]);
    }

    [Fact]
    public void DialogueCondition_ReputationLevel_FiltersChoicesCorrectly()
    {
        // Arrange
        _world = _worldFactory.CreateMinimalWorld();
        var avatarEntity = _worldFactory.CreateTestAvatar();

        var faction = new Faction
        {
            RefName = "KNIGHTS_OF_VALOR",
            DisplayName = "Knights of Valor",
            StartingReputation = 0  // Neutral
        };
        _world.FactionsLookup["KNIGHTS_OF_VALOR"] = faction;

        // Create mock Saga state with reputation at Friendly level (3000)
        var sagaState = new SagaState
        {
            SagaRef = "TEST_SAGA",
            FactionReputation = new Dictionary<string, int>
            {
                ["KNIGHTS_OF_VALOR"] = 3000  // Friendly level
            }
        };

        var stateProvider = new DirectDialogueStateProvider(
            _world,
            avatarEntity,
            _ => sagaState,
            "avatar_1");

        var evaluator = new DialogueConditionEvaluator(stateProvider);

        // Condition 1: ReputationLevel >= Friendly (should pass)
        var condition1 = new DialogueCondition
        {
            Type = DialogueConditionType.ReputationLevel,
            FactionRef = "KNIGHTS_OF_VALOR",
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "Friendly"
        };

        // Condition 2: ReputationLevel >= Honored (should fail - only at Friendly)
        var condition2 = new DialogueCondition
        {
            Type = DialogueConditionType.ReputationLevel,
            FactionRef = "KNIGHTS_OF_VALOR",
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "Honored"
        };

        // Act & Assert
        Assert.True(evaluator.Evaluate(condition1));
        Assert.False(evaluator.Evaluate(condition2));
    }

    [Fact]
    public void DialogueCondition_ReputationValue_ComparesNumericReputation()
    {
        // Arrange
        _world = _worldFactory.CreateMinimalWorld();
        var avatarEntity = _worldFactory.CreateTestAvatar();

        var faction = new Faction
        {
            RefName = "TEST_FACTION",
            DisplayName = "Test Faction",
            StartingReputation = 0
        };
        _world.FactionsLookup["TEST_FACTION"] = faction;

        var sagaState = new SagaState
        {
            SagaRef = "TEST_SAGA",
            FactionReputation = new Dictionary<string, int>
            {
                ["TEST_FACTION"] = 5000  // Friendly level (3000-9000)
            }
        };

        var stateProvider = new DirectDialogueStateProvider(
            _world,
            avatarEntity,
            _ => sagaState,
            "avatar_1");

        var evaluator = new DialogueConditionEvaluator(stateProvider);

        var condition1 = new DialogueCondition
        {
            Type = DialogueConditionType.ReputationValue,
            FactionRef = "TEST_FACTION",
            Operator = ComparisonOperator.GreaterThan,
            Value = "3000"
        };

        var condition2 = new DialogueCondition
        {
            Type = DialogueConditionType.ReputationValue,
            FactionRef = "TEST_FACTION",
            Operator = ComparisonOperator.LessThan,
            Value = "9000"
        };

        // Act & Assert
        Assert.True(evaluator.Evaluate(condition1));  // 5000 > 3000
        Assert.True(evaluator.Evaluate(condition2));  // 5000 < 9000
    }

    [Fact]
    public void ReputationRewards_AtHonoredLevel_UnlocksCorrectRewards()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "KNIGHTS_OF_VALOR",
            DisplayName = "Knights of Valor",
            ReputationRewards = new[]
            {
                new ReputationReward
                {
                    RequiredLevel = ReputationLevel.Friendly,
                    Equipment = new[]
                    {
                        new ReputationRewardEquipment
                        {
                            EquipmentRef = "BASIC_SWORD",
                            DiscountPercent = 5
                        }
                    }
                },
                new ReputationReward
                {
                    RequiredLevel = ReputationLevel.Honored,
                    Equipment = new[]
                    {
                        new ReputationRewardEquipment
                        {
                            EquipmentRef = "LEGENDARY_SWORD",
                            DiscountPercent = 10
                        }
                    }
                },
                new ReputationReward
                {
                    RequiredLevel = ReputationLevel.Exalted,
                    Equipment = new[]
                    {
                        new ReputationRewardEquipment
                        {
                            EquipmentRef = "EPIC_ARMOR",
                            DiscountPercent = 50
                        }
                    }
                }
            }
        };

        // Act - at Honored level (9000 reputation)
        var rewards = ReputationManager.GetAvailableRewards(faction, 9000);

        // Assert - Should unlock Friendly and Honored rewards, but not Exalted
        Assert.Equal(2, rewards.Length);
        Assert.Contains(rewards, r => r.RequiredLevel == ReputationLevel.Friendly);
        Assert.Contains(rewards, r => r.RequiredLevel == ReputationLevel.Honored);
        Assert.DoesNotContain(rewards, r => r.RequiredLevel == ReputationLevel.Exalted);
    }

    [Fact]
    public void MultipleReputationChanges_Accumulate_CorrectlyInState()
    {
        // Arrange
        _world = _worldFactory.CreateMinimalWorld();

        var faction = new Faction
        {
            RefName = "TEST_FACTION",
            DisplayName = "Test Faction",
            StartingReputation = 0
        };
        _world.FactionsLookup["TEST_FACTION"] = faction;

        var instance = new SagaInstance
        {
            InstanceId = Guid.NewGuid(),
            SagaRef = "TEST_SAGA",
            OwnerAvatarId = Guid.NewGuid()
        };

        var sagaTemplate = new SagaArc
        {
            RefName = "TEST_SAGA",
            DisplayName = "Test Saga"
        };

        var stateMachine = new SagaStateMachine(sagaTemplate, new List<SagaTrigger>(), _world);

        // Add multiple reputation change transactions
        instance.AddTransaction(new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ReputationChanged,
            AvatarId = "avatar_1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["FactionRef"] = "TEST_FACTION",
                ["ReputationChange"] = "1000"
            }
        });

        instance.AddTransaction(new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ReputationChanged,
            AvatarId = "avatar_1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["FactionRef"] = "TEST_FACTION",
                ["ReputationChange"] = "500"
            }
        });

        instance.AddTransaction(new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ReputationChanged,
            AvatarId = "avatar_1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 3,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["FactionRef"] = "TEST_FACTION",
                ["ReputationChange"] = "-300"
            }
        });

        // Act
        var state = stateMachine.ReplayToNow(instance);

        // Assert
        Assert.Equal(1200, state.FactionReputation["TEST_FACTION"]);  // 1000 + 500 - 300
        Assert.Equal(ReputationLevel.Neutral, ReputationManager.GetReputationLevel(1200));
    }

    [Fact]
    public void ReputationLevel_CrossingThreshold_ReturnsNewLevel()
    {
        // Arrange & Act & Assert
        // Start at Neutral (0), gain 3000 reputation → should be Friendly
        Assert.Equal(ReputationLevel.Neutral, ReputationManager.GetReputationLevel(0));
        Assert.Equal(ReputationLevel.Neutral, ReputationManager.GetReputationLevel(2999));
        Assert.Equal(ReputationLevel.Friendly, ReputationManager.GetReputationLevel(3000));
        Assert.Equal(ReputationLevel.Friendly, ReputationManager.GetReputationLevel(8999));
        Assert.Equal(ReputationLevel.Honored, ReputationManager.GetReputationLevel(9000));
        Assert.Equal(ReputationLevel.Honored, ReputationManager.GetReputationLevel(20999));
        Assert.Equal(ReputationLevel.Revered, ReputationManager.GetReputationLevel(21000));
        Assert.Equal(ReputationLevel.Revered, ReputationManager.GetReputationLevel(41999));
        Assert.Equal(ReputationLevel.Exalted, ReputationManager.GetReputationLevel(42000));
        Assert.Equal(ReputationLevel.Exalted, ReputationManager.GetReputationLevel(100000));
    }

    [Fact]
    public void StartingReputation_NonZero_InitializesCorrectly()
    {
        // Arrange
        _world = _worldFactory.CreateMinimalWorld();
        var avatarEntity = _worldFactory.CreateTestAvatar();

        var faction = new Faction
        {
            RefName = "FRIENDLY_FACTION",
            DisplayName = "Friendly Faction",
            StartingReputation = 3000  // Start at Friendly
        };
        _world.FactionsLookup["FRIENDLY_FACTION"] = faction;

        // Create state provider with no Saga state (first interaction)
        var stateProvider = new DirectDialogueStateProvider(
            _world,
            avatarEntity,
            _ => null,  // No Saga state yet
            "avatar_1");

        // Act
        var reputation = stateProvider.GetFactionReputation("FRIENDLY_FACTION");
        var level = stateProvider.GetFactionReputationLevel("FRIENDLY_FACTION");

        // Assert
        Assert.Equal(3000, reputation);
        Assert.Equal("Friendly", level);
    }
}
