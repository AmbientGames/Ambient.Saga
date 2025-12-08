using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain.Rpg.Voxel;

namespace Ambient.Saga.Engine.Tests.Rpg.Voxel;

/// <summary>
/// Unit tests for SagaStateMachine handling of voxel transactions.
/// Verifies that voxel transactions can be replayed without errors.
/// </summary>
public class SagaStateMachineVoxelTests
{
    private readonly SagaArc _testSagaArc;
    private readonly List<SagaTrigger> _testSagaTriggers;
    private readonly World _testWorld;
    private readonly SagaStateMachine _stateMachine;

    private readonly Guid _testSagaInstanceId = Guid.NewGuid();
    private readonly Guid _testAvatarId = Guid.NewGuid();

    public SagaStateMachineVoxelTests()
    {
        // Create minimal test Saga template
        _testSagaArc = new SagaArc
        {
            RefName = "VoxelTestSaga",
            DisplayName = "Voxel Test Saga",
            LatitudeZ = 35.0,
            LongitudeX = 139.0
        };

        // Create test triggers
        _testSagaTriggers = new List<SagaTrigger>
        {
            new SagaTrigger
            {
                RefName = "mining_area",
                DisplayName = "Mining Area",
                EnterRadius = 50.0f
            }
        };

        // Create minimal test world
        _testWorld = new World
        {
            CharactersLookup = new Dictionary<string, Character>()
        };

        _stateMachine = new SagaStateMachine(_testSagaArc, _testSagaTriggers, _testWorld);
    }

    [Fact]
    public void ReplayToNow_LocationClaimedTransaction_CompletesWithoutError()
    {
        // Arrange
        var instance = CreateSagaInstance();
        var claim = new LocationClaim
        {
            Timestamp = DateTime.UtcNow,
            Position = new Position3D(100, 50, 200)
        };

        var transaction = VoxelTransactionHelper.CreateLocationClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        instance.AddTransaction(transaction);

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("VoxelTestSaga", state.SagaRef);
    }

    [Fact]
    public void ReplayToNow_ToolWearClaimedTransaction_CompletesWithoutError()
    {
        // Arrange
        var instance = CreateSagaInstance();
        var claim = new ToolWearClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            ToolRef = "IronPickaxe",
            ConditionBefore = 0.85f,
            ConditionAfter = 0.82f,
            BlocksMinedWithTool = 50
        };

        var transaction = VoxelTransactionHelper.CreateToolWearClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        instance.AddTransaction(transaction);

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("VoxelTestSaga", state.SagaRef);
    }

    [Fact]
    public void ReplayToNow_MiningSessionClaimedTransaction_CompletesWithoutError()
    {
        // Arrange
        var instance = CreateSagaInstance();
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

        var transaction = VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        instance.AddTransaction(transaction);

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("VoxelTestSaga", state.SagaRef);
    }

    [Fact]
    public void ReplayToNow_BuildingSessionClaimedTransaction_CompletesWithoutError()
    {
        // Arrange
        var instance = CreateSagaInstance();
        var claim = new BuildingSessionClaim
        {
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow,
            BlocksPlaced = new List<PlacedBlock>
            {
                new PlacedBlock { BlockType = "cobblestone", Position = new Position3D(100, 50, 200) },
                new PlacedBlock { BlockType = "wood_plank", Position = new Position3D(101, 50, 200) }
            },
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(101, 50, 200),
            MaterialsConsumed = new Dictionary<string, int>
            {
                ["cobblestone"] = 1,
                ["wood_plank"] = 1
            }
        };

        var transaction = VoxelTransactionHelper.CreateBuildingSessionClaimedTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        instance.AddTransaction(transaction);

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("VoxelTestSaga", state.SagaRef);
    }

    [Fact]
    public void ReplayToNow_InventorySnapshotTransaction_CompletesWithoutError()
    {
        // Arrange
        var instance = CreateSagaInstance();
        var claim = new InventorySnapshotClaim
        {
            Timestamp = DateTime.UtcNow,
            Blocks = new Dictionary<string, int> { ["stone"] = 100, ["dirt"] = 50 },
            Tools = new Dictionary<string, float> { ["IronPickaxe"] = 0.85f },
            BuildingMaterials = new Dictionary<string, int> { ["cobblestone"] = 25 }
        };

        var transaction = VoxelTransactionHelper.CreateInventorySnapshotTransaction(
            _testAvatarId.ToString(),
            claim,
            _testSagaInstanceId);

        instance.AddTransaction(transaction);

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("VoxelTestSaga", state.SagaRef);
    }

    [Fact]
    public void ReplayToNow_MixedVoxelAndRpgTransactions_MaintainsStateConsistency()
    {
        // Arrange - Mix voxel and RPG transactions to ensure no interference
        var instance = CreateSagaInstance();

        // Add a saga discovered transaction (RPG)
        instance.AddTransaction(new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.SagaDiscovered,
            AvatarId = _testAvatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow.AddMinutes(-10),
            ServerTimestamp = DateTime.UtcNow.AddMinutes(-10),
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new Dictionary<string, string>
            {
                ["SagaArcRef"] = "VoxelTestSaga",
                ["DiscoveryRadius"] = "50.0"
            }
        });

        // Add mining transaction (voxel)
        var miningClaim = new MiningSessionClaim
        {
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow,
            BlocksMined = new List<MinedBlock>
            {
                new MinedBlock { BlockType = "stone", Position = new Position3D(100, 50, 200) }
            },
            ToolRef = "IronPickaxe",
            StartLocation = new Position3D(100, 50, 200),
            EndLocation = new Position3D(100, 50, 200)
        };

        instance.AddTransaction(VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
            _testAvatarId.ToString(),
            miningClaim,
            _testSagaInstanceId));

        // Add location transaction (voxel)
        var locationClaim = new LocationClaim
        {
            Timestamp = DateTime.UtcNow,
            Position = new Position3D(105, 50, 205)
        };

        instance.AddTransaction(VoxelTransactionHelper.CreateLocationClaimedTransaction(
            _testAvatarId.ToString(),
            locationClaim,
            _testSagaInstanceId));

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("VoxelTestSaga", state.SagaRef);
        Assert.Equal(SagaStatus.Active, state.Status); // RPG transaction should have been applied
    }

    [Fact]
    public void ReplayToNow_MultipleVoxelTransactions_MaintainsTransactionOrder()
    {
        // Arrange - Add transactions in chronological order
        var instance = CreateSagaInstance();
        var baseTime = DateTime.UtcNow.AddMinutes(-10);

        // Location claim 1
        instance.AddTransaction(VoxelTransactionHelper.CreateLocationClaimedTransaction(
            _testAvatarId.ToString(),
            new LocationClaim
            {
                Timestamp = baseTime,
                Position = new Position3D(100, 50, 200)
            },
            _testSagaInstanceId));

        // Mining claim
        instance.AddTransaction(VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
            _testAvatarId.ToString(),
            new MiningSessionClaim
            {
                StartTime = baseTime.AddSeconds(10),
                EndTime = baseTime.AddSeconds(20),
                BlocksMined = new List<MinedBlock>
                {
                    new MinedBlock { BlockType = "stone", Position = new Position3D(100, 50, 200) }
                },
                ToolRef = "IronPickaxe",
                StartLocation = new Position3D(100, 50, 200),
                EndLocation = new Position3D(100, 50, 200)
            },
            _testSagaInstanceId));

        // Tool wear claim
        instance.AddTransaction(VoxelTransactionHelper.CreateToolWearClaimedTransaction(
            _testAvatarId.ToString(),
            new ToolWearClaim
            {
                StartTime = baseTime.AddSeconds(10),
                EndTime = baseTime.AddSeconds(20),
                ToolRef = "IronPickaxe",
                ConditionBefore = 1.0f,
                ConditionAfter = 0.99f,
                BlocksMinedWithTool = 1
            },
            _testSagaInstanceId));

        // Location claim 2
        instance.AddTransaction(VoxelTransactionHelper.CreateLocationClaimedTransaction(
            _testAvatarId.ToString(),
            new LocationClaim
            {
                Timestamp = baseTime.AddSeconds(30),
                Position = new Position3D(105, 50, 200)
            },
            _testSagaInstanceId));

        // Inventory snapshot
        instance.AddTransaction(VoxelTransactionHelper.CreateInventorySnapshotTransaction(
            _testAvatarId.ToString(),
            new InventorySnapshotClaim
            {
                Timestamp = baseTime.AddSeconds(40),
                Blocks = new Dictionary<string, int> { ["stone"] = 1 },
                Tools = new Dictionary<string, float> { ["IronPickaxe"] = 0.99f }
            },
            _testSagaInstanceId));

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("VoxelTestSaga", state.SagaRef);
        Assert.Equal(5, instance.Transactions.Count); // All transactions preserved
    }

    [Fact]
    public void ReplayToNow_VoxelTransactionsWithDifferentAvatars_HandlesMultipleAvatars()
    {
        // Arrange - Multiple avatars interacting in same saga
        var instance = CreateSagaInstance();
        var avatar1 = Guid.NewGuid();
        var avatar2 = Guid.NewGuid();

        // Avatar 1 mines
        instance.AddTransaction(VoxelTransactionHelper.CreateMiningSessionClaimedTransaction(
            avatar1.ToString(),
            new MiningSessionClaim
            {
                StartTime = DateTime.UtcNow.AddMinutes(-5),
                EndTime = DateTime.UtcNow,
                BlocksMined = new List<MinedBlock>
                {
                    new MinedBlock { BlockType = "stone", Position = new Position3D(100, 50, 200) }
                },
                ToolRef = "IronPickaxe",
                StartLocation = new Position3D(100, 50, 200),
                EndLocation = new Position3D(100, 50, 200)
            },
            _testSagaInstanceId));

        // Avatar 2 builds
        instance.AddTransaction(VoxelTransactionHelper.CreateBuildingSessionClaimedTransaction(
            avatar2.ToString(),
            new BuildingSessionClaim
            {
                StartTime = DateTime.UtcNow.AddMinutes(-2),
                EndTime = DateTime.UtcNow,
                BlocksPlaced = new List<PlacedBlock>
                {
                    new PlacedBlock { BlockType = "cobblestone", Position = new Position3D(105, 50, 200) }
                },
                StartLocation = new Position3D(105, 50, 200),
                EndLocation = new Position3D(105, 50, 200)
            },
            _testSagaInstanceId));

        // Act
        var state = _stateMachine.ReplayToNow(instance);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(2, instance.Transactions.Count);
        Assert.Contains(instance.Transactions, t => t.AvatarId == avatar1.ToString());
        Assert.Contains(instance.Transactions, t => t.AvatarId == avatar2.ToString());
    }

    // ===== HELPER METHODS =====

    private SagaInstance CreateSagaInstance()
    {
        return new SagaInstance
        {
            InstanceId = _testSagaInstanceId,
            SagaRef = "VoxelTestSaga",
            OwnerAvatarId = _testAvatarId,
            InstanceType = SagaInstanceType.SinglePlayer,
            Transactions = new List<SagaTransaction>()
        };
    }
}
