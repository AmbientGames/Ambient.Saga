using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain.Rpg.Voxel;

namespace Ambient.Saga.Engine.Tests.Rpg.Voxel;

/// <summary>
/// Unit tests for VoxelAntiCheatAnalyzer retrospective detection algorithms.
/// </summary>
public class VoxelAntiCheatAnalyzerTests
{
    private readonly Guid _testAvatarId = Guid.NewGuid();
    private readonly Guid _testSagaInstanceId = Guid.NewGuid();

    [Fact]
    public void AnalyzePlayer_NoTransactions_ReturnsEmptyReport()
    {
        // Arrange
        var sagaInstances = new List<SagaInstance>();

        // Act
        var report = VoxelAntiCheatAnalyzer.AnalyzePlayer(
            _testAvatarId,
            sagaInstances,
            TimeSpan.FromHours(24));

        // Assert
        Assert.Equal(_testAvatarId, report.AvatarId);
        Assert.Empty(report.Flags);
        Assert.False(report.IsSuspicious);
    }

    [Fact]
    public void AnalyzePlayer_LegitimatePlay_PassesAnalysis()
    {
        // Arrange - Normal mining activity
        var instance = CreateSagaInstance();
        instance.Transactions.Add(CreateMiningTransaction(
            miningRate: 2.0f, // Normal speed
            rareOrePercentage: 0.05f, // Expected 5%
            timestamp: DateTime.UtcNow.AddHours(-1)));

        instance.Transactions.Add(CreateMiningTransaction(
            miningRate: 2.5f,
            rareOrePercentage: 0.04f,
            timestamp: DateTime.UtcNow));

        var sagaInstances = new List<SagaInstance> { instance };

        // Act
        var report = VoxelAntiCheatAnalyzer.AnalyzePlayer(
            _testAvatarId,
            sagaInstances,
            TimeSpan.FromHours(24));

        // Assert
        Assert.Empty(report.Flags);
        Assert.False(report.IsSuspicious);
    }

    [Fact]
    public void AnalyzePlayer_SpeedHack_DetectsFlag()
    {
        // Arrange - Mining too fast consistently
        var instance = CreateSagaInstance();

        // Add 10 transactions all near theoretical max
        for (var i = 0; i < 10; i++)
        {
            instance.Transactions.Add(CreateMiningTransaction(
                miningRate: 2.8f, // 93% of max (3.0)
                rareOrePercentage: 0.05f,
                timestamp: DateTime.UtcNow.AddMinutes(-i)));
        }

        var sagaInstances = new List<SagaInstance> { instance };

        // Act
        var report = VoxelAntiCheatAnalyzer.AnalyzePlayer(
            _testAvatarId,
            sagaInstances,
            TimeSpan.FromHours(1));

        // Assert
        Assert.NotEmpty(report.Flags);
        Assert.Contains(report.Flags, f => f.Type == CheatType.SpeedHack);
        Assert.True(report.IsSuspicious);
    }

    [Fact]
    public void AnalyzePlayer_XRayHack_DetectsFlag()
    {
        // Arrange - Finding rare ores at abnormal rate
        var instance = CreateSagaInstance();
        instance.Transactions.Add(CreateMiningTransaction(
            miningRate: 2.0f,
            rareOrePercentage: 0.50f, // 50% rare ore (10x expected)
            timestamp: DateTime.UtcNow,
            blockCount: 100)); // Need enough blocks for statistical significance

        var sagaInstances = new List<SagaInstance> { instance };

        // Act
        var report = VoxelAntiCheatAnalyzer.AnalyzePlayer(
            _testAvatarId,
            sagaInstances,
            TimeSpan.FromHours(1));

        // Assert
        Assert.NotEmpty(report.Flags);
        Assert.Contains(report.Flags, f => f.Type == CheatType.XRayHack);
        Assert.True(report.IsSuspicious);
    }

    [Fact]
    public void AnalyzePlayer_Teleportation_DetectsFlag()
    {
        // Arrange - Impossible movement speeds
        var instance = CreateSagaInstance();

        // Create location claims showing teleportation
        var prevTime = DateTime.UtcNow.AddSeconds(-10);
        var currentTime = DateTime.UtcNow;

        instance.Transactions.Add(CreateLocationTransaction(100, 50, 200, prevTime));
        instance.Transactions.Add(CreateLocationTransaction(1000, 50, 200, currentTime)); // Moved 900m in 10 seconds = 90 m/s

        var sagaInstances = new List<SagaInstance> { instance };

        // Act
        var report = VoxelAntiCheatAnalyzer.AnalyzePlayer(
            _testAvatarId,
            sagaInstances,
            TimeSpan.FromHours(1));

        // Assert
        Assert.NotEmpty(report.Flags);
        Assert.Contains(report.Flags, f => f.Type == CheatType.Teleportation);
        Assert.True(report.IsSuspicious);
    }

    [Fact]
    public void AnalyzePlayer_DurabilityHack_DetectsFlag()
    {
        // Arrange - Tool wearing out way too slowly
        var instance = CreateSagaInstance();

        // 1000 blocks mined with only 0.001 total wear (should be ~0.2 for iron pickaxe)
        instance.Transactions.Add(CreateToolWearTransaction(
            toolRef: "IronPickaxe",
            wearDelta: 0.001f,
            blocksMined: 1000));

        var sagaInstances = new List<SagaInstance> { instance };

        // Act
        var report = VoxelAntiCheatAnalyzer.AnalyzePlayer(
            _testAvatarId,
            sagaInstances,
            TimeSpan.FromHours(1));

        // Assert
        Assert.NotEmpty(report.Flags);
        Assert.Contains(report.Flags, f => f.Type == CheatType.DurabilityHack);
        Assert.True(report.IsSuspicious);
    }

    [Fact]
    public void CalculateZScore_StandardDeviation_CalculatesCorrectly()
    {
        // Arrange
        var value = 15.0;
        var mean = 10.0;
        var stdDev = 2.0;

        // Act
        var zScore = VoxelAntiCheatAnalyzer.CalculateZScore(value, mean, stdDev);

        // Assert
        Assert.Equal(2.5, zScore, precision: 2);
    }

    [Fact]
    public void CalculateStdDev_SampleData_CalculatesCorrectly()
    {
        // Arrange - Simple dataset: 2, 4, 6, 8, 10 (mean = 6, variance = 8, stddev = ~2.83)
        var values = new[] { 2.0, 4.0, 6.0, 8.0, 10.0 };

        // Act
        var stdDev = VoxelAntiCheatAnalyzer.CalculateStdDev(values);

        // Assert
        Assert.Equal(2.83, stdDev, precision: 2);
    }

    [Fact]
    public void GetCommunityStats_MultiplePlayers_CalculatesAverages()
    {
        // Arrange
        var avatarIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sagaInstances = new List<SagaInstance>();

        foreach (var avatarId in avatarIds)
        {
            var instance = new SagaInstance
            {
                InstanceId = Guid.NewGuid(),
                SagaRef = "TestSaga",
                OwnerAvatarId = avatarId,
                Transactions = new List<SagaTransaction>
                {
                    CreateMiningTransaction(
                        miningRate: 2.0f,
                        rareOrePercentage: 0.05f,
                        timestamp: DateTime.UtcNow,
                        avatarId: avatarId.ToString())
                }
            };
            sagaInstances.Add(instance);
        }

        // Act
        var stats = VoxelAntiCheatAnalyzer.GetCommunityStats(
            avatarIds,
            sagaInstances,
            TimeSpan.FromHours(24));

        // Assert
        Assert.Equal(3, stats.TotalPlayers);
        Assert.Equal(2.0, stats.AverageMiningRate, precision: 1);
        Assert.Equal(0.05, stats.AverageRareOreRate, precision: 2);
    }

    [Fact]
    public void AnalyzeAllPlayers_OnlySuspicious_ReturnsFilteredList()
    {
        // Arrange
        var legitimatePlayer = Guid.NewGuid();
        var cheatingPlayer = Guid.NewGuid();

        var legitimateInstance = new SagaInstance
        {
            InstanceId = Guid.NewGuid(),
            SagaRef = "TestSaga",
            OwnerAvatarId = legitimatePlayer,
            Transactions = new List<SagaTransaction>
            {
                CreateMiningTransaction(2.0f, 0.05f, DateTime.UtcNow, avatarId: legitimatePlayer.ToString())
            }
        };

        var cheatingInstance = new SagaInstance
        {
            InstanceId = Guid.NewGuid(),
            SagaRef = "TestSaga",
            OwnerAvatarId = cheatingPlayer,
            Transactions = new List<SagaTransaction>
            {
                CreateMiningTransaction(2.0f, 0.50f, DateTime.UtcNow, blockCount: 100, avatarId: cheatingPlayer.ToString()) // X-ray hack
            }
        };

        var sagaInstances = new List<SagaInstance> { legitimateInstance, cheatingInstance };
        var avatarIds = new List<Guid> { legitimatePlayer, cheatingPlayer };

        // Act
        var reports = VoxelAntiCheatAnalyzer.AnalyzeAllPlayers(
            avatarIds,
            sagaInstances,
            TimeSpan.FromHours(24));

        // Assert
        Assert.Single(reports); // Only cheating player returned
        Assert.Equal(cheatingPlayer, reports[0].AvatarId);
        Assert.True(reports[0].IsSuspicious);
    }

    // ===== HELPER METHODS =====

    private SagaInstance CreateSagaInstance()
    {
        return new SagaInstance
        {
            InstanceId = _testSagaInstanceId,
            SagaRef = "TestSaga",
            OwnerAvatarId = _testAvatarId,
            Transactions = new List<SagaTransaction>()
        };
    }

    private SagaTransaction CreateMiningTransaction(
        float miningRate,
        float rareOrePercentage,
        DateTime timestamp,
        int blockCount = 10,
        string? avatarId = null)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.MiningSessionClaimed,
            AvatarId = avatarId ?? _testAvatarId.ToString(),
            LocalTimestamp = timestamp,
            Status = TransactionStatus.Committed,
            Data = new Dictionary<string, string>
            {
                ["MiningRate"] = miningRate.ToString(),
                ["RareOrePercentage"] = rareOrePercentage.ToString(),
                ["BlockCount"] = blockCount.ToString()
            }
        };
    }

    private SagaTransaction CreateLocationTransaction(double x, double y, double z, DateTime timestamp)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.LocationClaimed,
            AvatarId = _testAvatarId.ToString(),
            LocalTimestamp = timestamp,
            ServerTimestamp = timestamp,  // Set ServerTimestamp for GetCanonicalTimestamp()
            Status = TransactionStatus.Committed,
            Data = new Dictionary<string, string>
            {
                ["PositionX"] = x.ToString(),
                ["PositionY"] = y.ToString(),
                ["PositionZ"] = z.ToString()
            }
        };
    }

    private SagaTransaction CreateToolWearTransaction(string toolRef, float wearDelta, int blocksMined)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ToolWearClaimed,
            AvatarId = _testAvatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Status = TransactionStatus.Committed,
            Data = new Dictionary<string, string>
            {
                ["ToolRef"] = toolRef,
                ["WearDelta"] = wearDelta.ToString(),
                ["BlocksMinedWithTool"] = blocksMined.ToString()
            }
        };
    }
}
