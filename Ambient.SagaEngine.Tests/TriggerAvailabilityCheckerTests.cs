using Ambient.Domain;
using Ambient.SagaEngine.Domain.Rpg.Sagas;

namespace Ambient.SagaEngine.Tests;

/// <summary>
/// Unit tests for TriggerAvailabilityChecker which validates quest token requirements for triggers.
/// </summary>
public class TriggerAvailabilityCheckerTests
{
    private AvatarBase CreateAvatarWithQuestTokens(params string[] questTokenRefs)
    {
        var avatar = new AvatarBase
        {
            ArchetypeRef = "TestAvatar",
            Capabilities = new ItemCollection
            {
                QuestTokens = questTokenRefs.Select(tokenRef => new QuestTokenEntry
                {
                    QuestTokenRef = tokenRef
                }).ToArray()
            }
        };
        return avatar;
    }

    private SagaTrigger CreateSagaTriggerWithRequirements(params string[] requiredTokenRefs)
    {
        return new SagaTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 25.0f,
            RequiresQuestTokenRef = requiredTokenRefs.Length > 0 ? requiredTokenRefs : null
        };
    }

    #region CanActivate Tests

    [Fact]
    public void CanActivate_WithNullTrigger_ThrowsArgumentNullException()
    {
        // Arrange
        var avatar = CreateAvatarWithQuestTokens();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            TriggerAvailabilityChecker.CanActivate(null!, avatar));
    }

    [Fact]
    public void CanActivate_WithNullAvatar_ThrowsArgumentNullException()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            TriggerAvailabilityChecker.CanActivate(trigger, null!));
    }

    [Fact]
    public void CanActivate_WithNoRequirements_ReturnsTrue()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements(); // No requirements
        var avatar = CreateAvatarWithQuestTokens(); // No tokens

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(trigger, avatar);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanActivate_WithRequirementAndAvatarHasToken_ReturnsTrue()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("DragonSlayerToken");
        var avatar = CreateAvatarWithQuestTokens("DragonSlayerToken");

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(trigger, avatar);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanActivate_WithRequirementAndAvatarLacksToken_ReturnsFalse()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("DragonSlayerToken");
        var avatar = CreateAvatarWithQuestTokens("DifferentToken");

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(trigger, avatar);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanActivate_WithMultipleRequirementsAndAvatarHasAll_ReturnsTrue()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("Token1", "Token2", "Token3");
        var avatar = CreateAvatarWithQuestTokens("Token1", "Token2", "Token3", "Token4");

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(trigger, avatar);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanActivate_WithMultipleRequirementsAndAvatarMissingOne_ReturnsFalse()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("Token1", "Token2", "Token3");
        var avatar = CreateAvatarWithQuestTokens("Token1", "Token3"); // Missing Token2

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(trigger, avatar);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanActivate_WithRequirementAndAvatarHasNoCapabilities_ReturnsFalse()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("SomeToken");
        var avatar = new AvatarBase
        {
            ArchetypeRef = "TestAvatar",
            Capabilities = null // No capabilities
        };

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(trigger, avatar);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanActivate_WithRequirementAndAvatarHasNoQuestTokens_ReturnsFalse()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("SomeToken");
        var avatar = new AvatarBase
        {
            ArchetypeRef = "TestAvatar",
            Capabilities = new ItemCollection
            {
                QuestTokens = null // No quest tokens
            }
        };

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(trigger, avatar);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetMissingQuestTokens Tests

    [Fact]
    public void GetMissingQuestTokens_WithNullTrigger_ThrowsArgumentNullException()
    {
        // Arrange
        var avatar = CreateAvatarWithQuestTokens();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            TriggerAvailabilityChecker.GetMissingQuestTokens(null!, avatar));
    }

    [Fact]
    public void GetMissingQuestTokens_WithNullAvatar_ThrowsArgumentNullException()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            TriggerAvailabilityChecker.GetMissingQuestTokens(trigger, null!));
    }

    [Fact]
    public void GetMissingQuestTokens_WithNoRequirements_ReturnsEmptyArray()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements();
        var avatar = CreateAvatarWithQuestTokens();

        // Act
        var missing = TriggerAvailabilityChecker.GetMissingQuestTokens(trigger, avatar);

        // Assert
        Assert.Empty(missing);
    }

    [Fact]
    public void GetMissingQuestTokens_WithAllTokensPresent_ReturnsEmptyArray()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("Token1", "Token2");
        var avatar = CreateAvatarWithQuestTokens("Token1", "Token2", "Token3");

        // Act
        var missing = TriggerAvailabilityChecker.GetMissingQuestTokens(trigger, avatar);

        // Assert
        Assert.Empty(missing);
    }

    [Fact]
    public void GetMissingQuestTokens_WithOneMissing_ReturnsMissingToken()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("Token1", "Token2", "Token3");
        var avatar = CreateAvatarWithQuestTokens("Token1", "Token3");

        // Act
        var missing = TriggerAvailabilityChecker.GetMissingQuestTokens(trigger, avatar);

        // Assert
        Assert.Single(missing);
        Assert.Contains("Token2", missing);
    }

    [Fact]
    public void GetMissingQuestTokens_WithMultipleMissing_ReturnsAllMissingTokens()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("Token1", "Token2", "Token3", "Token4");
        var avatar = CreateAvatarWithQuestTokens("Token1", "Token3");

        // Act
        var missing = TriggerAvailabilityChecker.GetMissingQuestTokens(trigger, avatar);

        // Assert
        Assert.Equal(2, missing.Length);
        Assert.Contains("Token2", missing);
        Assert.Contains("Token4", missing);
    }

    [Fact]
    public void GetMissingQuestTokens_WithNoTokensInInventory_ReturnsAllRequired()
    {
        // Arrange
        var trigger = CreateSagaTriggerWithRequirements("Token1", "Token2");
        var avatar = CreateAvatarWithQuestTokens(); // No tokens

        // Act
        var missing = TriggerAvailabilityChecker.GetMissingQuestTokens(trigger, avatar);

        // Assert
        Assert.Equal(2, missing.Length);
        Assert.Contains("Token1", missing);
        Assert.Contains("Token2", missing);
    }

    #endregion

    #region Progressive Trigger Chain Tests (Real-world TriggerExpander scenarios)

    [Fact]
    public void CanActivate_ProgressiveChain_OutermostTriggerAlwaysAvailable()
    {
        // Arrange - Simulate outer trigger of progressive chain (no requirements)
        var outerTrigger = CreateSagaTriggerWithRequirements(); // No requirements
        var avatar = CreateAvatarWithQuestTokens(); // No tokens yet

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(outerTrigger, avatar);

        // Assert
        Assert.True(result, "Outermost trigger should always be available");
    }

    [Fact]
    public void CanActivate_ProgressiveChain_MiddleTriggerLockedUntilOuterCompleted()
    {
        // Arrange - Simulate middle trigger requiring outer completion
        var middleTrigger = CreateSagaTriggerWithRequirements("BossDungeon_outer_Completed");
        var avatar = CreateAvatarWithQuestTokens(); // Haven't completed outer yet

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(middleTrigger, avatar);

        // Assert
        Assert.False(result, "Middle trigger should be locked until outer completed");
    }

    [Fact]
    public void CanActivate_ProgressiveChain_MiddleTriggerAvailableAfterOuterCompleted()
    {
        // Arrange
        var middleTrigger = CreateSagaTriggerWithRequirements("BossDungeon_outer_Completed");
        var avatar = CreateAvatarWithQuestTokens("BossDungeon_outer_Completed");

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(middleTrigger, avatar);

        // Assert
        Assert.True(result, "Middle trigger should unlock after completing outer");
    }

    [Fact]
    public void CanActivate_ProgressiveChain_InnerTriggerRequiresPreviousCompletions()
    {
        // Arrange - Inner trigger requires middle completion (which implicitly required outer)
        var innerTrigger = CreateSagaTriggerWithRequirements("BossDungeon_middle_Completed");
        var avatar = CreateAvatarWithQuestTokens("BossDungeon_outer_Completed");
        // Has outer but not middle

        // Act
        var result = TriggerAvailabilityChecker.CanActivate(innerTrigger, avatar);

        // Assert
        Assert.False(result, "Inner trigger should require middle completion");
    }

    [Fact]
    public void CanActivate_ProgressiveChain_AllTriggersCompletedInOrder()
    {
        // Arrange - Test full progression path
        var outerTrigger = CreateSagaTriggerWithRequirements();
        var middleTrigger = CreateSagaTriggerWithRequirements("Saga_outer_Completed");
        var innerTrigger = CreateSagaTriggerWithRequirements("Saga_middle_Completed");

        var avatar = CreateAvatarWithQuestTokens(); // Start with no tokens

        // Act & Assert - Outer trigger always available
        Assert.True(TriggerAvailabilityChecker.CanActivate(outerTrigger, avatar));
        Assert.False(TriggerAvailabilityChecker.CanActivate(middleTrigger, avatar));
        Assert.False(TriggerAvailabilityChecker.CanActivate(innerTrigger, avatar));

        // Player completes outer trigger
        avatar = CreateAvatarWithQuestTokens("Saga_outer_Completed");
        Assert.True(TriggerAvailabilityChecker.CanActivate(outerTrigger, avatar));
        Assert.True(TriggerAvailabilityChecker.CanActivate(middleTrigger, avatar));
        Assert.False(TriggerAvailabilityChecker.CanActivate(innerTrigger, avatar));

        // Player completes middle trigger
        avatar = CreateAvatarWithQuestTokens("Saga_outer_Completed", "Saga_middle_Completed");
        Assert.True(TriggerAvailabilityChecker.CanActivate(outerTrigger, avatar));
        Assert.True(TriggerAvailabilityChecker.CanActivate(middleTrigger, avatar));
        Assert.True(TriggerAvailabilityChecker.CanActivate(innerTrigger, avatar));
    }

    #endregion
}
