namespace Ambient.SagaEngine.Domain.Rpg.Voxel;

/// <summary>
/// Position in 3D space (GPS coordinates for Ambient world).
/// </summary>
public class Position3D
{
    public double X { get; set; }       // LongitudeX
    public double Y { get; set; }       // Height
    public double Z { get; set; }       // LatitudeZ

    public Position3D() { }

    public Position3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public override string ToString() => $"{X:F4},{Y:F4},{Z:F4}";

    public static Position3D Parse(string str)
    {
        var parts = str.Split(',');
        if (parts.Length != 3)
            throw new ArgumentException("Invalid position format. Expected: X,Y,Z");

        return new Position3D(
            double.Parse(parts[0]),
            double.Parse(parts[1]),
            double.Parse(parts[2]));
    }
}

/// <summary>
/// Represents a single mined block in a mining session.
/// </summary>
public class MinedBlock
{
    public string BlockType { get; set; } = string.Empty;
    public Position3D Position { get; set; } = new();
    public DateTime MinedAt { get; set; }

    public override string ToString() => $"{BlockType}@{Position}";
}

/// <summary>
/// Represents a single placed block in a building session.
/// </summary>
public class PlacedBlock
{
    public string BlockType { get; set; } = string.Empty;
    public Position3D Position { get; set; } = new();
    public DateTime PlacedAt { get; set; }

    public override string ToString() => $"{BlockType}@{Position}";
}

/// <summary>
/// Location claim - player reporting their position.
/// Sent periodically (every 1-5 seconds) for movement validation.
/// </summary>
public class LocationClaim
{
    public DateTime Timestamp { get; set; }
    public Position3D Position { get; set; } = new();
    public Position3D? PreviousPosition { get; set; }  // For velocity calculation
    public DateTime? PreviousTimestamp { get; set; }
}

/// <summary>
/// Tool wear claim - player reporting tool condition delta.
/// Sent when tool condition changes significantly or periodically.
/// </summary>
public class ToolWearClaim
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string ToolRef { get; set; } = string.Empty;
    public float ConditionBefore { get; set; }
    public float ConditionAfter { get; set; }
    public int BlocksMinedWithTool { get; set; }  // For wear rate validation
    public string? DominantBlockType { get; set; }  // Most common block type mined (for hardness)
}

/// <summary>
/// Mining session claim - batch of blocks mined in a time window.
/// Sent every 1-5 seconds with all blocks mined in that period.
/// </summary>
public class MiningSessionClaim
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<MinedBlock> BlocksMined { get; set; } = new();
    public string ToolRef { get; set; } = string.Empty;
    public float ToolConditionBefore { get; set; }
    public float ToolConditionAfter { get; set; }
    public Position3D StartLocation { get; set; } = new();
    public Position3D EndLocation { get; set; } = new();

    public int BlockCount => BlocksMined.Count;
    public double DurationSeconds => (EndTime - StartTime).TotalSeconds;
    public double MiningRate => DurationSeconds > 0 ? BlockCount / DurationSeconds : 0;
}

/// <summary>
/// Building session claim - batch of blocks placed in a time window.
/// Sent every 1-5 seconds with all blocks placed in that period.
/// </summary>
public class BuildingSessionClaim
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<PlacedBlock> BlocksPlaced { get; set; } = new();
    public Position3D StartLocation { get; set; } = new();
    public Position3D EndLocation { get; set; } = new();

    // Building materials consumed (for inventory validation)
    public Dictionary<string, int> MaterialsConsumed { get; set; } = new();

    public int BlockCount => BlocksPlaced.Count;
    public double DurationSeconds => (EndTime - StartTime).TotalSeconds;
    public double BuildingRate => DurationSeconds > 0 ? BlockCount / DurationSeconds : 0;
}

/// <summary>
/// Inventory snapshot claim - full inventory state for validation baseline.
/// Sent periodically (every 5-10 minutes) to establish ground truth.
/// Allows server to detect inventory manipulation between snapshots.
/// </summary>
public class InventorySnapshotClaim
{
    public DateTime Timestamp { get; set; }
    public Dictionary<string, int> Blocks { get; set; } = new();  // BlockType -> Quantity
    public Dictionary<string, float> Tools { get; set; } = new();  // ToolRef -> Condition
    public Dictionary<string, int> BuildingMaterials { get; set; } = new();  // MaterialRef -> Quantity
    public int TotalBlockCount => Blocks.Values.Sum();
    public int TotalToolCount => Tools.Count;
}

/// <summary>
/// Result of a claim validation attempt.
/// </summary>
public class ClaimValidationResult
{
    public bool IsValid { get; set; }
    public string Reason { get; set; } = string.Empty;
    public float CheatConfidence { get; set; }  // 0.0 to 1.0 (how confident we are this is cheating)
    public List<string> ValidationWarnings { get; set; } = new();

    public static ClaimValidationResult Success() =>
        new() { IsValid = true, Reason = "Valid", CheatConfidence = 0.0f };

    public static ClaimValidationResult Failure(string reason, float confidence = 0.5f) =>
        new() { IsValid = false, Reason = reason, CheatConfidence = confidence };

    public static ClaimValidationResult Warning(string warning) =>
        new()
        {
            IsValid = true,
            Reason = "Valid with warnings",
            CheatConfidence = 0.2f,
            ValidationWarnings = new List<string> { warning }
        };
}
