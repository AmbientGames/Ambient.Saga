using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.Avatar;

namespace Ambient.Domain.Tests;

public class CarryWeightCalculatorTests
{
    private static IWorldConfiguration DefaultConfig() => new StubWorldConfiguration
    {
        BlockWeight = 1000,
        EquipmentWeight = 2000,
        ToolWeight = 1500,
        SpellWeight = 100,
        ConsumableWeight = 250,
        BuildingMaterialWeight = 500,
        WeightUnitName = "kg"
    };

    private static AvatarArchetype ArchetypeWithCapacity(int maxCarryWeight) => new()
    {
        MaxCarryWeight = maxCarryWeight
    };

    private static ItemCollection EmptyCapabilities() => new();

    #region GetMaxCarryWeight

    [Fact]
    public void GetMaxCarryWeight_NullArchetype_ReturnsDefault50000()
    {
        var result = CarryWeightCalculator.GetMaxCarryWeight(null);
        Assert.Equal(50000, result);
    }

    [Fact]
    public void GetMaxCarryWeight_ArchetypeWithValue_ReturnsArchetypeValue()
    {
        var archetype = ArchetypeWithCapacity(60000);
        var result = CarryWeightCalculator.GetMaxCarryWeight(archetype);
        Assert.Equal(60000, result);
    }

    [Theory]
    [InlineData(35000)]
    [InlineData(40000)]
    [InlineData(55000)]
    [InlineData(60000)]
    public void GetMaxCarryWeight_VariousValues_ReturnsCorrectly(int expected)
    {
        var archetype = ArchetypeWithCapacity(expected);
        Assert.Equal(expected, CarryWeightCalculator.GetMaxCarryWeight(archetype));
    }

    #endregion

    #region CalculateTotalWeight

    [Fact]
    public void CalculateTotalWeight_NullCapabilities_ReturnsZero()
    {
        var result = CarryWeightCalculator.CalculateTotalWeight(null, DefaultConfig());
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateTotalWeight_EmptyCapabilities_ReturnsZero()
    {
        var result = CarryWeightCalculator.CalculateTotalWeight(EmptyCapabilities(), DefaultConfig());
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateTotalWeight_BlocksOnly_CalculatesCorrectly()
    {
        var capabilities = new ItemCollection
        {
            Blocks = new[]
            {
                new BlockEntry { BlockRef = "Stone", Quantity = 10 },
                new BlockEntry { BlockRef = "Wood", Quantity = 5 }
            }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        // 10 * 1000 + 5 * 1000 = 15000
        Assert.Equal(15000, result);
    }

    [Fact]
    public void CalculateTotalWeight_EquipmentOnly_CountsPerItem()
    {
        var capabilities = new ItemCollection
        {
            Equipment = new[]
            {
                new EquipmentEntry { EquipmentRef = "Sword", Condition = 1f },
                new EquipmentEntry { EquipmentRef = "Shield", Condition = 1f },
                new EquipmentEntry { EquipmentRef = "Helmet", Condition = 0.5f }
            }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        // 3 items * 2000 = 6000
        Assert.Equal(6000, result);
    }

    [Fact]
    public void CalculateTotalWeight_ToolsOnly_CountsPerItem()
    {
        var capabilities = new ItemCollection
        {
            Tools = new[]
            {
                new ToolEntry { ToolRef = "Pickaxe", Condition = 1f },
                new ToolEntry { ToolRef = "Axe", Condition = 1f }
            }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        // 2 * 1500 = 3000
        Assert.Equal(3000, result);
    }

    [Fact]
    public void CalculateTotalWeight_SpellsOnly_LightWeight()
    {
        var capabilities = new ItemCollection
        {
            Spells = new[]
            {
                new SpellEntry { SpellRef = "Fireball", Condition = 1f },
                new SpellEntry { SpellRef = "Heal", Condition = 1f },
                new SpellEntry { SpellRef = "Shield", Condition = 1f },
                new SpellEntry { SpellRef = "Teleport", Condition = 1f }
            }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        // 4 * 100 = 400
        Assert.Equal(400, result);
    }

    [Fact]
    public void CalculateTotalWeight_ConsumablesOnly_MultipliedByQuantity()
    {
        var capabilities = new ItemCollection
        {
            Consumables = new[]
            {
                new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 5 },
                new ConsumableEntry { ConsumableRef = "Bread", Quantity = 10 }
            }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        // 5 * 250 + 10 * 250 = 3750
        Assert.Equal(3750, result);
    }

    [Fact]
    public void CalculateTotalWeight_BuildingMaterialsOnly_MultipliedByQuantity()
    {
        var capabilities = new ItemCollection
        {
            BuildingMaterials = new[]
            {
                new BuildingMaterialEntry { BuildingMaterialRef = "Plank", Quantity = 20 }
            }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        // 20 * 500 = 10000
        Assert.Equal(10000, result);
    }

    [Fact]
    public void CalculateTotalWeight_QuestTokens_AreWeightless()
    {
        var capabilities = new ItemCollection
        {
            QuestTokens = new[]
            {
                new QuestTokenEntry { QuestTokenRef = "Token1" },
                new QuestTokenEntry { QuestTokenRef = "Token2" },
                new QuestTokenEntry { QuestTokenRef = "Token3" }
            }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateTotalWeight_MixedInventory_SumsAllCategories()
    {
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 5 } },
            Equipment = new[] { new EquipmentEntry { EquipmentRef = "Sword", Condition = 1f } },
            Tools = new[] { new ToolEntry { ToolRef = "Pickaxe", Condition = 1f } },
            Spells = new[] { new SpellEntry { SpellRef = "Fireball", Condition = 1f } },
            Consumables = new[] { new ConsumableEntry { ConsumableRef = "Potion", Quantity = 3 } },
            BuildingMaterials = new[] { new BuildingMaterialEntry { BuildingMaterialRef = "Plank", Quantity = 2 } },
            QuestTokens = new[] { new QuestTokenEntry { QuestTokenRef = "Token" } }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        // Blocks: 5*1000 = 5000
        // Equipment: 1*2000 = 2000
        // Tools: 1*1500 = 1500
        // Spells: 1*100 = 100
        // Consumables: 3*250 = 750
        // Materials: 2*500 = 1000
        // QuestTokens: 0
        // Total: 10350
        Assert.Equal(10350, result);
    }

    #endregion

    #region GetRemainingCapacity

    [Fact]
    public void GetRemainingCapacity_EmptyInventory_ReturnsFullCapacity()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var result = CarryWeightCalculator.GetRemainingCapacity(EmptyCapabilities(), archetype, DefaultConfig());
        Assert.Equal(50000, result);
    }

    [Fact]
    public void GetRemainingCapacity_NullCapabilities_ReturnsFullCapacity()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var result = CarryWeightCalculator.GetRemainingCapacity(null, archetype, DefaultConfig());
        Assert.Equal(50000, result);
    }

    [Fact]
    public void GetRemainingCapacity_PartiallyLoaded_ReturnsCorrectRemaining()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 20 } } // 20000 weight
        };

        var result = CarryWeightCalculator.GetRemainingCapacity(capabilities, archetype, DefaultConfig());
        Assert.Equal(30000, result);
    }

    [Fact]
    public void GetRemainingCapacity_Overloaded_ReturnsNegative()
    {
        var archetype = ArchetypeWithCapacity(5000);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 10 } } // 10000 weight
        };

        var result = CarryWeightCalculator.GetRemainingCapacity(capabilities, archetype, DefaultConfig());
        Assert.Equal(-5000, result);
    }

    #endregion

    #region WouldExceedCapacity

    [Fact]
    public void WouldExceedCapacity_UnderLimit_ReturnsFalse()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var capabilities = EmptyCapabilities();

        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 1000);
        Assert.False(result);
    }

    [Fact]
    public void WouldExceedCapacity_ExactlyAtLimit_ReturnsFalse()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var capabilities = EmptyCapabilities();

        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 50000);
        Assert.False(result);
    }

    [Fact]
    public void WouldExceedCapacity_OverLimit_ReturnsTrue()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var capabilities = EmptyCapabilities();

        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 50001);
        Assert.True(result);
    }

    [Fact]
    public void WouldExceedCapacity_ExistingItemsPlusNew_ExceedsLimit()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 45 } } // 45000 weight
        };

        // Adding 6000 more would exceed 50000
        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 6000);
        Assert.True(result);
    }

    [Fact]
    public void WouldExceedCapacity_ExistingItemsPlusNew_StillUnderLimit()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 45 } } // 45000 weight
        };

        // Adding 5000 exactly fills capacity
        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 5000);
        Assert.False(result);
    }

    [Fact]
    public void WouldExceedCapacity_NullArchetype_UsesDefaultCapacity()
    {
        var capabilities = EmptyCapabilities();

        // Default is 50000, adding 49999 should be fine
        Assert.False(CarryWeightCalculator.WouldExceedCapacity(capabilities, null, DefaultConfig(), 49999));
        // Adding 50001 should exceed
        Assert.True(CarryWeightCalculator.WouldExceedCapacity(capabilities, null, DefaultConfig(), 50001));
    }

    [Fact]
    public void WouldExceedCapacity_ZeroAdditionalWeight_ReturnsFalse()
    {
        var archetype = ArchetypeWithCapacity(50000);
        var capabilities = EmptyCapabilities();

        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 0);
        Assert.False(result);
    }

    #endregion

    #region GetCategoryWeight

    [Theory]
    [InlineData("block", 1000)]
    [InlineData("equipment", 2000)]
    [InlineData("tool", 1500)]
    [InlineData("spell", 100)]
    [InlineData("consumable", 250)]
    [InlineData("buildingmaterial", 500)]
    public void GetCategoryWeight_KnownCategories_ReturnsCorrectWeight(string category, int expected)
    {
        var result = CarryWeightCalculator.GetCategoryWeight(category, DefaultConfig());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("questtoken")]
    [InlineData("")]
    public void GetCategoryWeight_UnknownCategory_ReturnsZero(string category)
    {
        var result = CarryWeightCalculator.GetCategoryWeight(category, DefaultConfig());
        Assert.Equal(0, result);
    }

    #endregion

    #region Custom World Configuration Weights

    [Fact]
    public void CalculateTotalWeight_CustomWeights_UsesWorldConfig()
    {
        var config = new StubWorldConfiguration
        {
            BlockWeight = 500,     // Half default
            EquipmentWeight = 100, // Very light equipment
            ConsumableWeight = 50,
            ToolWeight = 200,
            SpellWeight = 10,
            BuildingMaterialWeight = 300,
            WeightUnitName = "lbs"
        };

        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 10 } },
            Equipment = new[] { new EquipmentEntry { EquipmentRef = "Sword", Condition = 1f } }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, config);
        // 10*500 + 1*100 = 5100
        Assert.Equal(5100, result);
    }

    [Fact]
    public void WouldExceedCapacity_LowCapacityMage_BlockedEarlier()
    {
        var mageArchetype = ArchetypeWithCapacity(35000);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 30 } }, // 30000
            Equipment = new[]
            {
                new EquipmentEntry { EquipmentRef = "Staff", Condition = 1f },
                new EquipmentEntry { EquipmentRef = "Robe", Condition = 1f }
            } // 4000
        };
        // Total: 34000 out of 35000 — remaining = 1000

        // Adding 1000 exactly fills capacity — not exceeded
        Assert.False(CarryWeightCalculator.WouldExceedCapacity(capabilities, mageArchetype, DefaultConfig(), 1000));

        // Adding 1001 exceeds
        Assert.True(CarryWeightCalculator.WouldExceedCapacity(capabilities, mageArchetype, DefaultConfig(), 1001));

        // Adding a spell (100) is fine
        Assert.False(CarryWeightCalculator.WouldExceedCapacity(capabilities, mageArchetype, DefaultConfig(), 100));
    }

    [Fact]
    public void WouldExceedCapacity_HighCapacityWarrior_CanCarryMore()
    {
        var warriorArchetype = ArchetypeWithCapacity(60000);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 50 } } // 50000
        };

        // Warrior still has 10000 capacity left
        Assert.False(CarryWeightCalculator.WouldExceedCapacity(capabilities, warriorArchetype, DefaultConfig(), 10000));
        Assert.True(CarryWeightCalculator.WouldExceedCapacity(capabilities, warriorArchetype, DefaultConfig(), 10001));
    }

    #endregion

    /// <summary>
    /// Minimal stub for IWorldConfiguration used in carry weight tests.
    /// </summary>
    private class StubWorldConfiguration : IWorldConfiguration
    {
        public string RefName { get; set; } = "Test";
        public string ContentPackLibrary { get; set; } = "default";
        public string ContentPackTheme { get; set; } = "default";
        public string Namespace { get; set; } = "test";
        public double SpawnLatitude { get; set; }
        public double SpawnLongitude { get; set; }
        public IProceduralSettings ProceduralSettings { get; set; } = null!;
        public IHeightMapSettings HeightMapSettings { get; set; } = null!;
        public string CurrencyName { get; set; } = "Credit";
        public DateTime StartDate { get; set; }
        public int SecondsInHour { get; set; } = 60;
        public object Item { get; set; } = null!;
        public string DisplayName { get; set; } = "Test";
        public string Description { get; set; } = "Test";
        public ClimateModel ClimateModel { get; set; }
        public bool AllowTeleporting { get; set; }
        public int DisplayOrder { get; set; }
        public string WeightUnitName { get; set; } = "kg";
        public int BlockWeight { get; set; } = 1000;
        public int EquipmentWeight { get; set; } = 2000;
        public int ToolWeight { get; set; } = 1500;
        public int SpellWeight { get; set; } = 100;
        public int ConsumableWeight { get; set; } = 250;
        public int BuildingMaterialWeight { get; set; } = 500;
        public string? SourceDirectory { get; set; }
    }
}
