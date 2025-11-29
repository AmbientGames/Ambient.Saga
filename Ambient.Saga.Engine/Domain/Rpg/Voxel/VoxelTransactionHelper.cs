using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using System.Text.Json;

namespace Ambient.Saga.Engine.Domain.Rpg.Voxel;

/// <summary>
/// Helper service for creating voxel-related Saga transactions.
/// Ensures mining/building actions are auditable and replayable with complete history.
/// </summary>
public static class VoxelTransactionHelper
{
    /// <summary>
    /// Creates a transaction for a location claim (player position update).
    /// Used for movement validation and teleportation/fly hack detection.
    /// </summary>
    public static SagaTransaction CreateLocationClaimedTransaction(
        string avatarId,
        LocationClaim claim,
        Guid sagaInstanceId)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.LocationClaimed,
            AvatarId = avatarId,
            LocalTimestamp = claim.Timestamp,
            Data = new Dictionary<string, string>
            {
                ["SagaInstanceId"] = sagaInstanceId.ToString(),
                ["Timestamp"] = claim.Timestamp.ToString("O"),
                ["PositionX"] = claim.Position.X.ToString("F4"),
                ["PositionY"] = claim.Position.Y.ToString("F4"),
                ["PositionZ"] = claim.Position.Z.ToString("F4")
            }
        };

        // Include previous position if available (for velocity calculation)
        if (claim.PreviousPosition != null && claim.PreviousTimestamp.HasValue)
        {
            transaction.Data["PreviousPositionX"] = claim.PreviousPosition.X.ToString("F4");
            transaction.Data["PreviousPositionY"] = claim.PreviousPosition.Y.ToString("F4");
            transaction.Data["PreviousPositionZ"] = claim.PreviousPosition.Z.ToString("F4");
            transaction.Data["PreviousTimestamp"] = claim.PreviousTimestamp.Value.ToString("O");

            // Calculate and store velocity for analytics
            var timeDelta = (claim.Timestamp - claim.PreviousTimestamp.Value).TotalSeconds;
            if (timeDelta > 0)
            {
                var distance = VoxelGameConstants.CalculateDistance(
                    claim.PreviousPosition.X, claim.PreviousPosition.Y, claim.PreviousPosition.Z,
                    claim.Position.X, claim.Position.Y, claim.Position.Z);
                var velocity = distance / timeDelta;
                transaction.Data["Velocity"] = velocity.ToString("F2");
            }
        }

        return transaction;
    }

    /// <summary>
    /// Creates a transaction for a tool wear claim.
    /// Used for durability hack detection and tool usage analytics.
    /// </summary>
    public static SagaTransaction CreateToolWearClaimedTransaction(
        string avatarId,
        ToolWearClaim claim,
        Guid sagaInstanceId)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.ToolWearClaimed,
            AvatarId = avatarId,
            LocalTimestamp = claim.StartTime,
            Data = new Dictionary<string, string>
            {
                ["SagaInstanceId"] = sagaInstanceId.ToString(),
                ["StartTime"] = claim.StartTime.ToString("O"),
                ["EndTime"] = claim.EndTime.ToString("O"),
                ["ToolRef"] = claim.ToolRef,
                ["ConditionBefore"] = claim.ConditionBefore.ToString("F4"),
                ["ConditionAfter"] = claim.ConditionAfter.ToString("F4"),
                ["WearDelta"] = (claim.ConditionBefore - claim.ConditionAfter).ToString("F4"),
                ["BlocksMinedWithTool"] = claim.BlocksMinedWithTool.ToString()
            }
        };

        if (!string.IsNullOrEmpty(claim.DominantBlockType))
        {
            transaction.Data["DominantBlockType"] = claim.DominantBlockType;
        }

        // Calculate wear rate for analytics
        if (claim.BlocksMinedWithTool > 0)
        {
            var wearRate = (claim.ConditionBefore - claim.ConditionAfter) / claim.BlocksMinedWithTool;
            transaction.Data["WearRatePerBlock"] = wearRate.ToString("F6");
        }

        return transaction;
    }

    /// <summary>
    /// Creates a transaction for a mining session claim.
    /// Used for speed hack, X-ray, and reachability validation.
    /// </summary>
    public static SagaTransaction CreateMiningSessionClaimedTransaction(
        string avatarId,
        MiningSessionClaim claim,
        Guid sagaInstanceId)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.MiningSessionClaimed,
            AvatarId = avatarId,
            LocalTimestamp = claim.StartTime,
            Data = new Dictionary<string, string>
            {
                ["SagaInstanceId"] = sagaInstanceId.ToString(),
                ["StartTime"] = claim.StartTime.ToString("O"),
                ["EndTime"] = claim.EndTime.ToString("O"),
                ["DurationSeconds"] = claim.DurationSeconds.ToString("F2"),
                ["BlockCount"] = claim.BlockCount.ToString(),
                ["MiningRate"] = claim.MiningRate.ToString("F2"),
                ["ToolRef"] = claim.ToolRef,
                ["ToolConditionBefore"] = claim.ToolConditionBefore.ToString("F4"),
                ["ToolConditionAfter"] = claim.ToolConditionAfter.ToString("F4"),
                ["StartLocationX"] = claim.StartLocation.X.ToString("F4"),
                ["StartLocationY"] = claim.StartLocation.Y.ToString("F4"),
                ["StartLocationZ"] = claim.StartLocation.Z.ToString("F4"),
                ["EndLocationX"] = claim.EndLocation.X.ToString("F4"),
                ["EndLocationY"] = claim.EndLocation.Y.ToString("F4"),
                ["EndLocationZ"] = claim.EndLocation.Z.ToString("F4")
            }
        };

        // Serialize blocks mined (compact JSON format)
        if (claim.BlocksMined.Count > 0)
        {
            var blocksJson = SerializeMinedBlocks(claim.BlocksMined);
            transaction.Data["BlocksMined"] = blocksJson;

            // Track block type distribution for X-ray detection
            var blockTypeCount = claim.BlocksMined
                .GroupBy(b => b.BlockType)
                .ToDictionary(g => g.Key, g => g.Count());
            transaction.Data["BlockTypeDistribution"] = string.Join(",",
                blockTypeCount.Select(kvp => $"{kvp.Key}:{kvp.Value}"));

            // Flag rare ore percentage for analytics
            var rareOreCount = claim.BlocksMined.Count(b => VoxelGameConstants.IsRareOre(b.BlockType));
            var rareOrePercentage = (float)rareOreCount / claim.BlockCount;
            transaction.Data["RareOrePercentage"] = rareOrePercentage.ToString("F4");
        }

        return transaction;
    }

    /// <summary>
    /// Creates a transaction for a building session claim.
    /// Used for material availability and placement validation.
    /// </summary>
    public static SagaTransaction CreateBuildingSessionClaimedTransaction(
        string avatarId,
        BuildingSessionClaim claim,
        Guid sagaInstanceId)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.BuildingSessionClaimed,
            AvatarId = avatarId,
            LocalTimestamp = claim.StartTime,
            Data = new Dictionary<string, string>
            {
                ["SagaInstanceId"] = sagaInstanceId.ToString(),
                ["StartTime"] = claim.StartTime.ToString("O"),
                ["EndTime"] = claim.EndTime.ToString("O"),
                ["DurationSeconds"] = claim.DurationSeconds.ToString("F2"),
                ["BlockCount"] = claim.BlockCount.ToString(),
                ["BuildingRate"] = claim.BuildingRate.ToString("F2"),
                ["StartLocationX"] = claim.StartLocation.X.ToString("F4"),
                ["StartLocationY"] = claim.StartLocation.Y.ToString("F4"),
                ["StartLocationZ"] = claim.StartLocation.Z.ToString("F4"),
                ["EndLocationX"] = claim.EndLocation.X.ToString("F4"),
                ["EndLocationY"] = claim.EndLocation.Y.ToString("F4"),
                ["EndLocationZ"] = claim.EndLocation.Z.ToString("F4")
            }
        };

        // Serialize blocks placed (compact JSON format)
        if (claim.BlocksPlaced.Count > 0)
        {
            var blocksJson = SerializePlacedBlocks(claim.BlocksPlaced);
            transaction.Data["BlocksPlaced"] = blocksJson;

            // Track block type distribution
            var blockTypeCount = claim.BlocksPlaced
                .GroupBy(b => b.BlockType)
                .ToDictionary(g => g.Key, g => g.Count());
            transaction.Data["BlockTypeDistribution"] = string.Join(",",
                blockTypeCount.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        }

        // Serialize materials consumed (for inventory validation)
        if (claim.MaterialsConsumed.Count > 0)
        {
            transaction.Data["MaterialsConsumed"] = string.Join(",",
                claim.MaterialsConsumed.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        }

        return transaction;
    }

    /// <summary>
    /// Creates a transaction for an inventory snapshot claim.
    /// Used as validation baseline for detecting inventory manipulation.
    /// </summary>
    public static SagaTransaction CreateInventorySnapshotTransaction(
        string avatarId,
        InventorySnapshotClaim claim,
        Guid sagaInstanceId)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.InventorySnapshot,
            AvatarId = avatarId,
            LocalTimestamp = claim.Timestamp,
            Data = new Dictionary<string, string>
            {
                ["SagaInstanceId"] = sagaInstanceId.ToString(),
                ["Timestamp"] = claim.Timestamp.ToString("O"),
                ["TotalBlockCount"] = claim.TotalBlockCount.ToString(),
                ["TotalToolCount"] = claim.TotalToolCount.ToString()
            }
        };

        // Serialize blocks inventory
        if (claim.Blocks.Count > 0)
        {
            transaction.Data["Blocks"] = string.Join(",",
                claim.Blocks.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        }

        // Serialize tools inventory (with condition)
        if (claim.Tools.Count > 0)
        {
            transaction.Data["Tools"] = string.Join(",",
                claim.Tools.Select(kvp => $"{kvp.Key}:{kvp.Value:F4}"));
        }

        // Serialize building materials inventory
        if (claim.BuildingMaterials.Count > 0)
        {
            transaction.Data["BuildingMaterials"] = string.Join(",",
                claim.BuildingMaterials.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        }

        return transaction;
    }


    // ===== SERIALIZATION HELPERS =====

    /// <summary>
    /// Serialize mined blocks to compact JSON format.
    /// Format: [{"T":"Stone","X":100,"Y":50,"Z":200},...]
    /// </summary>
    private static string SerializeMinedBlocks(List<MinedBlock> blocks)
    {
        // Compact format to save space
        var compactBlocks = blocks.Select(b => new
        {
            T = b.BlockType,
            b.Position.X,
            b.Position.Y,
            b.Position.Z
        }).ToList();

        return JsonSerializer.Serialize(compactBlocks);
    }

    /// <summary>
    /// Deserialize mined blocks from compact JSON format.
    /// </summary>
    public static List<MinedBlock> DeserializeMinedBlocks(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new List<MinedBlock>();

        try
        {
            // Define a simple class for deserialization to avoid JsonElement issues
            var compactBlocks = JsonSerializer.Deserialize<List<CompactBlock>>(json);
            if (compactBlocks == null)
                return new List<MinedBlock>();

            return compactBlocks.Select(b => new MinedBlock
            {
                BlockType = b.T ?? "",
                Position = new Position3D(b.X, b.Y, b.Z)
            }).ToList();
        }
        catch
        {
            return new List<MinedBlock>();
        }
    }

    /// <summary>
    /// Compact block representation for JSON serialization.
    /// </summary>
    private class CompactBlock
    {
        public string? T { get; set; } // BlockType
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    /// <summary>
    /// Serialize placed blocks to compact JSON format.
    /// </summary>
    private static string SerializePlacedBlocks(List<PlacedBlock> blocks)
    {
        // Compact format to save space
        var compactBlocks = blocks.Select(b => new
        {
            T = b.BlockType,
            b.Position.X,
            b.Position.Y,
            b.Position.Z
        }).ToList();

        return JsonSerializer.Serialize(compactBlocks);
    }

    /// <summary>
    /// Deserialize placed blocks from compact JSON format.
    /// </summary>
    public static List<PlacedBlock> DeserializePlacedBlocks(string json)
    {
        if (string.IsNullOrEmpty(json))
            return new List<PlacedBlock>();

        try
        {
            var compactBlocks = JsonSerializer.Deserialize<List<CompactBlock>>(json);
            if (compactBlocks == null)
                return new List<PlacedBlock>();

            return compactBlocks.Select(b => new PlacedBlock
            {
                BlockType = b.T ?? "",
                Position = new Position3D(b.X, b.Y, b.Z)
            }).ToList();
        }
        catch
        {
            return new List<PlacedBlock>();
        }
    }
}
