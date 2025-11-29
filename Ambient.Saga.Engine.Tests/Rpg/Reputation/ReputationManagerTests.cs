using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Reputation;
using Xunit;

namespace Ambient.Saga.Engine.Tests.Rpg.Reputation;

/// <summary>
/// Tests for ReputationManager - faction reputation calculation, level conversion, and spillover.
/// Tests the full WoW-style reputation system implementation.
/// </summary>
public class ReputationManagerTests
{
    [Theory]
    [InlineData(-42000, ReputationLevel.Hated)]
    [InlineData(-21000, ReputationLevel.Hostile)]
    [InlineData(-20999, ReputationLevel.Hostile)]
    [InlineData(-6000, ReputationLevel.Unfriendly)]
    [InlineData(-5999, ReputationLevel.Unfriendly)]
    [InlineData(-3000, ReputationLevel.Neutral)]
    [InlineData(0, ReputationLevel.Neutral)]
    [InlineData(2999, ReputationLevel.Neutral)]
    [InlineData(3000, ReputationLevel.Friendly)]
    [InlineData(8999, ReputationLevel.Friendly)]
    [InlineData(9000, ReputationLevel.Honored)]
    [InlineData(20999, ReputationLevel.Honored)]
    [InlineData(21000, ReputationLevel.Revered)]
    [InlineData(41999, ReputationLevel.Revered)]
    [InlineData(42000, ReputationLevel.Exalted)]
    [InlineData(100000, ReputationLevel.Exalted)]
    public void GetReputationLevel_VariousValues_ReturnsCorrectLevel(int reputationValue, ReputationLevel expectedLevel)
    {
        // Act
        var actualLevel = ReputationManager.GetReputationLevel(reputationValue);

        // Assert
        Assert.Equal(expectedLevel, actualLevel);
    }

    [Theory]
    [InlineData(ReputationLevel.Hated, -42000, -21000)]
    [InlineData(ReputationLevel.Hostile, -21000, -6000)]
    [InlineData(ReputationLevel.Unfriendly, -6000, -3000)]
    [InlineData(ReputationLevel.Neutral, -3000, 3000)]
    [InlineData(ReputationLevel.Friendly, 3000, 9000)]
    [InlineData(ReputationLevel.Honored, 9000, 21000)]
    [InlineData(ReputationLevel.Revered, 21000, 42000)]
    [InlineData(ReputationLevel.Exalted, 42000, int.MaxValue)]
    public void GetReputationRange_AllLevels_ReturnsCorrectRange(ReputationLevel level, int expectedMin, int expectedMax)
    {
        // Act
        var (min, max) = ReputationManager.GetReputationRange(level);

        // Assert
        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedMax, max);
    }

    [Theory]
    [InlineData(-3000, 6000, ReputationLevel.Friendly)]  // Neutral → Friendly (crosses threshold at 3000)
    [InlineData(0, 9000, ReputationLevel.Honored)]        // Neutral → Honored
    [InlineData(8999, 1, ReputationLevel.Honored)]        // Friendly (edge) → Honored
    [InlineData(-6000, 3001, ReputationLevel.Neutral)]    // Unfriendly → Neutral
    public void AddReputation_CrossesThresholds_ReturnsNewLevel(int currentRep, int gainAmount, ReputationLevel expectedLevel)
    {
        // Act
        var newRep = currentRep + gainAmount;
        var actualLevel = ReputationManager.GetReputationLevel(newRep);

        // Assert
        Assert.Equal(expectedLevel, actualLevel);
    }

    [Fact]
    public void CalculateSpillover_AlliedFaction_Returns25PercentByDefault()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "FACTION_A",
            DisplayName = "Faction A",
            Relationships = new[]
            {
                new FactionRelationship
                {
                    FactionRef = "FACTION_B",
                    RelationshipType = FactionRelationshipRelationshipType.Allied
                    // SpilloverPercent defaults to 0.25 (25%)
                }
            }
        };

        // Act
        var spilloverAmount = ReputationManager.CalculateSpillover(faction, "FACTION_B", 100);

        // Assert
        Assert.Equal(25, spilloverAmount);
    }

    [Fact]
    public void CalculateSpillover_AlliedFactionWithCustomPercent_ReturnsCustomAmount()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "FACTION_A",
            DisplayName = "Faction A",
            Relationships = new[]
            {
                new FactionRelationship
                {
                    FactionRef = "FACTION_B",
                    RelationshipType = FactionRelationshipRelationshipType.Allied,
                    SpilloverPercent = 0.5f  // 50% spillover
                }
            }
        };

        // Act
        var spilloverAmount = ReputationManager.CalculateSpillover(faction, "FACTION_B", 100);

        // Assert
        Assert.Equal(50, spilloverAmount);
    }

    [Fact]
    public void CalculateSpillover_EnemyFaction_ReturnsNegativeAmount()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "FACTION_A",
            DisplayName = "Faction A",
            Relationships = new[]
            {
                new FactionRelationship
                {
                    FactionRef = "FACTION_B",
                    RelationshipType = FactionRelationshipRelationshipType.Enemy,
                    SpilloverPercent = 0.5f  // 50% loss to enemies
                }
            }
        };

        // Act
        var spilloverAmount = ReputationManager.CalculateSpillover(faction, "FACTION_B", 100);

        // Assert
        Assert.Equal(-50, spilloverAmount);  // Negative because it's an enemy
    }

    [Fact]
    public void CalculateSpillover_RivalFaction_ReturnsZero()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "FACTION_A",
            DisplayName = "Faction A",
            Relationships = new[]
            {
                new FactionRelationship
                {
                    FactionRef = "FACTION_B",
                    RelationshipType = FactionRelationshipRelationshipType.Rival,
                    SpilloverPercent = 0.5f
                }
            }
        };

        // Act
        var spilloverAmount = ReputationManager.CalculateSpillover(faction, "FACTION_B", 100);

        // Assert
        Assert.Equal(0, spilloverAmount);  // Rivals don't affect each other
    }

    [Fact]
    public void CalculateSpillover_NoRelationship_ReturnsZero()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "FACTION_A",
            DisplayName = "Faction A",
            Relationships = null
        };

        // Act
        var spilloverAmount = ReputationManager.CalculateSpillover(faction, "FACTION_B", 100);

        // Assert
        Assert.Equal(0, spilloverAmount);
    }

    [Fact]
    public void ApplyReputationChange_WithSpillover_ModifiesAllRelatedFactions()
    {
        // Arrange
        var factionA = new Faction
        {
            RefName = "KNIGHTS_OF_VALOR",
            DisplayName = "Knights of Valor",
            Category = FactionCategory.Military,
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

        var factionB = new Faction
        {
            RefName = "CITY_OF_HAVEN",
            DisplayName = "City of Haven",
            Category = FactionCategory.City
        };

        var factionC = new Faction
        {
            RefName = "DARK_CULTISTS",
            DisplayName = "Dark Cultists",
            Category = FactionCategory.Criminal
        };

        var factions = new Dictionary<string, Faction>
        {
            ["KNIGHTS_OF_VALOR"] = factionA,
            ["CITY_OF_HAVEN"] = factionB,
            ["DARK_CULTISTS"] = factionC
        };

        var currentReputation = new Dictionary<string, int>
        {
            ["KNIGHTS_OF_VALOR"] = 0,
            ["CITY_OF_HAVEN"] = 0,
            ["DARK_CULTISTS"] = 0
        };

        // Act
        var changes = ReputationManager.ApplyReputationChange(
            currentReputation,
            factions,
            "KNIGHTS_OF_VALOR",
            100);

        // Assert
        Assert.Equal(100, changes["KNIGHTS_OF_VALOR"]);         // Direct gain
        Assert.Equal(25, changes["CITY_OF_HAVEN"]);             // 25% Allied spillover
        Assert.Equal(-50, changes["DARK_CULTISTS"]);            // 50% Enemy loss
    }

    [Fact]
    public void ApplyReputationChange_MultipleAllies_AllReceiveSpillover()
    {
        // Arrange
        var factionA = new Faction
        {
            RefName = "FACTION_A",
            DisplayName = "Faction A",
            Relationships = new[]
            {
                new FactionRelationship
                {
                    FactionRef = "FACTION_B",
                    RelationshipType = FactionRelationshipRelationshipType.Allied,
                    SpilloverPercent = 0.25f
                },
                new FactionRelationship
                {
                    FactionRef = "FACTION_C",
                    RelationshipType = FactionRelationshipRelationshipType.Allied,
                    SpilloverPercent = 0.1f
                }
            }
        };

        var factions = new Dictionary<string, Faction>
        {
            ["FACTION_A"] = factionA,
            ["FACTION_B"] = new Faction { RefName = "FACTION_B", DisplayName = "Faction B" },
            ["FACTION_C"] = new Faction { RefName = "FACTION_C", DisplayName = "Faction C" }
        };

        var currentReputation = new Dictionary<string, int>
        {
            ["FACTION_A"] = 0,
            ["FACTION_B"] = 0,
            ["FACTION_C"] = 0
        };

        // Act
        var changes = ReputationManager.ApplyReputationChange(
            currentReputation,
            factions,
            "FACTION_A",
            1000);

        // Assert
        Assert.Equal(1000, changes["FACTION_A"]);
        Assert.Equal(250, changes["FACTION_B"]);   // 25% spillover
        Assert.Equal(100, changes["FACTION_C"]);   // 10% spillover
    }

    [Fact]
    public void GetStartingReputation_FactionWithStartingRep_ReturnsConfiguredValue()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "FRIENDLY_FACTION",
            DisplayName = "Friendly Faction",
            StartingReputation = 3000  // Start at Friendly
        };

        // Act
        var startingRep = ReputationManager.GetStartingReputation(faction);

        // Assert
        Assert.Equal(3000, startingRep);
        Assert.Equal(ReputationLevel.Friendly, ReputationManager.GetReputationLevel(startingRep));
    }

    [Fact]
    public void GetStartingReputation_FactionWithNoStartingRep_ReturnsZero()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "NEUTRAL_FACTION",
            DisplayName = "Neutral Faction"
            // StartingReputation defaults to 0
        };

        // Act
        var startingRep = ReputationManager.GetStartingReputation(faction);

        // Assert
        Assert.Equal(0, startingRep);
        Assert.Equal(ReputationLevel.Neutral, ReputationManager.GetReputationLevel(startingRep));
    }

    [Fact]
    public void GetReputationRewards_AtHonored_ReturnsHonoredRewards()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "TEST_FACTION",
            DisplayName = "Test Faction",
            ReputationRewards = new[]
            {
                new ReputationReward
                {
                    RequiredLevel = ReputationLevel.Honored,
                    Equipment = new[]
                    {
                        new ReputationRewardEquipment
                        {
                            EquipmentRef = "LEGENDARY_SWORD",
                            Quantity = 1,
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
                            Quantity = 1,
                            DiscountPercent = 50
                        }
                    }
                }
            }
        };

        // Act - at Honored level (9000 rep)
        var rewards = ReputationManager.GetAvailableRewards(faction, 9000);

        // Assert
        Assert.Single(rewards);
        Assert.Equal(ReputationLevel.Honored, rewards[0].RequiredLevel);
        Assert.Single(rewards[0].Equipment);
        Assert.Equal("LEGENDARY_SWORD", rewards[0].Equipment[0].EquipmentRef);
    }

    [Fact]
    public void GetReputationRewards_AtExalted_ReturnsAllRewards()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "TEST_FACTION",
            DisplayName = "Test Faction",
            ReputationRewards = new[]
            {
                new ReputationReward { RequiredLevel = ReputationLevel.Friendly },
                new ReputationReward { RequiredLevel = ReputationLevel.Honored },
                new ReputationReward { RequiredLevel = ReputationLevel.Revered },
                new ReputationReward { RequiredLevel = ReputationLevel.Exalted }
            }
        };

        // Act - at Exalted level (42000 rep)
        var rewards = ReputationManager.GetAvailableRewards(faction, 42000);

        // Assert
        Assert.Equal(4, rewards.Length);  // All rewards unlocked
    }

    [Fact]
    public void GetReputationRewards_BelowRequiredLevel_ReturnsEmpty()
    {
        // Arrange
        var faction = new Faction
        {
            RefName = "TEST_FACTION",
            DisplayName = "Test Faction",
            ReputationRewards = new[]
            {
                new ReputationReward { RequiredLevel = ReputationLevel.Honored }
            }
        };

        // Act - at Neutral level (0 rep)
        var rewards = ReputationManager.GetAvailableRewards(faction, 0);

        // Assert
        Assert.Empty(rewards);
    }
}
