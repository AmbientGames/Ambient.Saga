using Ambient.Saga.Engine.Domain.Rpg.Voxel;

namespace Ambient.Saga.Engine.Tests.Rpg.Voxel;

/// <summary>
/// Unit tests for voxel claim DTOs and value objects.
/// </summary>
public class VoxelClaimDtosTests
{
    [Fact]
    public void Position3D_Constructor_SetsProperties()
    {
        // Arrange & Act
        var position = new Position3D(123.456, 78.9, 234.567);

        // Assert
        Assert.Equal(123.456, position.X);
        Assert.Equal(78.9, position.Y);
        Assert.Equal(234.567, position.Z);
    }

    [Fact]
    public void Position3D_ToString_FormatsCorrectly()
    {
        // Arrange
        var position = new Position3D(123.456, 78.9, 234.567);

        // Act
        var str = position.ToString();

        // Assert
        Assert.Contains("123.456", str);
        Assert.Contains("78.9", str);
        Assert.Contains("234.567", str);
    }

    [Fact]
    public void Position3D_Parse_ValidString_ReturnsPosition()
    {
        // Arrange
        var str = "123.456,78.9,234.567";

        // Act
        var position = Position3D.Parse(str);

        // Assert
        Assert.Equal(123.456, position.X);
        Assert.Equal(78.9, position.Y);
        Assert.Equal(234.567, position.Z);
    }

    [Fact]
    public void Position3D_Parse_InvalidString_ThrowsException()
    {
        // Arrange
        var str = "invalid";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => Position3D.Parse(str));
    }

    [Fact]
    public void MinedBlock_ToString_IncludesTypeAndPosition()
    {
        // Arrange
        var block = new MinedBlock
        {
            BlockType = "stone",
            Position = new Position3D(100, 50, 200),
            MinedAt = DateTime.UtcNow
        };

        // Act
        var str = block.ToString();

        // Assert
        Assert.Contains("stone", str);
        Assert.Contains("@", str);
    }

    [Fact]
    public void MiningSessionClaim_MiningRate_CalculatesCorrectly()
    {
        // Arrange
        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            BlocksMined = new List<MinedBlock>
            {
                new MinedBlock { BlockType = "stone", Position = new Position3D(0, 0, 0) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(1, 0, 0) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(2, 0, 0) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(3, 0, 0) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(4, 0, 0) }
            }
        };

        // Act
        var rate = claim.MiningRate;

        // Assert - 5 blocks in 10 seconds = 0.5 blocks/sec
        Assert.Equal(0.5, rate, precision: 2);
    }

    [Fact]
    public void MiningSessionClaim_BlockCount_ReturnsCorrectCount()
    {
        // Arrange
        var claim = new MiningSessionClaim
        {
            BlocksMined = new List<MinedBlock>
            {
                new MinedBlock { BlockType = "stone", Position = new Position3D(0, 0, 0) },
                new MinedBlock { BlockType = "dirt", Position = new Position3D(1, 0, 0) },
                new MinedBlock { BlockType = "gravel", Position = new Position3D(2, 0, 0) }
            }
        };

        // Act
        var count = claim.BlockCount;

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void BuildingSessionClaim_BuildingRate_CalculatesCorrectly()
    {
        // Arrange
        var claim = new BuildingSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow,
            BlocksPlaced = new List<PlacedBlock>
            {
                new PlacedBlock { BlockType = "stone", Position = new Position3D(0, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(1, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(2, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(3, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(4, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(5, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(6, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(7, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(8, 0, 0) },
                new PlacedBlock { BlockType = "stone", Position = new Position3D(9, 0, 0) }
            }
        };

        // Act
        var rate = claim.BuildingRate;

        // Assert - 10 blocks in 5 seconds = 2.0 blocks/sec
        Assert.Equal(2.0, rate, precision: 2);
    }

    [Fact]
    public void InventorySnapshotClaim_TotalBlockCount_SumsCorrectly()
    {
        // Arrange
        var claim = new InventorySnapshotClaim
        {
            Blocks = new Dictionary<string, int>
            {
                ["stone"] = 100,
                ["dirt"] = 50,
                ["gravel"] = 25
            }
        };

        // Act
        var total = claim.TotalBlockCount;

        // Assert
        Assert.Equal(175, total);
    }

    [Fact]
    public void InventorySnapshotClaim_TotalToolCount_CountsCorrectly()
    {
        // Arrange
        var claim = new InventorySnapshotClaim
        {
            Tools = new Dictionary<string, float>
            {
                ["IronPickaxe"] = 0.85f,
                ["DiamondSword"] = 0.95f,
                ["WoodenAxe"] = 0.45f
            }
        };

        // Act
        var total = claim.TotalToolCount;

        // Assert
        Assert.Equal(3, total);
    }

    [Fact]
    public void ClaimValidationResult_Success_IsValid()
    {
        // Act
        var result = ClaimValidationResult.Success();

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Valid", result.Reason);
        Assert.Equal(0.0f, result.CheatConfidence);
    }

    [Fact]
    public void ClaimValidationResult_Failure_IsNotValid()
    {
        // Act
        var result = ClaimValidationResult.Failure("Test failure", 0.85f);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Test failure", result.Reason);
        Assert.Equal(0.85f, result.CheatConfidence);
    }

    [Fact]
    public void ClaimValidationResult_Warning_IsValidWithWarnings()
    {
        // Act
        var result = ClaimValidationResult.Warning("High mining rate");

        // Assert
        Assert.True(result.IsValid);
        Assert.Single(result.ValidationWarnings);
        Assert.Contains("High mining rate", result.ValidationWarnings);
        Assert.Equal(0.2f, result.CheatConfidence);
    }

    [Fact]
    public void ToolWearClaim_CalculatesWearDelta()
    {
        // Arrange
        var claim = new ToolWearClaim
        {
            ToolRef = "IronPickaxe",
            ConditionBefore = 0.85f,
            ConditionAfter = 0.82f,
            BlocksMinedWithTool = 50
        };

        // Act
        var delta = claim.ConditionBefore - claim.ConditionAfter;

        // Assert
        Assert.Equal(0.03f, delta, precision: 4);
    }

    [Fact]
    public void LocationClaim_WithPreviousPosition_CanCalculateVelocity()
    {
        // Arrange
        var claim = new LocationClaim
        {
            Timestamp = DateTime.UtcNow,
            Position = new Position3D(100, 50, 200),
            PreviousPosition = new Position3D(95, 50, 200),
            PreviousTimestamp = DateTime.UtcNow.AddSeconds(-1)
        };

        // Act
        var distance = VoxelGameConstants.CalculateDistance(
            claim.PreviousPosition.X, claim.PreviousPosition.Y, claim.PreviousPosition.Z,
            claim.Position.X, claim.Position.Y, claim.Position.Z);
        var velocity = distance / (claim.Timestamp - claim.PreviousTimestamp.Value).TotalSeconds;

        // Assert - Moved 5 meters in 1 second = 5 m/s
        Assert.Equal(5.0, velocity, precision: 2);
    }
}
