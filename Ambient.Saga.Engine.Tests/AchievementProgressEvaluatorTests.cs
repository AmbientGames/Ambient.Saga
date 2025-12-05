using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Achievements;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Unit tests for AchievementProgressEvaluator which computes achievement progress from transaction logs.
/// </summary>
public class AchievementProgressEvaluatorTests
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

    private SagaInstance CreateSagaWithTransactions(params SagaTransaction[] transactions)
    {
        return new SagaInstance
        {
            SagaRef = TestSagaRef,
            Transactions = transactions.ToList()
        };
    }

    private SagaTransaction CreateTransaction(SagaTransactionType type, string avatarId, Dictionary<string, object>? data = null)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = avatarId,
            Status = TransactionStatus.Committed,
            LocalTimestamp = DateTime.UtcNow
        };

        if (data != null)
        {
            foreach (var kvp in data)
            {
                transaction.SetData(kvp.Key, kvp.Value);
            }
        }

        return transaction;
    }

    private Achievement CreateAchievement(
        AchievementCriteriaType type,
        float threshold,
        string? characterRef = null,
        string? questTokenRef = null,
        string? questRef = null,
        string? factionRef = null,
        string? reputationLevel = null,
        string? statusEffectType = null)
    {
        return new Achievement
        {
            RefName = "TestAchievement",
            DisplayName = "Test Achievement",
            Criteria = new AchievementCriteria
            {
                Type = type,
                Threshold = threshold,
                CharacterRef = characterRef,
                QuestTokenRef = questTokenRef,
                QuestRef = questRef,
                FactionRef = factionRef,
                ReputationLevel = reputationLevel,
                StatusEffectType = statusEffectType
            }
        };
    }

    #region EvaluateProgress Tests

    [Fact]
    public void EvaluateProgress_WithNoCriteria_ReturnsZero()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = new Achievement { RefName = "Test", Criteria = null };
        var sagaInstances = new List<SagaInstance>();

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.0f, progress);
    }

    [Fact]
    public void EvaluateProgress_WithNoTransactions_ReturnsZero()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var sagaInstances = new List<SagaInstance> { CreateSagaWithTransactions() };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.0f, progress);
    }

    [Fact]
    public void EvaluateProgress_WithPartialProgress_ReturnsCorrectPercentage()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.3f, progress, precision: 2);
    }

    [Fact]
    public void EvaluateProgress_WithCompleteProgress_ReturnsOne()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 3);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(1.0f, progress);
    }

    [Fact]
    public void EvaluateProgress_WithExcessProgress_ClampsToOne()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 3);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(1.0f, progress); // Should clamp at 1.0
    }

    [Fact]
    public void EvaluateProgress_FiltersTransactionsByAvatar()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, "other-avatar"),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.2f, progress, precision: 2); // Only 2 out of 10
    }

    [Fact]
    public void EvaluateProgress_OnlyCountsCommittedTransactions()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var pendingTransaction = CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId);
        pendingTransaction.Status = TransactionStatus.Pending;

        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            pendingTransaction,
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.2f, progress, precision: 2); // Only 2 committed out of 10
    }

    #endregion

    #region CharactersDefeatedByRef Tests

    [Fact]
    public void EvaluateProgress_CharactersDefeatedByRef_CountsSpecificCharacter()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeatedByRef, 5, characterRef: "Boss_Dragon");
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId, new() { ["CharacterRef"] = "Boss_Dragon" }),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId, new() { ["CharacterRef"] = "Boss_Demon" }),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId, new() { ["CharacterRef"] = "Boss_Dragon" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.4f, progress, precision: 2); // 2 dragons out of 5
    }

    #endregion

    #region Discovery Metrics Tests

    [Fact]
    public void EvaluateProgress_SagasDiscovered_CountsUniqueSagas()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.SagaArcsDiscovered, 5);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.SagaDiscovered, TestAvatarId, new() { ["SagaArcRef"] = "Saga_1" }),
            CreateTransaction(SagaTransactionType.SagaDiscovered, TestAvatarId, new() { ["SagaArcRef"] = "Saga_2" }),
            CreateTransaction(SagaTransactionType.SagaDiscovered, TestAvatarId, new() { ["SagaArcRef"] = "Saga_1" }), // Duplicate
            CreateTransaction(SagaTransactionType.SagaDiscovered, TestAvatarId, new() { ["SagaArcRef"] = "Saga_3" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.6f, progress, precision: 2); // 3 unique Sagas out of 5
    }

    [Fact]
    public void EvaluateProgress_TriggersActivated_CountsAllActivations()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.SagaTriggersActivated, 10);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.TriggerActivated, TestAvatarId),
            CreateTransaction(SagaTransactionType.TriggerActivated, TestAvatarId),
            CreateTransaction(SagaTransactionType.TriggerActivated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.3f, progress, precision: 2); // 3 out of 10
    }

    #endregion

    #region Quest Token Tests

    [Fact]
    public void EvaluateProgress_QuestTokensEarned_CountsAllTokensWhenNoFilterSpecified()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.QuestTokensEarned, 10);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "Token1" }),
            CreateTransaction(SagaTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "Token2" }),
            CreateTransaction(SagaTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "Token3" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.3f, progress, precision: 2); // 3 out of 10
    }

    [Fact]
    public void EvaluateProgress_QuestTokensEarned_CountsSpecificTokenWhenFilterSpecified()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.QuestTokensEarned, 5, questTokenRef: "DragonSlayerToken");
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "DragonSlayerToken" }),
            CreateTransaction(SagaTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "OtherToken" }),
            CreateTransaction(SagaTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "DragonSlayerToken" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.4f, progress, precision: 2); // 2 DragonSlayerTokens out of 5
    }

    #endregion

    #region GetNewlyUnlockedAchievements Tests

    [Fact]
    public void GetNewlyUnlockedAchievements_WithNoNewUnlocks_ReturnsEmpty()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var allAchievements = new[] { achievement };

        var previousInstances = new[]
        {
            new AchievementInstance { TemplateRef = "TestAchievement", IsUnlocked = false }
        };

        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var newlyUnlocked = AchievementProgressEvaluator.GetNewlyUnlockedAchievements(
            allAchievements, previousInstances, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Empty(newlyUnlocked);
    }

    [Fact]
    public void GetNewlyUnlockedAchievements_WithNewUnlock_ReturnsAchievement()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 3);
        var allAchievements = new[] { achievement };

        var previousInstances = new[]
        {
            new AchievementInstance { TemplateRef = "TestAchievement", IsUnlocked = false }
        };

        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var newlyUnlocked = AchievementProgressEvaluator.GetNewlyUnlockedAchievements(
            allAchievements, previousInstances, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Single(newlyUnlocked);
        Assert.Equal("TestAchievement", newlyUnlocked[0].RefName);
    }

    [Fact]
    public void GetNewlyUnlockedAchievements_WithAlreadyUnlocked_DoesNotReturnAgain()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 3);
        var allAchievements = new[] { achievement };

        var previousInstances = new[]
        {
            new AchievementInstance { TemplateRef = "TestAchievement", IsUnlocked = true }
        };

        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var newlyUnlocked = AchievementProgressEvaluator.GetNewlyUnlockedAchievements(
            allAchievements, previousInstances, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Empty(newlyUnlocked); // Already unlocked, shouldn't return again
    }

    #endregion

    #region Quest Completion Tests

    [Fact]
    public void EvaluateProgress_QuestsCompleted_CountsUniqueQuests()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.QuestsCompleted, 5);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "Quest1" }),
            CreateTransaction(SagaTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "Quest2" }),
            CreateTransaction(SagaTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "Quest1" }), // Duplicate
            CreateTransaction(SagaTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "Quest3" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.6f, progress, precision: 2); // 3 unique quests out of 5
    }

    [Fact]
    public void EvaluateProgress_QuestsCompletedByRef_ChecksSpecificQuest()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.QuestsCompletedByRef, 1, questRef: "MainQuest");
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "SideQuest" }),
            CreateTransaction(SagaTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "MainQuest" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(1.0f, progress); // MainQuest completed
    }

    [Fact]
    public void EvaluateProgress_QuestsCompletedByRef_ReturnsZeroWhenNotCompleted()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.QuestsCompletedByRef, 1, questRef: "MainQuest");
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "SideQuest" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.0f, progress); // MainQuest not completed
    }

    #endregion

    #region Reputation Tests

    [Fact]
    public void EvaluateProgress_ReputationReached_ReturnsTrueWhenThresholdMet()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.ReputationReached, 1, factionRef: "Elves", reputationLevel: "Friendly");
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Elves", ["Amount"] = 1000 }),
            CreateTransaction(SagaTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Elves", ["Amount"] = 2500 })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(1.0f, progress); // Friendly requires 3000+, we have 3500
    }

    [Fact]
    public void EvaluateProgress_ReputationReached_ReturnsZeroWhenNotMet()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.ReputationReached, 1, factionRef: "Elves", reputationLevel: "Exalted");
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Elves", ["Amount"] = 5000 })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.0f, progress); // Exalted requires 21000+, we only have 5000
    }

    [Fact]
    public void EvaluateProgress_FactionsAtReputationLevel_CountsFactions()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.FactionsAtReputationLevel, 3, reputationLevel: "Friendly");
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Elves", ["Amount"] = 4000 }),
            CreateTransaction(SagaTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Dwarves", ["Amount"] = 3500 }),
            CreateTransaction(SagaTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Humans", ["Amount"] = 2000 }) // Not enough
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.67f, progress, precision: 2); // 2 factions out of 3 at Friendly level
    }

    #endregion

    #region Battle Achievement Tests

    [Fact]
    public void EvaluateProgress_StatusEffectsApplied_CountsAllStatusEffects()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.StatusEffectsApplied, 10);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.StatusEffectApplied, TestAvatarId, new() { ["StatusEffectRef"] = "Poison" }),
            CreateTransaction(SagaTransactionType.StatusEffectApplied, TestAvatarId, new() { ["StatusEffectRef"] = "Burn" }),
            CreateTransaction(SagaTransactionType.StatusEffectApplied, TestAvatarId, new() { ["StatusEffectRef"] = "Stun" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.3f, progress, precision: 2); // 3 out of 10
    }

    [Fact]
    public void EvaluateProgress_StatusEffectsApplied_FiltersSpecificType()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.StatusEffectsApplied, 5, statusEffectType: "Poison");
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.StatusEffectApplied, TestAvatarId, new() { ["StatusEffectRef"] = "Poison" }),
            CreateTransaction(SagaTransactionType.StatusEffectApplied, TestAvatarId, new() { ["StatusEffectRef"] = "DeadlyPoison" }),
            CreateTransaction(SagaTransactionType.StatusEffectApplied, TestAvatarId, new() { ["StatusEffectRef"] = "Burn" }),
            CreateTransaction(SagaTransactionType.StatusEffectApplied, TestAvatarId, new() { ["StatusEffectRef"] = "Poison" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.6f, progress, precision: 2); // 3 poison-related out of 5 (Poison, DeadlyPoison, Poison)
    }

    [Fact]
    public void EvaluateProgress_CriticalHitsDealt_CountsAllCriticals()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CriticalHitsDealt, 20);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CriticalHitDealt, TestAvatarId),
            CreateTransaction(SagaTransactionType.CriticalHitDealt, TestAvatarId),
            CreateTransaction(SagaTransactionType.CriticalHitDealt, TestAvatarId),
            CreateTransaction(SagaTransactionType.CriticalHitDealt, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.2f, progress, precision: 2); // 4 out of 20
    }

    [Fact]
    public void EvaluateProgress_CombosExecuted_CountsAllCombos()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CombosExecuted, 10);
        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.ComboExecuted, TestAvatarId),
            CreateTransaction(SagaTransactionType.ComboExecuted, TestAvatarId),
            CreateTransaction(SagaTransactionType.ComboExecuted, TestAvatarId)
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.3f, progress, precision: 2); // 3 out of 10
    }

    #endregion

    #region EvaluateAllAchievements Tests

    [Fact]
    public void EvaluateAllAchievements_CreatesInstancesForAll()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement1 = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        achievement1.RefName = "Achievement1";
        var achievement2 = CreateAchievement(AchievementCriteriaType.SagaArcsDiscovered, 5);
        achievement2.RefName = "Achievement2";

        var allAchievements = new[] { achievement1, achievement2 };

        var saga = CreateSagaWithTransactions(
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(SagaTransactionType.SagaDiscovered, TestAvatarId, new() { ["SagaArcRef"] = "Saga_1" })
        );
        var sagaInstances = new List<SagaInstance> { saga };

        // Act
        var instances = AchievementProgressEvaluator.EvaluateAllAchievements(
            allAchievements, sagaInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(2, instances.Count);

        var instance1 = instances.First(i => i.TemplateRef == "Achievement1");
        Assert.Equal(20, instance1.CurrentProgress); // 2/10 = 20%
        Assert.False(instance1.IsUnlocked);

        var instance2 = instances.First(i => i.TemplateRef == "Achievement2");
        Assert.Equal(20, instance2.CurrentProgress); // 1/5 = 20%
        Assert.False(instance2.IsUnlocked);
    }

    #endregion
}
