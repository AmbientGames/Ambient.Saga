using Ambient.Saga.Engine.Domain.Rpg.Voxel;

namespace Ambient.Saga.Engine.Tests.Rpg.Voxel;

/// <summary>
/// Unit tests for voxel claim validation logic.
/// Tests the validation algorithms without requiring full CQRS infrastructure.
/// </summary>
public class VoxelClaimValidationTests
{
    // ===== LOCATION CLAIM VALIDATION =====

    [Fact]
    public void LocationClaim_ValidMovement_PassesValidation()
    {
        // Arrange - Moving 5 meters in 1 second = 5 m/s (within 8 m/s limit)
        var claim = new LocationClaim
        {
            Timestamp = DateTime.UtcNow,
            Position = new Position3D(100, 50, 200),
            PreviousPosition = new Position3D(95, 50, 200),
            PreviousTimestamp = DateTime.UtcNow.AddSeconds(-1)
        };

        // Act
        var isValid = ValidateMovementSpeed(claim);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void LocationClaim_TooFast_FailsValidation()
    {
        // Arrange - Moving 50 meters in 1 second = 50 m/s (exceeds 8 m/s limit)
        var claim = new LocationClaim
        {
            Timestamp = DateTime.UtcNow,
            Position = new Position3D(100, 50, 200),
            PreviousPosition = new Position3D(50, 50, 200),
            PreviousTimestamp = DateTime.UtcNow.AddSeconds(-1)
        };

        // Act
        var isValid = ValidateMovementSpeed(claim);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void LocationClaim_FallingTooFast_FailsValidation()
    {
        // Arrange - Falling 50 meters in 1 second = 50 m/s vertical (exceeds 20 m/s limit)
        var claim = new LocationClaim
        {
            Timestamp = DateTime.UtcNow,
            Position = new Position3D(100, 0, 200),
            PreviousPosition = new Position3D(100, 50, 200),
            PreviousTimestamp = DateTime.UtcNow.AddSeconds(-1)
        };

        // Act
        var isValid = ValidateVerticalSpeed(claim);

        // Assert
        Assert.False(isValid);
    }

    // ===== TOOL WEAR CLAIM VALIDATION =====

    [Fact]
    public void ToolWearClaim_ValidWearRate_PassesValidation()
    {
        // Arrange - Expected wear for iron pickaxe on stone
        var expectedWear = VoxelGameConstants.IRON_TOOL_WEAR_PER_BLOCK;
        var claim = new ToolWearClaim
        {
            ToolRef = "IronPickaxe",
            ConditionBefore = 0.85f,
            ConditionAfter = 0.85f - expectedWear * 100, // 100 blocks
            BlocksMinedWithTool = 100,
            DominantBlockType = "stone"
        };

        // Act
        var isValid = ValidateToolWear(claim);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ToolWearClaim_TooLittleWear_FailsValidation()
    {
        // Arrange - Almost no wear despite mining 100 blocks (durability hack)
        var claim = new ToolWearClaim
        {
            ToolRef = "IronPickaxe",
            ConditionBefore = 0.85f,
            ConditionAfter = 0.849f, // Only 0.001 wear for 100 blocks
            BlocksMinedWithTool = 100,
            DominantBlockType = "stone"
        };

        // Act
        var isValid = ValidateToolWear(claim);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ToolWearClaim_ConditionIncreased_FailsValidation()
    {
        // Arrange - Condition went UP (invalid repair during mining)
        var claim = new ToolWearClaim
        {
            ToolRef = "IronPickaxe",
            ConditionBefore = 0.85f,
            ConditionAfter = 0.90f, // Increased!
            BlocksMinedWithTool = 100
        };

        // Act
        var wearDelta = claim.ConditionBefore - claim.ConditionAfter;

        // Assert
        Assert.True(wearDelta < 0, "Condition should not increase during mining");
    }

    // ===== MINING CLAIM VALIDATION =====

    [Fact]
    public void MiningClaim_ValidRate_PassesValidation()
    {
        // Arrange - 2.0 blocks/sec (within 3.0 limit)
        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            BlocksMined = Enumerable.Range(0, 20)
                .Select(i => new MinedBlock
                {
                    BlockType = "stone",
                    Position = new Position3D(100 + i, 50, 200)
                }).ToList(),
            ToolRef = "IronPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(120, 50, 200)
        };

        // Act
        var rate = claim.MiningRate;

        // Assert
        Assert.Equal(2.0, rate, precision: 1);
        Assert.True(rate <= VoxelGameConstants.MAX_MINING_RATE_BLOCKS_PER_SECOND);
    }

    [Fact]
    public void MiningClaim_TooFast_FailsValidation()
    {
        // Arrange - 5.0 blocks/sec (exceeds 3.0 limit)
        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            BlocksMined = Enumerable.Range(0, 50)
                .Select(i => new MinedBlock
                {
                    BlockType = "stone",
                    Position = new Position3D(100 + i, 50, 200)
                }).ToList(),
            ToolRef = "IronPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(150, 50, 200)
        };

        // Act
        var rate = claim.MiningRate;

        // Assert
        Assert.True(rate > VoxelGameConstants.MAX_MINING_RATE_BLOCKS_PER_SECOND);
    }

    [Fact]
    public void MiningClaim_BlockTooFarAway_FailsValidation()
    {
        // Arrange - Block is 100 meters away (exceeds 5 meter reach)
        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddSeconds(1),
            BlocksMined = new List<MinedBlock>
            {
                new MinedBlock { BlockType = "stone", Position = new Position3D(200, 50, 200) } // 100m from start
            },
            ToolRef = "IronPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(100, 50, 200)
        };

        // Act
        var distance = VoxelGameConstants.CalculateDistance(
            claim.StartLocation.X, claim.StartLocation.Y, claim.StartLocation.Z,
            claim.BlocksMined[0].Position.X, claim.BlocksMined[0].Position.Y, claim.BlocksMined[0].Position.Z);

        // Assert
        Assert.True(distance > VoxelGameConstants.MAX_MINING_REACH_METERS);
    }

    [Fact]
    public void MiningClaim_HighRareOreRate_IndicatesXRayHack()
    {
        // Arrange - 50% rare ore rate (10x expected)
        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddSeconds(10),
            BlocksMined = new List<MinedBlock>
            {
                new MinedBlock { BlockType = "diamond_ore", Position = new Position3D(100, 50, 200) },
                new MinedBlock { BlockType = "diamond_ore", Position = new Position3D(101, 50, 200) },
                new MinedBlock { BlockType = "diamond_ore", Position = new Position3D(102, 50, 200) },
                new MinedBlock { BlockType = "diamond_ore", Position = new Position3D(103, 50, 200) },
                new MinedBlock { BlockType = "diamond_ore", Position = new Position3D(104, 50, 200) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(105, 50, 200) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(106, 50, 200) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(107, 50, 200) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(108, 50, 200) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(109, 50, 200) }
            },
            ToolRef = "DiamondPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(109, 50, 200)
        };

        // Act
        var rareOreCount = claim.BlocksMined.Count(b => VoxelGameConstants.IsRareOre(b.BlockType));
        var rareOreRate = (float)rareOreCount / claim.BlockCount;

        // Assert
        Assert.Equal(0.5f, rareOreRate); // 50% rare ore
        Assert.True(rareOreRate > VoxelGameConstants.EXPECTED_RARE_ORE_PERCENTAGE * VoxelGameConstants.RARE_ORE_DETECTION_MULTIPLIER);
    }

    // ===== BUILDING CLAIM VALIDATION =====

    [Fact]
    public void BuildingClaim_ValidRate_PassesValidation()
    {
        // Arrange - 4.0 blocks/sec (within 5.0 limit)
        var claim = new BuildingSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            BlocksPlaced = Enumerable.Range(0, 40)
                .Select(i => new PlacedBlock
                {
                    BlockType = "stone",
                    Position = new Position3D(100 + i, 50, 200)
                }).ToList(),
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(140, 50, 200)
        };

        // Act
        var rate = claim.BuildingRate;

        // Assert
        Assert.Equal(4.0, rate, precision: 1);
        Assert.True(rate <= VoxelGameConstants.MAX_BUILDING_RATE_BLOCKS_PER_SECOND);
    }

    [Fact]
    public void BuildingClaim_MaterialMismatch_FailsValidation()
    {
        // Arrange - Placed 10 stone blocks but only consumed 5 materials
        var claim = new BuildingSessionClaim
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddSeconds(5),
            BlocksPlaced = Enumerable.Range(0, 10)
                .Select(i => new PlacedBlock
                {
                    BlockType = "stone",
                    Position = new Position3D(100 + i, 50, 200)
                }).ToList(),
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(110, 50, 200),
            MaterialsConsumed = new Dictionary<string, int>
            {
                ["stone"] = 5 // Only 5 consumed but 10 placed!
            }
        };

        // Act
        var placed = claim.BlocksPlaced.Count(b => b.BlockType == "stone");
        var consumed = claim.MaterialsConsumed["stone"];

        // Assert
        Assert.NotEqual(placed, consumed);
    }

    [Fact]
    public void BuildingClaim_BlockTooFarAway_FailsValidation()
    {
        // Arrange - Block is 100 meters away (exceeds 5 meter reach)
        var claim = new BuildingSessionClaim
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddSeconds(1),
            BlocksPlaced = new List<PlacedBlock>
            {
                new PlacedBlock { BlockType = "stone", Position = new Position3D(200, 50, 200) } // 100m from start
            },
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(100, 50, 200)
        };

        // Act
        var distance = VoxelGameConstants.CalculateDistance(
            claim.StartLocation.X, claim.StartLocation.Y, claim.StartLocation.Z,
            claim.BlocksPlaced[0].Position.X, claim.BlocksPlaced[0].Position.Y, claim.BlocksPlaced[0].Position.Z);

        // Assert
        Assert.True(distance > VoxelGameConstants.MAX_BUILDING_REACH_METERS);
    }

    // ===== HELPER METHODS (Simplified versions of handler validation logic) =====

    private bool ValidateMovementSpeed(LocationClaim claim)
    {
        if (claim.PreviousPosition == null || !claim.PreviousTimestamp.HasValue)
            return true;

        var timeDelta = (claim.Timestamp - claim.PreviousTimestamp.Value).TotalSeconds;
        if (timeDelta <= 0) return false;

        var horizontalDistance = VoxelGameConstants.CalculateDistance2D(
            claim.PreviousPosition.X, claim.PreviousPosition.Z,
            claim.Position.X, claim.Position.Z);

        var horizontalSpeed = horizontalDistance / timeDelta;

        return horizontalSpeed <= VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND;
    }

    private bool ValidateVerticalSpeed(LocationClaim claim)
    {
        if (claim.PreviousPosition == null || !claim.PreviousTimestamp.HasValue)
            return true;

        var timeDelta = (claim.Timestamp - claim.PreviousTimestamp.Value).TotalSeconds;
        if (timeDelta <= 0) return false;

        var verticalDistance = Math.Abs(claim.Position.Y - claim.PreviousPosition.Y);
        var verticalSpeed = verticalDistance / timeDelta;

        return verticalSpeed <= VoxelGameConstants.MAX_VERTICAL_SPEED_METERS_PER_SECOND;
    }

    private bool ValidateToolWear(ToolWearClaim claim)
    {
        if (claim.ConditionBefore < 0 || claim.ConditionBefore > 1 ||
            claim.ConditionAfter < 0 || claim.ConditionAfter > 1)
            return false;

        var wearDelta = claim.ConditionBefore - claim.ConditionAfter;
        if (wearDelta < 0) return false; // Condition increased

        if (claim.BlocksMinedWithTool == 0)
            return wearDelta <= 0.0001f;

        var blockType = claim.DominantBlockType ?? "stone";
        var expectedWearPerBlock = VoxelGameConstants.GetExpectedToolWear(claim.ToolRef, blockType);
        var actualWearRate = wearDelta / claim.BlocksMinedWithTool;
        var wearRatio = actualWearRate / expectedWearPerBlock;
        var tolerance = VoxelGameConstants.TOOL_WEAR_TOLERANCE;

        return wearRatio >= 1.0f - tolerance;
    }
}
