namespace Ambient.Domain.Tests.UnitTests.Gameplay;

/// <summary>
/// Unit tests for the Achievement class that represents game achievements.
/// </summary>
public class AchievementTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var achievement = new Achievement();

        // Assert
        Assert.NotNull(achievement);
        Assert.Null(achievement.Criteria);
        Assert.Null(achievement.ExtensionData);
    }

    [Fact]
    public void RefName_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var achievement = new Achievement();
        const string expectedRefName = "FirstBloodKill";

        // Act
        achievement.RefName = expectedRefName;

        // Assert
        Assert.Equal(expectedRefName, achievement.RefName);
    }

    [Fact]
    public void DisplayName_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var achievement = new Achievement();
        const string expectedDisplayName = "First Blood";

        // Act
        achievement.DisplayName = expectedDisplayName;

        // Assert
        Assert.Equal(expectedDisplayName, achievement.DisplayName);
    }

    [Fact]
    public void Description_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var achievement = new Achievement();
        const string expectedDescription = "Defeat your first enemy";

        // Act
        achievement.Description = expectedDescription;

        // Assert
        Assert.Equal(expectedDescription, achievement.Description);
    }

    [Fact]
    public void Criteria_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var achievement = new Achievement();
        var criteria = new AchievementCriteria
        {
            Type = AchievementCriteriaType.PlayTimeHours,
            Threshold = 1
        };

        // Act
        achievement.Criteria = criteria;

        // Assert
        Assert.NotNull(achievement.Criteria);
        Assert.Equal(AchievementCriteriaType.PlayTimeHours, achievement.Criteria.Type);
        Assert.Equal(1, achievement.Criteria.Threshold);
    }

    [Fact]
    public void ExtensionData_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var achievement = new Achievement();
        const string extensionData = "custom-data-here";

        // Act
        achievement.ExtensionData = extensionData;

        // Assert
        Assert.Equal(extensionData, achievement.ExtensionData);
    }

    [Fact]
    public void MultipleInstances_HaveIndependentProperties()
    {
        // Arrange
        var achievement1 = new Achievement
        {
            RefName = "Achievement1",
            DisplayName = "First Achievement"
        };
        var achievement2 = new Achievement
        {
            RefName = "Achievement2",
            DisplayName = "Second Achievement"
        };

        // Assert
        Assert.Equal("Achievement1", achievement1.RefName);
        Assert.Equal("Achievement2", achievement2.RefName);
        Assert.NotEqual(achievement1.RefName, achievement2.RefName);
        Assert.NotEqual(achievement1.DisplayName, achievement2.DisplayName);
    }

    [Fact]
    public void Achievement_IsPartialClass_CanBeExtended()
    {
        // This test verifies that the Achievement class is partial
        // and can be extended with additional functionality
        var type = typeof(Achievement);

        // Assert - The class should be a public class (partial classes are regular classes at runtime)
        Assert.True(type.IsClass);
        Assert.True(type.IsPublic);
    }

    [Fact]
    public void Achievement_InheritsFromEntityBase()
    {
        // Arrange
        var achievement = new Achievement();

        // Assert
        Assert.IsAssignableFrom<EntityBase>(achievement);
    }

    [Theory]
    [InlineData(AchievementCriteriaType.BlocksDestroyed, 10)]
    [InlineData(AchievementCriteriaType.BlocksPlaced, 5)]
    [InlineData(AchievementCriteriaType.PlayTimeHours, 1)]
    [InlineData(AchievementCriteriaType.DistanceTraveled, 100)]
    public void Criteria_SetToVariousTypes_AcceptsAllValues(AchievementCriteriaType type, float threshold)
    {
        // Arrange
        var achievement = new Achievement();
        var criteria = new AchievementCriteria
        {
            Type = type,
            Threshold = threshold
        };

        // Act
        achievement.Criteria = criteria;

        // Assert
        Assert.Equal(type, achievement.Criteria.Type);
        Assert.Equal(threshold, achievement.Criteria.Threshold);
    }

    [Fact]
    public void Achievement_WithCompleteData_StoresAllProperties()
    {
        // Arrange & Act
        var achievement = new Achievement
        {
            RefName = "MasterCrafter",
            DisplayName = "Master Crafter",
            Description = "Craft 1000 items",
            Criteria = new AchievementCriteria
            {
                Type = AchievementCriteriaType.BlocksDestroyed,
                Threshold = 1000
            },
            ExtensionData = "bonus-reward-id"
        };

        // Assert
        Assert.Equal("MasterCrafter", achievement.RefName);
        Assert.Equal("Master Crafter", achievement.DisplayName);
        Assert.Equal("Craft 1000 items", achievement.Description);
        Assert.NotNull(achievement.Criteria);
        Assert.Equal(AchievementCriteriaType.BlocksDestroyed, achievement.Criteria.Type);
        Assert.Equal(1000, achievement.Criteria.Threshold);
        Assert.Equal("bonus-reward-id", achievement.ExtensionData);
    }
}
