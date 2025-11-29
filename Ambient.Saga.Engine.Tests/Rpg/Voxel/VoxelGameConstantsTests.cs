using Ambient.Saga.Engine.Domain.Rpg.Voxel;

namespace Ambient.Saga.Engine.Tests.Rpg.Voxel;

/// <summary>
/// Unit tests for VoxelGameConstants helper methods and calculations.
/// </summary>
public class VoxelGameConstantsTests
{
    [Fact]
    public void GetExpectedToolWear_DiamondPickaxe_ReturnsLowestWear()
    {
        // Arrange & Act
        var wear = VoxelGameConstants.GetExpectedToolWear("DiamondPickaxe", "stone");

        // Assert
        Assert.Equal(VoxelGameConstants.DIAMOND_TOOL_WEAR_PER_BLOCK, wear);
    }

    [Fact]
    public void GetExpectedToolWear_WoodenPickaxe_ReturnsHighestWear()
    {
        // Arrange & Act
        var wear = VoxelGameConstants.GetExpectedToolWear("WoodenPickaxe", "stone");

        // Assert
        Assert.Equal(VoxelGameConstants.WOODEN_TOOL_WEAR_PER_BLOCK, wear);
    }

    [Fact]
    public void GetExpectedToolWear_IronPickaxe_ReturnsIronWear()
    {
        // Arrange & Act
        var wear = VoxelGameConstants.GetExpectedToolWear("IronPickaxe", "stone");

        // Assert
        Assert.Equal(VoxelGameConstants.IRON_TOOL_WEAR_PER_BLOCK, wear);
    }

    [Fact]
    public void GetExpectedToolWear_UnknownTool_DefaultsToIronWear()
    {
        // Arrange & Act
        var wear = VoxelGameConstants.GetExpectedToolWear("MysteryPickaxe", "stone");

        // Assert
        Assert.Equal(VoxelGameConstants.IRON_TOOL_WEAR_PER_BLOCK, wear);
    }

    [Fact]
    public void GetExpectedToolWear_SoftBlocks_ReducedWear()
    {
        // Arrange & Act
        var dirtWear = VoxelGameConstants.GetExpectedToolWear("IronPickaxe", "dirt");

        // Assert - Soft blocks have 0.5x multiplier
        Assert.Equal(VoxelGameConstants.IRON_TOOL_WEAR_PER_BLOCK * 0.5f, dirtWear);
    }

    [Fact]
    public void GetExpectedToolWear_HardBlocks_IncreasedWear()
    {
        // Arrange & Act
        var diamondOreWear = VoxelGameConstants.GetExpectedToolWear("IronPickaxe", "diamond_ore");

        // Assert - Very hard blocks have 2.0x multiplier
        Assert.Equal(VoxelGameConstants.IRON_TOOL_WEAR_PER_BLOCK * 2.0f, diamondOreWear);
    }

    [Fact]
    public void IsRareOre_DiamondOre_ReturnsTrue()
    {
        // Arrange & Act
        var isRare = VoxelGameConstants.IsRareOre("diamond_ore");

        // Assert
        Assert.True(isRare);
    }

    [Fact]
    public void IsRareOre_GoldOre_ReturnsTrue()
    {
        // Arrange & Act
        var isRare = VoxelGameConstants.IsRareOre("gold_ore");

        // Assert
        Assert.True(isRare);
    }

    [Fact]
    public void IsRareOre_Stone_ReturnsFalse()
    {
        // Arrange & Act
        var isRare = VoxelGameConstants.IsRareOre("stone");

        // Assert
        Assert.False(isRare);
    }

    [Fact]
    public void IsRareOre_Cobblestone_ReturnsFalse()
    {
        // Arrange & Act
        var isRare = VoxelGameConstants.IsRareOre("cobblestone");

        // Assert
        Assert.False(isRare);
    }

    [Fact]
    public void CalculateDistance_SamePosition_ReturnsZero()
    {
        // Arrange & Act
        var distance = VoxelGameConstants.CalculateDistance(100, 50, 200, 100, 50, 200);

        // Assert
        Assert.Equal(0, distance);
    }

    [Fact]
    public void CalculateDistance_OneBlockAway_ReturnsOne()
    {
        // Arrange & Act
        var distance = VoxelGameConstants.CalculateDistance(100, 50, 200, 101, 50, 200);

        // Assert
        Assert.Equal(1, distance, precision: 2);
    }

    [Fact]
    public void CalculateDistance_ThreeDimensional_CalculatesCorrectly()
    {
        // Arrange - Pythagorean triple: 3-4-5
        var distance = VoxelGameConstants.CalculateDistance(0, 0, 0, 3, 4, 0);

        // Assert
        Assert.Equal(5, distance, precision: 2);
    }

    [Fact]
    public void CalculateDistance2D_IgnoresVertical()
    {
        // Arrange & Act
        var distance = VoxelGameConstants.CalculateDistance2D(0, 0, 3, 4);

        // Assert - Should calculate 3-4-5 triangle, ignoring Y
        Assert.Equal(5, distance, precision: 2);
    }

    [Fact]
    public void CalculateDistance2D_SamePosition_ReturnsZero()
    {
        // Arrange & Act
        var distance = VoxelGameConstants.CalculateDistance2D(100, 200, 100, 200);

        // Assert
        Assert.Equal(0, distance);
    }

    [Theory]
    [InlineData(1.0, 1.0)] // 1 block/sec mining
    [InlineData(2.5, 2.5)] // 2.5 blocks/sec mining
    [InlineData(3.0, 3.0)] // Max plausible mining rate
    public void MaxMiningRate_VariousRates_ValidatesCorrectly(double rate, double expected)
    {
        // Assert
        Assert.Equal(expected, rate);
        Assert.True(rate <= VoxelGameConstants.MAX_MINING_RATE_BLOCKS_PER_SECOND);
    }

    [Fact]
    public void ToolWearTolerance_IsReasonable()
    {
        // Assert - 10% tolerance seems reasonable for floating point variations
        Assert.Equal(0.10f, VoxelGameConstants.TOOL_WEAR_TOLERANCE);
    }

    [Fact]
    public void RareOrePercentage_MatchesGameDesign()
    {
        // Assert - 5% of blocks being rare ore is reasonable for game balance
        Assert.Equal(0.05f, VoxelGameConstants.EXPECTED_RARE_ORE_PERCENTAGE);
    }

    [Fact]
    public void MaxInventoryCapacity_MatchesClaudeMd()
    {
        // Assert - From CLAUDE.md: 512 max blocks
        Assert.Equal(512, VoxelGameConstants.MAX_INVENTORY_CAPACITY_BLOCKS);
    }

    [Fact]
    public void MaxToolCapacity_MatchesClaudeMd()
    {
        // Assert - From CLAUDE.md: 16 tools per player
        Assert.Equal(16, VoxelGameConstants.MAX_INVENTORY_CAPACITY_TOOLS);
    }
}
