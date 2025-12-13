using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests.Rpg.Quests;

/// <summary>
/// Unit tests for QuestProgressEvaluator which computes quest progress from transaction logs.
/// </summary>
public class QuestProgressEvaluatorTests
{
    private const string TestAvatarId = "avatar-123";
    private const string TestSagaRef = "TestSaga";

    private World CreateTestWorld()
    {
        return new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };
    }

    private SagaTransaction CreateTransaction(SagaTransactionType type, Dictionary<string, string>? data = null)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = TestAvatarId,
            Status = TransactionStatus.Committed,
            LocalTimestamp = DateTime.UtcNow,
            Data = data ?? new Dictionary<string, string>()
        };

        return transaction;
    }

    private Quest CreateTestQuest()
    {
        // Note: This will use generated classes once XSD is regenerated
        // For now, we'll create a simple quest structure
        return new Quest
        {
            RefName = "TEST_QUEST",
            DisplayName = "Test Quest"
        };
    }

    #region CharacterDefeated Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_CharacterDefeated_CountsCorrectly()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        // Create objective: Defeat 5 dragons
        var objective = new QuestObjective
        {
            RefName = "DEFEAT_DRAGONS",
            Type = QuestObjectiveType.CharactersDefeatedByTag,
            CharacterTag = "dragon",
            Threshold = 5
        };

        var stage = new QuestStage
        {
            RefName = "HUNT",
            DisplayName = "Hunt Dragons"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "FROST_DRAGON",
                ["CharacterTag"] = "dragon"
            }),
            CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "FIRE_DRAGON",
                ["CharacterTag"] = "dragon"
            }),
            CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "SHADOW_DRAGON",
                ["CharacterTag"] = "dragon"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(3, progress);
    }

    [Fact]
    public void IsObjectiveComplete_WhenThresholdMet_ReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "DEFEAT_BOSS",
            Type = QuestObjectiveType.CharacterDefeated,
            CharacterRef = "DRAGON_BOSS",
            Threshold = 1
        };

        var stage = new QuestStage
        {
            RefName = "BOSS_FIGHT",
            DisplayName = "Boss Fight"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "DRAGON_BOSS"
            })
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsObjectiveComplete(quest, stage, objective, transactions, world);

        // Assert
        Assert.True(isComplete);
    }

    [Fact]
    public void IsObjectiveComplete_WhenThresholdNotMet_ReturnsFalse()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "DEFEAT_DRAGONS",
            Type = QuestObjectiveType.CharactersDefeatedByTag,
            CharacterTag = "dragon",
            Threshold = 5
        };

        var stage = new QuestStage
        {
            RefName = "HUNT",
            DisplayName = "Hunt Dragons"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "FROST_DRAGON",
                ["CharacterTag"] = "dragon"
            })
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsObjectiveComplete(quest, stage, objective, transactions, world);

        // Assert
        Assert.False(isComplete);
    }

    #endregion

    #region DialogueCompleted Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_DialogueCompleted_CountsCorrectly()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "TALK_WITNESSES",
            Type = QuestObjectiveType.DialogueCompleted,
            CharacterTag = "witness",
            Threshold = 3
        };

        var stage = new QuestStage
        {
            RefName = "INVESTIGATION",
            DisplayName = "Investigation"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_1",
                ["CharacterTag"] = "witness"
            }),
            CreateTransaction(SagaTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_2",
                ["CharacterTag"] = "witness"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(2, progress);
    }

    #endregion

    #region ItemCollected Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_ItemCollected_CountsQuantity()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "COLLECT_HERBS",
            Type = QuestObjectiveType.ItemCollected,
            ItemRef = "MOONFLOWER",
            Threshold = 5
        };

        var stage = new QuestStage
        {
            RefName = "GATHER",
            DisplayName = "Gather Herbs"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.LootAwarded, new Dictionary<string, string>
            {
                ["ItemRef"] = "MOONFLOWER",
                ["Quantity"] = "3"
            }),
            CreateTransaction(SagaTransactionType.LootAwarded, new Dictionary<string, string>
            {
                ["ItemRef"] = "MOONFLOWER",
                ["Quantity"] = "2"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(5, progress);
    }

    #endregion

    #region IsStageComplete Tests

    [Fact]
    public void IsStageComplete_AllObjectivesComplete_ReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var stage = new QuestStage
        {
            RefName = "INVESTIGATION",
            DisplayName = "Investigation",
            Objectives = new QuestStageObjectives
            {
                Objective = new[]
                {
                    new QuestObjective
                    {
                        RefName = "TALK_WITNESSES",
                        Type = QuestObjectiveType.DialogueCompleted,
                        CharacterTag = "witness",
                        Threshold = 2,
                        Optional = false
                    },
                    new QuestObjective
                    {
                        RefName = "FIND_WEAPON",
                        Type = QuestObjectiveType.ItemCollected,
                        ItemRef = "BLOODY_KNIFE",
                        Threshold = 1,
                        Optional = false
                    }
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_1",
                ["CharacterTag"] = "witness"
            }),
            CreateTransaction(SagaTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_2",
                ["CharacterTag"] = "witness"
            }),
            CreateTransaction(SagaTransactionType.LootAwarded, new Dictionary<string, string>
            {
                ["ItemRef"] = "BLOODY_KNIFE",
                ["Quantity"] = "1"
            })
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsStageComplete(quest, stage, transactions, world);

        // Assert
        Assert.True(isComplete);
    }

    [Fact]
    public void IsStageComplete_OptionalObjectiveIncomplete_StillReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var stage = new QuestStage
        {
            RefName = "ESCORT",
            DisplayName = "Escort Mission",
            Objectives = new QuestStageObjectives
            {
                Objective = new[]
                {
                    new QuestObjective
                    {
                        RefName = "REACH_TOWN",
                        Type = QuestObjectiveType.LocationReached,
                        LocationRef = "TOWN",
                        Threshold = 1,
                        Optional = false
                    },
                    new QuestObjective
                    {
                        RefName = "DEFEAT_BANDITS",
                        Type = QuestObjectiveType.CharactersDefeatedByTag,
                        CharacterTag = "bandit",
                        Threshold = 5,
                        Optional = true
                    }
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.TriggerActivated, new Dictionary<string, string>
            {
                ["TriggerRef"] = "TOWN"
            })
            // Bandits not defeated - but it's optional
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsStageComplete(quest, stage, transactions, world);

        // Assert
        Assert.True(isComplete);
    }

    [Fact]
    public void IsStageComplete_RequiredObjectiveIncomplete_ReturnsFalse()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var stage = new QuestStage
        {
            RefName = "INVESTIGATION",
            DisplayName = "Investigation",
            Objectives = new QuestStageObjectives
            {
                Objective = new[]
                {
                    new QuestObjective
                    {
                        RefName = "TALK_WITNESSES",
                        Type = QuestObjectiveType.DialogueCompleted,
                        CharacterTag = "witness",
                        Threshold = 3,
                        Optional = false
                    },
                    new QuestObjective
                    {
                        RefName = "FIND_WEAPON",
                        Type = QuestObjectiveType.ItemCollected,
                        ItemRef = "BLOODY_KNIFE",
                        Threshold = 1,
                        Optional = false
                    }
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.DialogueCompleted, new Dictionary<string, string>
            {
                ["CharacterRef"] = "WITNESS_1",
                ["CharacterTag"] = "witness"
            })
            // Only 1 witness talked to (need 3), weapon not found
        };

        // Act
        var isComplete = QuestProgressEvaluator.IsStageComplete(quest, stage, transactions, world);

        // Assert
        Assert.False(isComplete);
    }

    #endregion

    #region GetNextStage Tests

    [Fact]
    public void GetNextStage_WithNextStageAttribute_ReturnsNextStage()
    {
        // Arrange
        var quest = CreateTestQuest();

        var currentStage = new QuestStage
        {
            RefName = "INVESTIGATION",
            DisplayName = "Investigation",
            NextStage = "ACCUSATION"
        };

        var transactions = new List<SagaTransaction>();

        // Act
        var nextStage = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions);

        // Assert
        Assert.Equal("ACCUSATION", nextStage);
    }

    [Fact]
    public void GetNextStage_WithBranchChoice_ReturnsBranchNextStage()
    {
        // Arrange
        var quest = CreateTestQuest();

        var currentStage = new QuestStage
        {
            RefName = "CHOOSE_SIDE",
            DisplayName = "Choose Your Side",
            Branches = new QuestStageBranches
            {
                Branch = new[]
                {
                    new QuestBranch
                    {
                        RefName = "JOIN_REBELS",
                        DisplayName = "Join Rebels",
                        NextStage = "REBEL_MISSIONS"
                    },
                    new QuestBranch
                    {
                        RefName = "JOIN_EMPIRE",
                        DisplayName = "Join Empire",
                        NextStage = "EMPIRE_MISSIONS"
                    }
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.QuestBranchChosen, new Dictionary<string, string>
            {
                ["QuestRef"] = "TEST_QUEST",
                ["StageRef"] = "CHOOSE_SIDE",
                ["BranchRef"] = "JOIN_REBELS"
            })
        };

        // Act
        var nextStage = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions);

        // Assert
        Assert.Equal("REBEL_MISSIONS", nextStage);
    }

    [Fact]
    public void GetNextStage_NoNextStage_ReturnsNull()
    {
        // Arrange
        var quest = CreateTestQuest();

        var currentStage = new QuestStage
        {
            RefName = "FINAL_STAGE",
            DisplayName = "Final Stage"
            // NextStage = null (quest complete)
        };

        var transactions = new List<SagaTransaction>();

        // Act
        var nextStage = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions);

        // Assert
        Assert.Null(nextStage);
    }

    #endregion

    #region CheckFailConditions Tests

    [Fact]
    public void CheckFailConditions_CharacterDied_ReturnsFailed()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "ESCORT_MISSION",
            DisplayName = "Escort Mission",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.CharacterDied,
                    CharacterRef = "MERCHANT_NPC"
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.CharacterDefeated, new Dictionary<string, string>
            {
                ["CharacterRef"] = "MERCHANT_NPC"
            })
        };

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions);

        // Assert
        Assert.True(failed);
        Assert.Contains("MERCHANT_NPC died", reason);
    }

    [Fact]
    public void CheckFailConditions_NoFailConditionsMet_ReturnsFalse()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "ESCORT_MISSION",
            DisplayName = "Escort Mission",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.CharacterDied,
                    CharacterRef = "MERCHANT_NPC"
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            // Merchant still alive
        };

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions);

        // Assert
        Assert.False(failed);
        Assert.Null(reason);
    }

    [Fact]
    public void CheckFailConditions_WrongChoiceMade_ReturnsFailed()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "MURDER_MYSTERY",
            DisplayName = "Murder Mystery",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.WrongChoiceMade,
                    DialogueRef = "FINAL_ACCUSATION",
                    ChoiceRef = "GARDENER"
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.DialogueNodeVisited, new Dictionary<string, string>
            {
                ["DialogueRef"] = "FINAL_ACCUSATION",
                ["ChoiceRef"] = "GARDENER"
            })
        };

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions);

        // Assert
        Assert.True(failed);
        Assert.Contains("Wrong choice made", reason);
    }

    [Fact]
    public void CheckFailConditions_TimeExpired_ReturnsFailed_WhenTimeLimitExceeded()
    {
        // Arrange
        var questStartTime = DateTime.UtcNow.AddMinutes(-10); // Quest started 10 minutes ago
        var quest = new Quest
        {
            RefName = "TIMED_QUEST",
            DisplayName = "Timed Quest",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.TimeExpired,
                    TimeLimit = 300, // 5 minutes (300 seconds)
                    TimeLimitSpecified = true
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestAccepted,
                AvatarId = TestAvatarId,
                Status = TransactionStatus.Committed,
                LocalTimestamp = questStartTime, // Quest accepted 10 minutes ago
                Data = new Dictionary<string, string>
                {
                    ["QuestRef"] = "TIMED_QUEST"
                }
            }
        };

        var currentTime = DateTime.UtcNow; // 10 minutes after quest start

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions, currentTime);

        // Assert
        Assert.True(failed);
        Assert.Contains("Time limit expired", reason);
    }

    [Fact]
    public void CheckFailConditions_TimeExpired_ReturnsNotFailed_WhenWithinTimeLimit()
    {
        // Arrange
        var questStartTime = DateTime.UtcNow.AddMinutes(-2); // Quest started 2 minutes ago
        var quest = new Quest
        {
            RefName = "TIMED_QUEST",
            DisplayName = "Timed Quest",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.TimeExpired,
                    TimeLimit = 300, // 5 minutes (300 seconds)
                    TimeLimitSpecified = true
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestAccepted,
                AvatarId = TestAvatarId,
                Status = TransactionStatus.Committed,
                LocalTimestamp = questStartTime,
                Data = new Dictionary<string, string>
                {
                    ["QuestRef"] = "TIMED_QUEST"
                }
            }
        };

        var currentTime = DateTime.UtcNow; // 2 minutes after quest start

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions, currentTime);

        // Assert
        Assert.False(failed);
        Assert.Null(reason);
    }

    [Fact]
    public void CheckFailConditions_ItemLost_ReturnsFailed_WhenRequiredItemSold()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "DELIVERY_QUEST",
            DisplayName = "Delivery Quest",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.ItemLost,
                    ItemRef = "SACRED_ARTIFACT"
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            // Player obtained the item
            CreateTransaction(SagaTransactionType.LootAwarded, new Dictionary<string, string>
            {
                ["ItemRef"] = "SACRED_ARTIFACT",
                ["Quantity"] = "1"
            }),
            // Player then sold it
            CreateTransaction(SagaTransactionType.ItemTraded, new Dictionary<string, string>
            {
                ["ItemRef"] = "SACRED_ARTIFACT",
                ["Quantity"] = "1",
                ["Direction"] = "Sell"
            })
        };

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions);

        // Assert
        Assert.True(failed);
        Assert.Contains("SACRED_ARTIFACT", reason);
        Assert.Contains("lost", reason);
    }

    [Fact]
    public void CheckFailConditions_ItemLost_ReturnsNotFailed_WhenItemStillOwned()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "DELIVERY_QUEST",
            DisplayName = "Delivery Quest",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.ItemLost,
                    ItemRef = "SACRED_ARTIFACT"
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            // Player obtained the item and kept it
            CreateTransaction(SagaTransactionType.LootAwarded, new Dictionary<string, string>
            {
                ["ItemRef"] = "SACRED_ARTIFACT",
                ["Quantity"] = "1"
            })
        };

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions);

        // Assert
        Assert.False(failed);
        Assert.Null(reason);
    }

    [Fact]
    public void CheckFailConditions_ItemLost_ReturnsNotFailed_WhenItemNeverOwned()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "DELIVERY_QUEST",
            DisplayName = "Delivery Quest",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.ItemLost,
                    ItemRef = "SACRED_ARTIFACT"
                }
            }
        };

        var transactions = new List<SagaTransaction>();

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions);

        // Assert
        Assert.False(failed); // Can't lose what you never had
        Assert.Null(reason);
    }

    [Fact]
    public void CheckFailConditions_LocationLeft_ReturnsFailed_WhenLeftRequiredArea()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "GUARD_DUTY",
            DisplayName = "Guard Duty",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.LocationLeft,
                    LocationRef = "CASTLE_ENTRANCE"
                }
            }
        };

        var transactions = new List<SagaTransaction>();

        // Player is now at a different location
        var currentLocationRef = "MARKET_DISTRICT";

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions, currentLocationRef: currentLocationRef);

        // Assert
        Assert.True(failed);
        Assert.Contains("CASTLE_ENTRANCE", reason);
        Assert.Contains("Left", reason);
    }

    [Fact]
    public void CheckFailConditions_LocationLeft_ReturnsNotFailed_WhenStillInRequiredArea()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "GUARD_DUTY",
            DisplayName = "Guard Duty",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.LocationLeft,
                    LocationRef = "CASTLE_ENTRANCE"
                }
            }
        };

        var transactions = new List<SagaTransaction>();

        // Player is still at the required location
        var currentLocationRef = "CASTLE_ENTRANCE";

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions, currentLocationRef: currentLocationRef);

        // Assert
        Assert.False(failed);
        Assert.Null(reason);
    }

    [Fact]
    public void CheckFailConditions_LocationLeft_FallsBackToTransactionCheck()
    {
        // Arrange
        var quest = new Quest
        {
            RefName = "GUARD_DUTY",
            DisplayName = "Guard Duty",
            FailConditions = new[]
            {
                new QuestFailCondition
                {
                    Type = QuestFailConditionType.LocationLeft,
                    LocationRef = "CASTLE_ENTRANCE"
                }
            }
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.LocationClaimed, new Dictionary<string, string>
            {
                ["LocationRef"] = "MARKET_DISTRICT" // Left the required area
            })
        };

        // No explicit current location provided - should check transactions

        // Act
        var (failed, reason) = QuestProgressEvaluator.CheckFailConditions(quest, transactions);

        // Assert
        Assert.True(failed);
        Assert.Contains("CASTLE_ENTRANCE", reason);
    }

    #endregion

    #region ItemCrafted Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_ItemCrafted_CountsCorrectly()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "CRAFT_SWORDS",
            Type = QuestObjectiveType.ItemCrafted,
            ItemRef = "IRON_SWORD",
            Threshold = 3
        };

        var stage = new QuestStage
        {
            RefName = "CRAFTING",
            DisplayName = "Craft Weapons"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.ItemCrafted, new Dictionary<string, string>
            {
                ["ItemRef"] = "IRON_SWORD",
                ["Quantity"] = "1"
            }),
            CreateTransaction(SagaTransactionType.ItemCrafted, new Dictionary<string, string>
            {
                ["ItemRef"] = "IRON_SWORD",
                ["Quantity"] = "2"
            }),
            // Different item - should not count
            CreateTransaction(SagaTransactionType.ItemCrafted, new Dictionary<string, string>
            {
                ["ItemRef"] = "STEEL_SWORD",
                ["Quantity"] = "1"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(3, progress);
    }

    [Fact]
    public void EvaluateObjectiveProgress_ItemCrafted_WithoutItemRef_CountsAllCraftedItems()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "CRAFT_ANYTHING",
            Type = QuestObjectiveType.ItemCrafted,
            // No ItemRef - count all crafted items
            Threshold = 5
        };

        var stage = new QuestStage
        {
            RefName = "CRAFTING",
            DisplayName = "Craft Items"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.ItemCrafted, new Dictionary<string, string>
            {
                ["ItemRef"] = "IRON_SWORD",
                ["Quantity"] = "2"
            }),
            CreateTransaction(SagaTransactionType.ItemCrafted, new Dictionary<string, string>
            {
                ["ItemRef"] = "STEEL_ARMOR",
                ["Quantity"] = "1"
            }),
            CreateTransaction(SagaTransactionType.ItemCrafted, new Dictionary<string, string>
            {
                ["ItemRef"] = "HEALTH_POTION",
                ["Quantity"] = "3"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(6, progress);
    }

    #endregion

    #region CurrencyCollected Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_CurrencyCollected_SumsPositiveAmounts()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "COLLECT_GOLD",
            Type = QuestObjectiveType.CurrencyCollected,
            Threshold = 1000
        };

        var stage = new QuestStage
        {
            RefName = "EARNING",
            DisplayName = "Earn Gold"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.CurrencyChanged, new Dictionary<string, string>
            {
                ["Amount"] = "500"
            }),
            CreateTransaction(SagaTransactionType.CurrencyChanged, new Dictionary<string, string>
            {
                ["Amount"] = "300"
            }),
            // Negative amount (spending) should NOT count
            CreateTransaction(SagaTransactionType.CurrencyChanged, new Dictionary<string, string>
            {
                ["Amount"] = "-100"
            }),
            CreateTransaction(SagaTransactionType.CurrencyChanged, new Dictionary<string, string>
            {
                ["Amount"] = "250"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(1050, progress); // 500 + 300 + 250 = 1050 (ignoring -100)
    }

    #endregion

    #region Custom Objective Tests

    [Fact]
    public void EvaluateObjectiveProgress_CustomObjective_ReturnsThreshold_WhenMarkedComplete()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "BONUS_STEALTH",
            Type = QuestObjectiveType.Custom,
            Threshold = 1
        };

        var stage = new QuestStage
        {
            RefName = "INFILTRATION",
            DisplayName = "Infiltrate the Castle"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.CustomObjectiveCompleted, new Dictionary<string, string>
            {
                ["ObjectiveRef"] = "BONUS_STEALTH"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(1, progress); // Returns threshold when complete
    }

    [Fact]
    public void EvaluateObjectiveProgress_CustomObjective_ReturnsZero_WhenNotComplete()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "BONUS_STEALTH",
            Type = QuestObjectiveType.Custom,
            Threshold = 1
        };

        var stage = new QuestStage
        {
            RefName = "INFILTRATION",
            DisplayName = "Infiltrate the Castle"
        };

        var transactions = new List<SagaTransaction>();

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(0, progress);
    }

    [Fact]
    public void EvaluateObjectiveProgress_CustomObjective_IgnoresDifferentObjectiveRef()
    {
        // Arrange
        var world = CreateTestWorld();
        var quest = CreateTestQuest();

        var objective = new QuestObjective
        {
            RefName = "BONUS_STEALTH",
            Type = QuestObjectiveType.Custom,
            Threshold = 1
        };

        var stage = new QuestStage
        {
            RefName = "INFILTRATION",
            DisplayName = "Infiltrate the Castle"
        };

        var transactions = new List<SagaTransaction>
        {
            CreateTransaction(SagaTransactionType.CustomObjectiveCompleted, new Dictionary<string, string>
            {
                ["ObjectiveRef"] = "DIFFERENT_OBJECTIVE"
            })
        };

        // Act
        var progress = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, world);

        // Assert
        Assert.Equal(0, progress);
    }

    #endregion
}
