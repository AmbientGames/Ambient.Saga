namespace Ambient.Saga.Engine.Domain.Rpg.Voxel;

/// <summary>
/// Game balance constants for voxel mining/building validation.
/// These constants define plausible limits for player actions to detect cheating.
/// Adjust these values based on actual gameplay testing and balance requirements.
/// </summary>
public static class VoxelGameConstants
{
    // ===== MINING CONSTANTS =====

    /// <summary>
    /// Maximum plausible mining rate in blocks per second.
    /// This is the theoretical max for a player with best tools and optimal conditions.
    /// Exceeding this triggers speed hack detection.
    /// </summary>
    public const float MAX_MINING_RATE_BLOCKS_PER_SECOND = 3.0f;

    /// <summary>
    /// Maximum distance a player can mine from (in meters).
    /// Blocks further than this are unreachable and trigger cheat detection.
    /// </summary>
    public const float MAX_MINING_REACH_METERS = 5.0f;

    /// <summary>
    /// Tolerance for tool wear validation (percentage).
    /// Actual wear can deviate from expected by this amount without triggering alerts.
    /// Accounts for rounding errors and minor variations in block hardness.
    /// </summary>
    public const float TOOL_WEAR_TOLERANCE = 0.10f; // 10% tolerance


    // ===== MOVEMENT CONSTANTS =====

    /// <summary>
    /// Maximum plausible horizontal movement speed in meters per second.
    /// This is sprinting speed. Exceeding this triggers fly/speed hack detection.
    /// </summary>
    public const float MAX_MOVEMENT_SPEED_METERS_PER_SECOND = 8.0f;

    /// <summary>
    /// Maximum plausible vertical movement speed in meters per second.
    /// This is terminal velocity when falling. Exceeding this triggers fly hack detection.
    /// </summary>
    public const float MAX_VERTICAL_SPEED_METERS_PER_SECOND = 20.0f;

    /// <summary>
    /// Maximum plausible jump height in meters.
    /// Players can't jump higher than this without fly hack.
    /// </summary>
    public const float MAX_JUMP_HEIGHT_METERS = 2.0f;


    // ===== INVENTORY CONSTANTS =====

    /// <summary>
    /// Maximum total blocks a player can carry in inventory.
    /// From CLAUDE.md: 512 max blocks per player.
    /// </summary>
    public const int MAX_INVENTORY_CAPACITY_BLOCKS = 512;

    /// <summary>
    /// Maximum number of tools a player can carry.
    /// From CLAUDE.md: 16 tools per player.
    /// </summary>
    public const int MAX_INVENTORY_CAPACITY_TOOLS = 16;


    // ===== BUILDING CONSTANTS =====

    /// <summary>
    /// Maximum plausible building rate in blocks per second.
    /// This is the theoretical max for rapid block placement.
    /// </summary>
    public const float MAX_BUILDING_RATE_BLOCKS_PER_SECOND = 5.0f;

    /// <summary>
    /// Maximum distance a player can place blocks from (in meters).
    /// Blocks placed further than this trigger cheat detection.
    /// </summary>
    public const float MAX_BUILDING_REACH_METERS = 5.0f;


    // ===== CLAIM BATCHING CONSTANTS =====

    /// <summary>
    /// Recommended interval for sending claim batches from client to server (in seconds).
    /// Smaller = more network traffic but faster validation.
    /// Larger = less network traffic but delayed cheat detection.
    /// </summary>
    public const float CLAIM_BATCH_INTERVAL_SECONDS = 2.0f;

    /// <summary>
    /// Maximum number of blocks in a single mining claim batch.
    /// Prevents extremely large claim submissions that could DOS the server.
    /// </summary>
    public const int MAX_BLOCKS_PER_MINING_CLAIM = 100;

    /// <summary>
    /// Maximum number of blocks in a single building claim batch.
    /// Prevents extremely large claim submissions that could DOS the server.
    /// </summary>
    public const int MAX_BLOCKS_PER_BUILDING_CLAIM = 100;


    // ===== TOOL WEAR RATES =====
    // Default tool wear per block mined (condition delta)
    // These are baseline values - actual wear depends on block hardness

    /// <summary>
    /// Tool wear when mining with wooden tools (per block).
    /// </summary>
    public const float WOODEN_TOOL_WEAR_PER_BLOCK = 0.010f; // 1% per block (100 blocks to break)

    /// <summary>
    /// Tool wear when mining with stone tools (per block).
    /// </summary>
    public const float STONE_TOOL_WEAR_PER_BLOCK = 0.005f; // 0.5% per block (200 blocks to break)

    /// <summary>
    /// Tool wear when mining with iron tools (per block).
    /// </summary>
    public const float IRON_TOOL_WEAR_PER_BLOCK = 0.002f; // 0.2% per block (500 blocks to break)

    /// <summary>
    /// Tool wear when mining with diamond tools (per block).
    /// </summary>
    public const float DIAMOND_TOOL_WEAR_PER_BLOCK = 0.001f; // 0.1% per block (1000 blocks to break)


    // ===== RARE ORE DETECTION (X-RAY HACK) =====

    /// <summary>
    /// Expected percentage of rare ore blocks in natural mining (e.g., diamond, gold).
    /// Used to detect X-ray hacks where players mine rare ores at abnormally high rates.
    /// </summary>
    public const float EXPECTED_RARE_ORE_PERCENTAGE = 0.05f; // 5% of blocks are rare ores

    /// <summary>
    /// Multiplier for rare ore detection threshold.
    /// If player mines rare ores at rate > EXPECTED * MULTIPLIER, flag as suspicious.
    /// </summary>
    public const float RARE_ORE_DETECTION_MULTIPLIER = 5.0f; // 5x normal rate = suspicious


    // ===== ANTI-CHEAT STATISTICAL THRESHOLDS =====

    /// <summary>
    /// Z-score threshold for statistical outlier detection.
    /// Players whose stats deviate more than this many standard deviations from the mean are flagged.
    /// Standard threshold: 3.0 (99.7% of normal distribution)
    /// </summary>
    public const float STATISTICAL_OUTLIER_Z_SCORE_THRESHOLD = 3.0f;

    /// <summary>
    /// Confidence threshold for cheat detection (0.0 to 1.0).
    /// Only report cheating if confidence exceeds this value.
    /// </summary>
    public const float CHEAT_DETECTION_CONFIDENCE_THRESHOLD = 0.80f; // 80% confidence


    // ===== HELPER METHODS =====

    /// <summary>
    /// Get expected tool wear for a specific tool and block type.
    /// Returns the baseline tool wear multiplied by block hardness.
    /// </summary>
    public static float GetExpectedToolWear(string toolRef, string blockType)
    {
        // Get baseline wear rate based on tool material
        var baseWear = toolRef.ToLower() switch
        {
            var t when t.Contains("diamond") => DIAMOND_TOOL_WEAR_PER_BLOCK,
            var t when t.Contains("iron") => IRON_TOOL_WEAR_PER_BLOCK,
            var t when t.Contains("stone") => STONE_TOOL_WEAR_PER_BLOCK,
            var t when t.Contains("wood") => WOODEN_TOOL_WEAR_PER_BLOCK,
            _ => IRON_TOOL_WEAR_PER_BLOCK // Default to iron
        };

        // Multiply by block hardness (harder blocks wear tools faster)
        var hardnessMultiplier = blockType.ToLower() switch
        {
            "dirt" or "sand" or "gravel" => 0.5f,  // Soft blocks wear slower
            "stone" or "cobblestone" => 1.0f,       // Normal wear
            "iron_ore" or "gold_ore" => 1.5f,       // Harder blocks wear faster
            "diamond_ore" or "obsidian" => 2.0f,    // Very hard blocks wear much faster
            _ => 1.0f // Default to normal wear
        };

        return baseWear * hardnessMultiplier;
    }

    /// <summary>
    /// Check if a block type is considered a rare ore for X-ray detection.
    /// </summary>
    public static bool IsRareOre(string blockType)
    {
        var blockLower = blockType.ToLower();
        return blockLower.Contains("diamond") ||
               blockLower.Contains("gold") ||
               blockLower.Contains("emerald") ||
               blockLower.Contains("ruby") ||
               blockLower.Contains("ancient");
    }

    /// <summary>
    /// Calculate 3D distance between two positions.
    /// </summary>
    public static double CalculateDistance(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var dz = z2 - z1;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Calculate 2D distance between two positions (ignoring vertical).
    /// </summary>
    public static double CalculateDistance2D(double x1, double z1, double x2, double z2)
    {
        var dx = x2 - x1;
        var dz = z2 - z1;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
