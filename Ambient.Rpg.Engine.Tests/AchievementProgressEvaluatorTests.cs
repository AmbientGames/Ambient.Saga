using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain.Achievements;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Tests;

/// <summary>
/// Unit tests for AchievementProgressEvaluator which computes achievement progress from transaction logs.
/// </summary>
public class AchievementProgressEvaluatorTests
{
    private const string TestAvatarId = "avatar-123";
    private const string TestArcRef = "TestArc";

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

    private ArcInstance CreateArcWithTransactions(params ArcTransaction[] transactions)
    {
        return new ArcInstance
        {
            ArcRef = TestArcRef,
            Transactions = transactions.ToList()
        };
    }

    private ArcTransaction CreateTransaction(ArcTransactionType type, string avatarId, Dictionary<string, object>? data = null)
    {
        var transaction = new ArcTransaction
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
        string? reputationLevel = null)
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
                ReputationLevel = reputationLevel
            }
        };
    }

    #region Content-Update Template Tests (audit C2)

    [Fact]
    public void GetNewlyUnlockedAchievements_TemplateAddedAfterFirstPlay_IsEvaluableAndUnlocks()
    {
        // Audit C2: the retired AchievementInstance store froze the instance set at
        // first creation, so templates added by content updates were invisible to
        // existing avatars. The avatar-ledger pipeline evaluates every template in
        // the world catalog each pass — a new template must unlock for a veteran
        // avatar whose ledger predates it.
        var world = CreateTestWorld();

        var originalTemplate = new Achievement
        {
            RefName = "ACH_FIRST_BLOOD",
            DisplayName = "First Blood",
            Criteria = new AchievementCriteria { Type = AchievementCriteriaType.CharactersDefeated, Threshold = 1 }
        };
        var addedByUpdate = new Achievement
        {
            RefName = "ACH_ADDED_IN_UPDATE",
            DisplayName = "Added In Update",
            Criteria = new AchievementCriteria { Type = AchievementCriteriaType.CharactersDefeated, Threshold = 1 }
        };

        // Ledger from first play only knows the original template (already unlocked)
        var previousInstances = new List<AchievementInstance>
        {
            new()
            {
                InstanceId = "ACH_FIRST_BLOOD",
                TemplateRef = "ACH_FIRST_BLOOD",
                AvatarId = TestAvatarId,
                IsUnlocked = true
            }
        };

        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId));

        // Act
        var newlyUnlocked = AchievementProgressEvaluator.GetNewlyUnlockedAchievements(
            new[] { originalTemplate, addedByUpdate },
            previousInstances,
            new[] { arc },
            world,
            TestAvatarId);

        // Assert - the update-added template unlocks; the already-unlocked one is not re-raised
        var unlocked = Assert.Single(newlyUnlocked);
        Assert.Equal("ACH_ADDED_IN_UPDATE", unlocked.RefName);
    }

    #endregion

    #region EvaluateProgress Tests

    [Fact]
    public void EvaluateProgress_WithNoCriteria_ReturnsZero()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = new Achievement { RefName = "Test", Criteria = null };
        var arcInstances = new List<ArcInstance>();

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.0f, progress);
    }

    [Fact]
    public void EvaluateProgress_WithNoTransactions_ReturnsZero()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var arcInstances = new List<ArcInstance> { CreateArcWithTransactions() };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.0f, progress);
    }

    [Fact]
    public void EvaluateProgress_WithPartialProgress_ReturnsCorrectPercentage()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.3f, progress, precision: 2);
    }

    [Fact]
    public void EvaluateProgress_WithCompleteProgress_ReturnsOne()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 3);
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(1.0f, progress);
    }

    [Fact]
    public void EvaluateProgress_WithExcessProgress_ClampsToOne()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 3);
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(1.0f, progress); // Should clamp at 1.0
    }

    [Fact]
    public void EvaluateProgress_FiltersTransactionsByAvatar()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, "other-avatar"),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.2f, progress, precision: 2); // Only 2 out of 10
    }

    [Fact]
    public void EvaluateProgress_OnlyCountsCommittedTransactions()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        var pendingTransaction = CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId);
        pendingTransaction.Status = TransactionStatus.Pending;

        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            pendingTransaction,
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

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
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId, new() { ["CharacterRef"] = "Boss_Dragon" }),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId, new() { ["CharacterRef"] = "Boss_Demon" }),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId, new() { ["CharacterRef"] = "Boss_Dragon" })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.4f, progress, precision: 2); // 2 dragons out of 5
    }

    #endregion

    #region Discovery Metrics Tests

    [Fact]
    public void EvaluateProgress_ArcsDiscovered_CountsUniqueArcs()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.ArcsDiscovered, 5);
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.ArcDiscovered, TestAvatarId, new() { ["ArcRef"] = "Arc_1" }),
            CreateTransaction(ArcTransactionType.ArcDiscovered, TestAvatarId, new() { ["ArcRef"] = "Arc_2" }),
            CreateTransaction(ArcTransactionType.ArcDiscovered, TestAvatarId, new() { ["ArcRef"] = "Arc_1" }), // Duplicate
            CreateTransaction(ArcTransactionType.ArcDiscovered, TestAvatarId, new() { ["ArcRef"] = "Arc_3" })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.6f, progress, precision: 2); // 3 unique Arcs out of 5
    }

    [Fact]
    public void EvaluateProgress_TriggersActivated_CountsAllActivations()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.ArcTriggersActivated, 10);
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.TriggerActivated, TestAvatarId),
            CreateTransaction(ArcTransactionType.TriggerActivated, TestAvatarId),
            CreateTransaction(ArcTransactionType.TriggerActivated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

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
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "Token1" }),
            CreateTransaction(ArcTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "Token2" }),
            CreateTransaction(ArcTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "Token3" })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.3f, progress, precision: 2); // 3 out of 10
    }

    [Fact]
    public void EvaluateProgress_QuestTokensEarned_CountsSpecificTokenWhenFilterSpecified()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.QuestTokensEarned, 5, questTokenRef: "DragonSlayerToken");
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "DragonSlayerToken" }),
            CreateTransaction(ArcTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "OtherToken" }),
            CreateTransaction(ArcTransactionType.QuestTokenAwarded, TestAvatarId, new() { ["QuestTokenRef"] = "DragonSlayerToken" })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

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

        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var newlyUnlocked = AchievementProgressEvaluator.GetNewlyUnlockedAchievements(
            allAchievements, previousInstances, arcInstances, world, TestAvatarId);

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

        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var newlyUnlocked = AchievementProgressEvaluator.GetNewlyUnlockedAchievements(
            allAchievements, previousInstances, arcInstances, world, TestAvatarId);

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

        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId)
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var newlyUnlocked = AchievementProgressEvaluator.GetNewlyUnlockedAchievements(
            allAchievements, previousInstances, arcInstances, world, TestAvatarId);

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
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "Quest1" }),
            CreateTransaction(ArcTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "Quest2" }),
            CreateTransaction(ArcTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "Quest1" }), // Duplicate
            CreateTransaction(ArcTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "Quest3" })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.6f, progress, precision: 2); // 3 unique quests out of 5
    }

    [Fact]
    public void EvaluateProgress_QuestsCompletedByRef_ChecksSpecificQuest()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.QuestsCompletedByRef, 1, questRef: "MainQuest");
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "SideQuest" }),
            CreateTransaction(ArcTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "MainQuest" })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(1.0f, progress); // MainQuest completed
    }

    [Fact]
    public void EvaluateProgress_QuestsCompletedByRef_ReturnsZeroWhenNotCompleted()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.QuestsCompletedByRef, 1, questRef: "MainQuest");
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.QuestCompleted, TestAvatarId, new() { ["QuestRef"] = "SideQuest" })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

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
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Elves", ["Amount"] = 1000 }),
            CreateTransaction(ArcTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Elves", ["Amount"] = 2500 })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(1.0f, progress); // Friendly requires 3000+, we have 3500
    }

    [Fact]
    public void EvaluateProgress_ReputationReached_ReturnsZeroWhenNotMet()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.ReputationReached, 1, factionRef: "Elves", reputationLevel: "Exalted");
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Elves", ["Amount"] = 5000 })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.0f, progress); // Exalted requires 21000+, we only have 5000
    }

    [Fact]
    public void EvaluateProgress_FactionsAtReputationLevel_CountsFactions()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement = CreateAchievement(AchievementCriteriaType.FactionsAtReputationLevel, 3, reputationLevel: "Friendly");
        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Elves", ["Amount"] = 4000 }),
            CreateTransaction(ArcTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Dwarves", ["Amount"] = 3500 }),
            CreateTransaction(ArcTransactionType.ReputationChanged, TestAvatarId, new() { ["FactionRef"] = "Humans", ["Amount"] = 2000 }) // Not enough
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var progress = AchievementProgressEvaluator.EvaluateProgress(achievement, arcInstances, world, TestAvatarId);

        // Assert
        Assert.Equal(0.67f, progress, precision: 2); // 2 factions out of 3 at Friendly level
    }

    #endregion

    // (Battle Achievement Tests region removed 2026-08-21 — the four tests fabricated
    // StatusEffectApplied / CriticalHitDealt / ComboExecuted transactions that no production
    // code has ever written, so they proved the counters could add up, not that the feature
    // worked. Criteria, counters and transaction types all deleted.)

    #region EvaluateAllAchievements Tests

    [Fact]
    public void EvaluateAllAchievements_CreatesInstancesForAll()
    {
        // Arrange
        var world = CreateTestWorld();
        var achievement1 = CreateAchievement(AchievementCriteriaType.CharactersDefeated, 10);
        achievement1.RefName = "Achievement1";
        var achievement2 = CreateAchievement(AchievementCriteriaType.ArcsDiscovered, 5);
        achievement2.RefName = "Achievement2";

        var allAchievements = new[] { achievement1, achievement2 };

        var arc = CreateArcWithTransactions(
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.CharacterDefeated, TestAvatarId),
            CreateTransaction(ArcTransactionType.ArcDiscovered, TestAvatarId, new() { ["ArcRef"] = "Arc_1" })
        );
        var arcInstances = new List<ArcInstance> { arc };

        // Act
        var instances = AchievementProgressEvaluator.EvaluateAllAchievements(
            allAchievements, arcInstances, world, TestAvatarId);

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
