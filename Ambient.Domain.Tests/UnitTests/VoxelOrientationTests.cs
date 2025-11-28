using Ambient.Domain.Enums;

namespace Ambient.Domain.Tests.UnitTests;

/// <summary>
/// Unit tests for the VoxelOrientation enum and its associated functionality.
/// </summary>
public class VoxelOrientationTests
{
    [Fact]
    public void VoxelOrientation_ShouldHaveCorrectCardinalDirectionValues()
    {
        // Arrange & Act & Assert
        Assert.Equal(0, (int)VoxelOrientation.SouthNegativeZ);
        Assert.Equal(1, (int)VoxelOrientation.NorthPositiveZ);
        Assert.Equal(2, (int)VoxelOrientation.UpPositiveY);
        Assert.Equal(3, (int)VoxelOrientation.DownNegativeY);
        Assert.Equal(4, (int)VoxelOrientation.EastPositiveX);
        Assert.Equal(5, (int)VoxelOrientation.WestNegativeX);
    }

    [Fact]
    public void VoxelOrientation_ShouldHaveCorrectDiagonalDirectionValues()
    {
        // Arrange & Act & Assert
        Assert.Equal(6, (int)VoxelOrientation.NorthEastPositiveZPositiveX);
        Assert.Equal(7, (int)VoxelOrientation.NorthWestPositiveZNegativeX);
        Assert.Equal(8, (int)VoxelOrientation.SouthEastNegativeZPositiveX);
        Assert.Equal(9, (int)VoxelOrientation.SouthWestNegativeZNegativeX);
    }

    [Fact]
    public void VoxelOrientation_ShouldHaveCorrectSpecialValues()
    {
        // Arrange & Act & Assert
        Assert.Equal(9, (int)VoxelOrientation.Max);
        Assert.Equal(63, (int)VoxelOrientation.All21);
        Assert.Equal(1023, (int)VoxelOrientation.All22);
        Assert.Equal(960, (int)VoxelOrientation.AllDiaganols);
    }

    [Theory]
    [InlineData(VoxelOrientation.NorthEastPositiveZPositiveX)]
    [InlineData(VoxelOrientation.NorthWestPositiveZNegativeX)]
    [InlineData(VoxelOrientation.SouthEastNegativeZPositiveX)]
    [InlineData(VoxelOrientation.SouthWestNegativeZNegativeX)]
    public void VoxelOrientation_DiagonalDirections_ShouldBeGreaterThanCardinals(VoxelOrientation diagonal)
    {
        // Arrange
        var cardinals = new[]
        {
            VoxelOrientation.SouthNegativeZ,
            VoxelOrientation.NorthPositiveZ,
            VoxelOrientation.UpPositiveY,
            VoxelOrientation.DownNegativeY,
            VoxelOrientation.EastPositiveX,
            VoxelOrientation.WestNegativeX
        };

        // Act & Assert
        foreach (var cardinal in cardinals)
        {
            Assert.True((int)diagonal > (int)cardinal, 
                $"Diagonal {diagonal} should have higher value than cardinal {cardinal}");
        }
    }

    [Fact]
    public void VoxelOrientation_Max_ShouldBeHighestSingleOrientationValue()
    {
        // Arrange
        var singleOrientations = new[]
        {
            VoxelOrientation.SouthNegativeZ,
            VoxelOrientation.NorthPositiveZ,
            VoxelOrientation.UpPositiveY,
            VoxelOrientation.DownNegativeY,
            VoxelOrientation.EastPositiveX,
            VoxelOrientation.WestNegativeX,
            VoxelOrientation.NorthEastPositiveZPositiveX,
            VoxelOrientation.NorthWestPositiveZNegativeX,
            VoxelOrientation.SouthEastNegativeZPositiveX,
            VoxelOrientation.SouthWestNegativeZNegativeX
        };

        // Act & Assert
        foreach (var orientation in singleOrientations)
        {
            Assert.True((int)orientation <= (int)VoxelOrientation.Max,
                $"Orientation {orientation} should not exceed Max value");
        }
    }
}
