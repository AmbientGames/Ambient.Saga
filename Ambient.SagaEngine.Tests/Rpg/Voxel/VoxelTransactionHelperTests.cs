using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.SagaEngine.Domain.Rpg.Voxel;

namespace Ambient.SagaEngine.Tests.Rpg.Voxel;

/// <summary>
/// Unit tests for VoxelTransactionHelper transaction creation and serialization.
/// </summary>
public class VoxelTransactionHelperTests
{
    private readonly Guid _testSagaInstanceId = Guid.NewGuid();
    private readonly Guid _testAvatarId = Guid.NewGuid();

    [Fact]
    public void CreateLocationClaimedTransaction_SetsCorrectType()
    {
        // Arrange
        var claim = new LocationClaim
        {
            Timestamp = DateTime.UtcNow,
            Position = new Position3D(100, 50, 200)
        };

        // Act
        var tx = VoxelTransactionHelper.CreateLocationClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        Assert.Equal(SagaTransactionType.LocationClaimed, tx.Type);
        Assert.Equal(_testAvatarId.ToString(), tx.AvatarId);
    }

    [Fact]
    public void CreateLocationClaimedTransaction_StoresPositionData()
    {
        // Arrange
        var claim = new LocationClaim
        {
            Timestamp = DateTime.UtcNow,
            Position = new Position3D(123.456, 78.9, 234.567)
        };

        // Act
        var tx = VoxelTransactionHelper.CreateLocationClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        Assert.Equal("123.4560", tx.Data["PositionX"]);
        Assert.Equal("78.9000", tx.Data["PositionY"]);
        Assert.Equal("234.5670", tx.Data["PositionZ"]);
    }

    [Fact]
    public void CreateLocationClaimedTransaction_WithPreviousPosition_CalculatesVelocity()
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
        var tx = VoxelTransactionHelper.CreateLocationClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        Assert.Contains("Velocity", tx.Data.Keys);
        var velocity = float.Parse(tx.Data["Velocity"]);
        Assert.Equal(5.0f, velocity, precision: 1); // Moved 5 meters in 1 second
    }

    [Fact]
    public void CreateToolWearClaimedTransaction_SetsCorrectType()
    {
        // Arrange
        var claim = new ToolWearClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            ToolRef = "IronPickaxe",
            ConditionBefore = 0.85f,
            ConditionAfter = 0.82f,
            BlocksMinedWithTool = 50
        };

        // Act
        var tx = VoxelTransactionHelper.CreateToolWearClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        Assert.Equal(SagaTransactionType.ToolWearClaimed, tx.Type);
    }

    [Fact]
    public void CreateToolWearClaimedTransaction_CalculatesWearDelta()
    {
        // Arrange
        var claim = new ToolWearClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            ToolRef = "IronPickaxe",
            ConditionBefore = 0.85f,
            ConditionAfter = 0.82f,
            BlocksMinedWithTool = 50
        };

        // Act
        var tx = VoxelTransactionHelper.CreateToolWearClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        var wearDelta = float.Parse(tx.Data["WearDelta"]);
        Assert.Equal(0.03f, wearDelta, precision: 4);
    }

    [Fact]
    public void CreateMiningSessionClaimedTransaction_SetsCorrectType()
    {
        // Arrange
        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            BlocksMined = new List<MinedBlock>
            {
                new MinedBlock { BlockType = "stone", Position = new Position3D(100, 50, 200) },
                new MinedBlock { BlockType = "dirt", Position = new Position3D(101, 50, 200) }
            },
            ToolRef = "IronPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(101, 50, 200)
        };

        // Act
        var tx = VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        Assert.Equal(SagaTransactionType.MiningSessionClaimed, tx.Type);
    }

    [Fact]
    public void CreateMiningSessionClaimedTransaction_CalculatesRareOrePercentage()
    {
        // Arrange
        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            BlocksMined = new List<MinedBlock>
            {
                new MinedBlock { BlockType = "stone", Position = new Position3D(100, 50, 200) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(101, 50, 200) },
                new MinedBlock { BlockType = "diamond_ore", Position = new Position3D(102, 50, 200) }, // Rare
                new MinedBlock { BlockType = "stone", Position = new Position3D(103, 50, 200) }
            },
            ToolRef = "IronPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(103, 50, 200)
        };

        // Act
        var tx = VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        var rareOrePercentage = float.Parse(tx.Data["RareOrePercentage"]);
        Assert.Equal(0.25f, rareOrePercentage); // 1 out of 4 = 25%
    }

    [Fact]
    public void CreateMiningSessionClaimedTransaction_TracksBlockTypeDistribution()
    {
        // Arrange
        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            BlocksMined = new List<MinedBlock>
            {
                new MinedBlock { BlockType = "stone", Position = new Position3D(100, 50, 200) },
                new MinedBlock { BlockType = "stone", Position = new Position3D(101, 50, 200) },
                new MinedBlock { BlockType = "dirt", Position = new Position3D(102, 50, 200) },
                new MinedBlock { BlockType = "gravel", Position = new Position3D(103, 50, 200) }
            },
            ToolRef = "IronPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(103, 50, 200)
        };

        // Act
        var tx = VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        Assert.Contains("BlockTypeDistribution", tx.Data.Keys);
        Assert.Contains("stone:2", tx.Data["BlockTypeDistribution"]);
        Assert.Contains("dirt:1", tx.Data["BlockTypeDistribution"]);
        Assert.Contains("gravel:1", tx.Data["BlockTypeDistribution"]);
    }

    [Fact]
    public void DeserializeMinedBlocks_RoundTrip_PreservesData()
    {
        // Arrange
        var originalBlocks = new List<MinedBlock>
        {
            new MinedBlock { BlockType = "stone", Position = new Position3D(100, 50, 200) },
            new MinedBlock { BlockType = "dirt", Position = new Position3D(101, 51, 201) }
        };

        var claim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            BlocksMined = originalBlocks,
            ToolRef = "IronPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(101, 51, 201)
        };

        var tx = VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Act
        var deserialized = VoxelTransactionHelper.DeserializeMinedBlocks(tx.Data["BlocksMined"]);

        // Assert
        Assert.Equal(2, deserialized.Count);
        Assert.Equal("stone", deserialized[0].BlockType);
        Assert.Equal(100, deserialized[0].Position.X);
        Assert.Equal("dirt", deserialized[1].BlockType);
        Assert.Equal(101, deserialized[1].Position.X);
    }

    [Fact]
    public void DeserializePlacedBlocks_RoundTrip_PreservesData()
    {
        // Arrange
        var originalBlocks = new List<PlacedBlock>
        {
            new PlacedBlock { BlockType = "cobblestone", Position = new Position3D(100, 50, 200) },
            new PlacedBlock { BlockType = "wood_plank", Position = new Position3D(101, 51, 201) }
        };

        var claim = new BuildingSessionClaim
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            BlocksPlaced = originalBlocks,
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(101, 51, 201)
        };

        var tx = VoxelTransactionHelper.CreateBuildingSessionClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Act
        var deserialized = VoxelTransactionHelper.DeserializePlacedBlocks(tx.Data["BlocksPlaced"]);

        // Assert
        Assert.Equal(2, deserialized.Count);
        Assert.Equal("cobblestone", deserialized[0].BlockType);
        Assert.Equal(100, deserialized[0].Position.X);
        Assert.Equal("wood_plank", deserialized[1].BlockType);
        Assert.Equal(101, deserialized[1].Position.X);
    }

    [Fact]
    public void CreateBuildingSessionClaimedTransaction_StoresMaterialsConsumed()
    {
        // Arrange
        var claim = new BuildingSessionClaim
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            BlocksPlaced = new List<PlacedBlock>
            {
                new PlacedBlock { BlockType = "stone", Position = new Position3D(100, 50, 200) }
            },
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(100, 50, 200),
            MaterialsConsumed = new Dictionary<string, int>
            {
                ["stone"] = 1
            }
        };

        // Act
        var tx = VoxelTransactionHelper.CreateBuildingSessionClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        Assert.Contains("MaterialsConsumed", tx.Data.Keys);
        Assert.Equal("stone:1", tx.Data["MaterialsConsumed"]);
    }

    [Fact]
    public void CreateInventorySnapshotTransaction_StoresAllInventoryTypes()
    {
        // Arrange
        var claim = new InventorySnapshotClaim
        {
            Timestamp = DateTime.UtcNow,
            Blocks = new Dictionary<string, int> { ["stone"] = 100, ["dirt"] = 50 },
            Tools = new Dictionary<string, float> { ["IronPickaxe"] = 0.85f, ["DiamondSword"] = 0.95f },
            BuildingMaterials = new Dictionary<string, int> { ["cobblestone"] = 25 }
        };

        // Act
        var tx = VoxelTransactionHelper.CreateInventorySnapshotTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        // Assert
        Assert.Equal(SagaTransactionType.InventorySnapshot, tx.Type);
        Assert.Contains("Blocks", tx.Data.Keys);
        Assert.Contains("Tools", tx.Data.Keys);
        Assert.Contains("BuildingMaterials", tx.Data.Keys);
        Assert.Equal("150", tx.Data["TotalBlockCount"]); // 100 + 50
        Assert.Equal("2", tx.Data["TotalToolCount"]);
    }
}
