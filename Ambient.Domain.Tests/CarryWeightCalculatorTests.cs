using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.Avatar;

namespace Ambient.Domain.Tests;

public class CarryWeightCalculatorTests
{
    private static IWorldConfiguration DefaultConfig() => new StubWorldConfiguration
    {
        BlockWeight = 1.0f,
        EquipmentWeight = 2.0f,
        ToolWeight = 1.5f,
        SpellWeight = 0.1f,
        ConsumableWeight = 0.25f,
        BuildingMaterialWeight = 0.5f,
        WeightUnitName = "kg"
    };

    /// <summary>
    /// Creates an archetype whose derived max carry weight equals the given value.
    /// Formula: Weight * Strength * 5. Using Weight=weight, Strength=strength.
    /// </summary>
    private static AvatarArchetype ArchetypeWithCapacity(float maxCarryWeight)
    {
        // Reverse-engineer Weight from desired carry capacity using a fixed Strength of 0.1:
        // maxCarryWeight = Weight * 0.1 * 5 → Weight = maxCarryWeight / 0.5 = maxCarryWeight * 2
        var weight = maxCarryWeight * 2f;
        return new AvatarArchetype
        {
            Weight = weight,
            SpawnStats = new CharacterStats { Strength = 0.1f }
        };
    }

    private static ItemCollection EmptyCapabilities() => new();

    #region GetMaxCarryWeight

    [Fact]
    public void GetMaxCarryWeight_ArchetypeWithValue_ReturnsArchetypeValue()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var result = CarryWeightCalculator.GetMaxCarryWeight(archetype);
        Assert.Equal(50.0f, result);
    }

    [Theory]
    [InlineData(20.0f)]
    [InlineData(35.0f)]
    [InlineData(45.0f)]
    [InlineData(50.0f)]
    public void GetMaxCarryWeight_VariousValues_ReturnsCorrectly(float expected)
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
        // 10 * 1.0 + 5 * 1.0 = 15.0 kg
        Assert.Equal(15.0f, result);
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
        // 3 items * 2.0 = 6.0 kg
        Assert.Equal(6.0f, result);
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
        // 2 * 1.5 = 3.0 kg
        Assert.Equal(3.0f, result);
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
        // 4 * 0.1 = 0.4 kg
        Assert.Equal(0.4f, result);
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
        // 5 * 0.25 + 10 * 0.25 = 3.75 kg
        Assert.Equal(3.75f, result);
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
        // 20 * 0.5 = 10.0 kg
        Assert.Equal(10.0f, result);
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
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, DefaultConfig());
        // Blocks: 5*1.0 = 5.0
        // Equipment: 1*2.0 = 2.0
        // Tools: 1*1.5 = 1.5
        // Spells: 1*0.1 = 0.1
        // Consumables: 3*0.25 = 0.75
        // Materials: 2*0.5 = 1.0
        // QuestTokens: 0
        // Total: 10.35 kg
        Assert.Equal(10.35f, result);
    }

    #endregion

    #region GetRemainingCapacity

    [Fact]
    public void GetRemainingCapacity_EmptyInventory_ReturnsFullCapacity()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var result = CarryWeightCalculator.GetRemainingCapacity(EmptyCapabilities(), archetype, DefaultConfig());
        Assert.Equal(50.0f, result);
    }

    [Fact]
    public void GetRemainingCapacity_NullCapabilities_ReturnsFullCapacity()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var result = CarryWeightCalculator.GetRemainingCapacity(null, archetype, DefaultConfig());
        Assert.Equal(50.0f, result);
    }

    [Fact]
    public void GetRemainingCapacity_PartiallyLoaded_ReturnsCorrectRemaining()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 20 } } // 20.0 kg
        };

        var result = CarryWeightCalculator.GetRemainingCapacity(capabilities, archetype, DefaultConfig());
        Assert.Equal(30.0f, result);
    }

    [Fact]
    public void GetRemainingCapacity_Overloaded_ReturnsNegative()
    {
        var archetype = ArchetypeWithCapacity(5.0f);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 10 } } // 10.0 kg
        };

        var result = CarryWeightCalculator.GetRemainingCapacity(capabilities, archetype, DefaultConfig());
        Assert.Equal(-5.0f, result);
    }

    #endregion

    #region WouldExceedCapacity

    [Fact]
    public void WouldExceedCapacity_UnderLimit_ReturnsFalse()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var capabilities = EmptyCapabilities();

        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 1.0f);
        Assert.False(result);
    }

    [Fact]
    public void WouldExceedCapacity_ExactlyAtLimit_ReturnsFalse()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var capabilities = EmptyCapabilities();

        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 50.0f);
        Assert.False(result);
    }

    [Fact]
    public void WouldExceedCapacity_OverLimit_ReturnsTrue()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var capabilities = EmptyCapabilities();

        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 50.1f);
        Assert.True(result);
    }

    [Fact]
    public void WouldExceedCapacity_ExistingItemsPlusNew_ExceedsLimit()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 45 } } // 45.0 kg
        };

        // Adding 6.0 more would exceed 50.0
        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 6.0f);
        Assert.True(result);
    }

    [Fact]
    public void WouldExceedCapacity_ExistingItemsPlusNew_StillUnderLimit()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 45 } } // 45.0 kg
        };

        // Adding 5.0 exactly fills capacity
        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 5.0f);
        Assert.False(result);
    }

    [Fact]
    public void WouldExceedCapacity_ZeroAdditionalWeight_ReturnsFalse()
    {
        var archetype = ArchetypeWithCapacity(50.0f);
        var capabilities = EmptyCapabilities();

        var result = CarryWeightCalculator.WouldExceedCapacity(capabilities, archetype, DefaultConfig(), 0);
        Assert.False(result);
    }

    #endregion

    #region GetCategoryWeight

    [Theory]
    [InlineData("block", 1.0f)]
    [InlineData("equipment", 2.0f)]
    [InlineData("tool", 1.5f)]
    [InlineData("spell", 0.1f)]
    [InlineData("consumable", 0.25f)]
    [InlineData("buildingmaterial", 0.5f)]
    public void GetCategoryWeight_KnownCategories_ReturnsCorrectWeight(string category, float expected)
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
            BlockWeight = 0.5f,
            EquipmentWeight = 0.1f,
            ConsumableWeight = 0.05f,
            ToolWeight = 0.2f,
            SpellWeight = 0.01f,
            BuildingMaterialWeight = 0.3f,
            WeightUnitName = "lbs"
        };

        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 10 } },
            Equipment = new[] { new EquipmentEntry { EquipmentRef = "Sword", Condition = 1f } }
        };

        var result = CarryWeightCalculator.CalculateTotalWeight(capabilities, config);
        // 10*0.5 + 1*0.1 = 5.1
        Assert.Equal(5.1f, result);
    }

    [Fact]
    public void WouldExceedCapacity_LowCapacityMage_BlockedEarlier()
    {
        var mageArchetype = ArchetypeWithCapacity(25.0f);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 20 } }, // 20.0 kg
            Equipment = new[]
            {
                new EquipmentEntry { EquipmentRef = "Staff", Condition = 1f },
                new EquipmentEntry { EquipmentRef = "Robe", Condition = 1f }
            } // 4.0 kg
        };
        // Total: 24.0 out of 25.0 — remaining = 1.0

        // Adding 1.0 exactly fills capacity — not exceeded
        Assert.False(CarryWeightCalculator.WouldExceedCapacity(capabilities, mageArchetype, DefaultConfig(), 1.0f));

        // Adding 1.1 exceeds
        Assert.True(CarryWeightCalculator.WouldExceedCapacity(capabilities, mageArchetype, DefaultConfig(), 1.1f));

        // Adding a spell (0.1) is fine
        Assert.False(CarryWeightCalculator.WouldExceedCapacity(capabilities, mageArchetype, DefaultConfig(), 0.1f));
    }

    [Fact]
    public void WouldExceedCapacity_HighCapacityWarrior_CanCarryMore()
    {
        var warriorArchetype = ArchetypeWithCapacity(50.0f);
        var capabilities = new ItemCollection
        {
            Blocks = new[] { new BlockEntry { BlockRef = "Stone", Quantity = 40 } } // 40.0 kg
        };

        // Warrior still has 10.0 kg capacity left
        Assert.False(CarryWeightCalculator.WouldExceedCapacity(capabilities, warriorArchetype, DefaultConfig(), 10.0f));
        Assert.True(CarryWeightCalculator.WouldExceedCapacity(capabilities, warriorArchetype, DefaultConfig(), 10.1f));
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
        public float BlockWeight { get; set; } = 1.0f;
        public float EquipmentWeight { get; set; } = 2.0f;
        public float ToolWeight { get; set; } = 1.5f;
        public float SpellWeight { get; set; } = 0.1f;
        public float ConsumableWeight { get; set; } = 0.25f;
        public float BuildingMaterialWeight { get; set; } = 0.5f;
        public string? SourceDirectory { get; set; }
        public string? CompletionQuestRef { get; set; }
    }
}
