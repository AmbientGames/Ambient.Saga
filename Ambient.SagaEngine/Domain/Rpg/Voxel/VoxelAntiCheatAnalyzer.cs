using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Domain.Rpg.Voxel;

/// <summary>
/// Result of anti-cheat analysis for a player.
/// </summary>
public class CheatReport
{
    public Guid AvatarId { get; set; }
    public DateTime AnalysisStartTime { get; set; }
    public DateTime AnalysisEndTime { get; set; }
    public List<CheatFlag> Flags { get; set; } = new();
    public bool IsSuspicious => Flags.Any(f => f.Confidence >= VoxelGameConstants.CHEAT_DETECTION_CONFIDENCE_THRESHOLD);
    public float MaxConfidence => Flags.Any() ? Flags.Max(f => f.Confidence) : 0.0f;
}

/// <summary>
/// Individual cheat detection flag.
/// </summary>
public class CheatFlag
{
    public CheatType Type { get; set; }
    public float Confidence { get; set; } // 0.0 to 1.0
    public string Evidence { get; set; } = string.Empty;
    public DateTime? FirstOccurrence { get; set; }
    public int OccurrenceCount { get; set; }
}

/// <summary>
/// Types of cheats that can be detected.
/// </summary>
public enum CheatType
{
    SpeedHack,           // Mining/building/movement too fast
    Teleportation,       // Instant position changes
    XRayHack,            // Finding rare ores at abnormal rates
    DurabilityHack,      // Tools not wearing out properly
    InfiniteInventory,   // Placing more blocks than carried
    ReachHack,           // Mining/building beyond max reach
    FlyHack             // Flying without legitimate means
}

/// <summary>
/// Statistical anti-cheat analyzer for voxel interactions.
/// Performs retrospective analysis on transaction logs to detect cheating patterns.
/// </summary>
public static class VoxelAntiCheatAnalyzer
{
    /// <summary>
    /// Analyze a single player's voxel transactions for cheating patterns.
    /// </summary>
    public static CheatReport AnalyzePlayer(
        Guid avatarId,
        IEnumerable<SagaInstance> allSagaInstances,
        TimeSpan analysisWindow)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - analysisWindow;

        var report = new CheatReport
        {
            AvatarId = avatarId,
            AnalysisStartTime = startTime,
            AnalysisEndTime = endTime
        };

        // Get all voxel transactions for this avatar in the window
        var transactions = allSagaInstances
            .SelectMany(saga => saga.Transactions)
            .Where(t => t.AvatarId == avatarId.ToString())
            .Where(t => t.Status == TransactionStatus.Committed)
            .Where(t => t.GetCanonicalTimestamp() >= startTime && t.GetCanonicalTimestamp() <= endTime)
            .OrderBy(t => t.GetCanonicalTimestamp())
            .ToList();

        // Run detection algorithms
        DetectSpeedHacks(transactions, report);
        DetectTeleportation(transactions, report);
        DetectXRayHacks(transactions, report);
        DetectDurabilityHacks(transactions, report);
        DetectReachHacks(transactions, report);

        return report;
    }

    /// <summary>
    /// Analyze all players and return those with suspicious activity.
    /// </summary>
    public static List<CheatReport> AnalyzeAllPlayers(
        IEnumerable<Guid> avatarIds,
        IEnumerable<SagaInstance> allSagaInstances,
        TimeSpan analysisWindow)
    {
        var reports = new List<CheatReport>();

        foreach (var avatarId in avatarIds)
        {
            var report = AnalyzePlayer(avatarId, allSagaInstances, analysisWindow);
            if (report.IsSuspicious)
            {
                reports.Add(report);
            }
        }

        return reports.OrderByDescending(r => r.MaxConfidence).ToList();
    }

    /// <summary>
    /// Detect mining/building speed hacks via statistical analysis.
    /// </summary>
    private static void DetectSpeedHacks(List<SagaTransaction> transactions, CheatReport report)
    {
        // Analyze mining sessions
        var miningSessions = transactions
            .Where(t => t.Type == SagaTransactionType.MiningSessionClaimed)
            .ToList();

        if (miningSessions.Count == 0) return;

        // Calculate average mining rate
        var miningRates = miningSessions
            .Select(t => t.GetData<float>("MiningRate"))
            .ToList();

        var avgMiningRate = miningRates.Average();
        var maxMiningRate = miningRates.Max();

        // Flag if consistently mining above 90% of theoretical max
        var suspiciousSessionCount = miningRates
            .Count(r => r > VoxelGameConstants.MAX_MINING_RATE_BLOCKS_PER_SECOND * 0.9f);

        if (suspiciousSessionCount > miningSessions.Count * 0.5f) // More than half are suspicious
        {
            report.Flags.Add(new CheatFlag
            {
                Type = CheatType.SpeedHack,
                Confidence = 0.85f,
                Evidence = $"Avg mining rate: {avgMiningRate:F1} b/s, Max: {maxMiningRate:F1} b/s, {suspiciousSessionCount}/{miningSessions.Count} sessions near theoretical max",
                OccurrenceCount = suspiciousSessionCount
            });
        }

        // Analyze building sessions
        var buildingSessions = transactions
            .Where(t => t.Type == SagaTransactionType.BuildingSessionClaimed)
            .ToList();

        if (buildingSessions.Count == 0) return;

        var buildingRates = buildingSessions
            .Select(t => t.GetData<float>("BuildingRate"))
            .ToList();

        var avgBuildingRate = buildingRates.Average();
        var maxBuildingRate = buildingRates.Max();

        var suspiciousBuildingSessionCount = buildingRates
            .Count(r => r > VoxelGameConstants.MAX_BUILDING_RATE_BLOCKS_PER_SECOND * 0.9f);

        if (suspiciousBuildingSessionCount > buildingSessions.Count * 0.5f)
        {
            report.Flags.Add(new CheatFlag
            {
                Type = CheatType.SpeedHack,
                Confidence = 0.85f,
                Evidence = $"Avg building rate: {avgBuildingRate:F1} b/s, Max: {maxBuildingRate:F1} b/s, {suspiciousBuildingSessionCount}/{buildingSessions.Count} sessions near theoretical max",
                OccurrenceCount = suspiciousBuildingSessionCount
            });
        }
    }

    /// <summary>
    /// Detect teleportation via impossible movement speeds.
    /// </summary>
    private static void DetectTeleportation(List<SagaTransaction> transactions, CheatReport report)
    {
        var locationClaims = transactions
            .Where(t => t.Type == SagaTransactionType.LocationClaimed)
            .OrderBy(t => t.GetCanonicalTimestamp())
            .ToList();

        if (locationClaims.Count < 2) return;

        var teleportationCount = 0;
        DateTime? firstTeleportation = null;

        for (var i = 1; i < locationClaims.Count; i++)
        {
            var prev = locationClaims[i - 1];
            var curr = locationClaims[i];

            var timeDelta = (curr.GetCanonicalTimestamp() - prev.GetCanonicalTimestamp()).TotalSeconds;
            if (timeDelta <= 0) continue;

            var prevX = prev.GetData<double>("PositionX");
            var prevY = prev.GetData<double>("PositionY");
            var prevZ = prev.GetData<double>("PositionZ");

            var currX = curr.GetData<double>("PositionX");
            var currY = curr.GetData<double>("PositionY");
            var currZ = curr.GetData<double>("PositionZ");

            var distance = VoxelGameConstants.CalculateDistance(prevX, prevY, prevZ, currX, currY, currZ);
            var speed = distance / timeDelta;

            // Teleportation = moving 2x faster than possible
            if (speed > VoxelGameConstants.MAX_MOVEMENT_SPEED_METERS_PER_SECOND * 2.0)
            {
                teleportationCount++;
                firstTeleportation ??= curr.GetCanonicalTimestamp();
            }
        }

        if (teleportationCount > 0)
        {
            // Teleportation is strong evidence of cheating - start at 0.85 confidence
            var confidence = Math.Min(0.98f, 0.85f + teleportationCount * 0.03f); // More occurrences = higher confidence
            report.Flags.Add(new CheatFlag
            {
                Type = CheatType.Teleportation,
                Confidence = confidence,
                Evidence = $"{teleportationCount} impossible movement speeds detected across {locationClaims.Count} location claims",
                FirstOccurrence = firstTeleportation,
                OccurrenceCount = teleportationCount
            });
        }
    }

    /// <summary>
    /// Detect X-ray hacks via rare ore distribution analysis.
    /// </summary>
    private static void DetectXRayHacks(List<SagaTransaction> transactions, CheatReport report)
    {
        var miningSessions = transactions
            .Where(t => t.Type == SagaTransactionType.MiningSessionClaimed)
            .ToList();

        if (miningSessions.Count == 0) return;

        // Calculate overall rare ore percentage
        var totalBlocks = miningSessions.Sum(t => t.GetData<int>("BlockCount"));
        var totalRareOrePercentage = miningSessions
            .Sum(t => t.GetData<float>("RareOrePercentage") * t.GetData<int>("BlockCount")) / totalBlocks;

        var expectedRareOrePercentage = VoxelGameConstants.EXPECTED_RARE_ORE_PERCENTAGE;

        // Flag if rare ore rate is abnormally high
        if (totalRareOrePercentage > expectedRareOrePercentage * 3.0f && totalBlocks > 50)
        {
            var confidence = Math.Min(0.95f, 0.70f + totalRareOrePercentage / expectedRareOrePercentage * 0.05f);
            report.Flags.Add(new CheatFlag
            {
                Type = CheatType.XRayHack,
                Confidence = confidence,
                Evidence = $"Rare ore rate: {totalRareOrePercentage:P1} (expected: {expectedRareOrePercentage:P1}) across {totalBlocks} blocks mined",
                OccurrenceCount = miningSessions.Count
            });
        }
    }

    /// <summary>
    /// Detect infinite durability hacks via tool wear analysis.
    /// </summary>
    private static void DetectDurabilityHacks(List<SagaTransaction> transactions, CheatReport report)
    {
        var toolWearClaims = transactions
            .Where(t => t.Type == SagaTransactionType.ToolWearClaimed)
            .ToList();

        if (toolWearClaims.Count == 0) return;

        // Group by tool type
        foreach (var toolGroup in toolWearClaims.GroupBy(t => t.GetData<string>("ToolRef")))
        {
            var toolRef = toolGroup.Key;
            var claims = toolGroup.ToList();

            var totalWear = claims.Sum(t => t.GetData<float>("WearDelta"));
            var totalBlocks = claims.Sum(t => t.GetData<int>("BlocksMinedWithTool"));

            if (totalBlocks == 0) continue;

            var actualWearRate = totalWear / totalBlocks;
            var expectedWearRate = VoxelGameConstants.GetExpectedToolWear(toolRef, "stone"); // Use stone as baseline

            // Flag if wearing out 10x slower than expected
            if (actualWearRate < expectedWearRate * 0.1f)
            {
                report.Flags.Add(new CheatFlag
                {
                    Type = CheatType.DurabilityHack,
                    Confidence = 0.95f,
                    Evidence = $"{toolRef}: {actualWearRate:F6} wear/block (expected: {expectedWearRate:F6}) across {totalBlocks} blocks",
                    OccurrenceCount = claims.Count
                });
            }
        }
    }

    /// <summary>
    /// Detect reach hacks via block distance analysis.
    /// NOTE: This is already validated in real-time, but we can detect patterns here.
    /// </summary>
    private static void DetectReachHacks(List<SagaTransaction> transactions, CheatReport report)
    {
        // Reach hacks are already caught by real-time validation in handlers
        // This method could analyze patterns like "always mining at max reach" which might indicate
        // a more sophisticated reach hack that's just below the threshold

        var miningSessions = transactions
            .Where(t => t.Type == SagaTransactionType.MiningSessionClaimed)
            .ToList();

        if (miningSessions.Count == 0) return;

        // TODO: Future enhancement - analyze block distances from player position
        // and flag if player is consistently mining at suspiciously uniform distances
    }

    /// <summary>
    /// Calculate Z-score for statistical outlier detection.
    /// </summary>
    public static double CalculateZScore(double value, double mean, double stdDev)
    {
        if (stdDev == 0) return 0;
        return (value - mean) / stdDev;
    }

    /// <summary>
    /// Calculate standard deviation.
    /// </summary>
    public static double CalculateStdDev(IEnumerable<double> values)
    {
        var enumerable = values.ToList();
        var count = enumerable.Count();
        if (count == 0) return 0;

        var avg = enumerable.Average();
        var sumOfSquares = enumerable.Sum(val => (val - avg) * (val - avg));
        return Math.Sqrt(sumOfSquares / count);
    }

    /// <summary>
    /// Get community-wide statistics for comparative analysis.
    /// Useful for detecting players who are statistical outliers.
    /// </summary>
    public static CommunityStats GetCommunityStats(
        IEnumerable<Guid> avatarIds,
        IEnumerable<SagaInstance> allSagaInstances,
        TimeSpan analysisWindow)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - analysisWindow;

        var playerStats = new List<PlayerStats>();

        foreach (var avatarId in avatarIds)
        {
            var transactions = allSagaInstances
                .SelectMany(saga => saga.Transactions)
                .Where(t => t.AvatarId == avatarId.ToString())
                .Where(t => t.Status == TransactionStatus.Committed)
                .Where(t => t.GetCanonicalTimestamp() >= startTime && t.GetCanonicalTimestamp() <= endTime)
                .ToList();

            var miningTransactions = transactions.Where(t => t.Type == SagaTransactionType.MiningSessionClaimed).ToList();
            if (miningTransactions.Count == 0) continue;

            var avgMiningRate = miningTransactions.Average(t => t.GetData<float>("MiningRate"));
            var totalBlocks = miningTransactions.Sum(t => t.GetData<int>("BlockCount"));
            var rareOreRate = totalBlocks > 0
                ? miningTransactions.Sum(t => t.GetData<float>("RareOrePercentage") * t.GetData<int>("BlockCount")) / totalBlocks
                : 0;

            playerStats.Add(new PlayerStats
            {
                AvatarId = avatarId,
                AverageMiningRate = avgMiningRate,
                RareOreRate = rareOreRate
            });
        }

        return new CommunityStats
        {
            TotalPlayers = playerStats.Count,
            AverageMiningRate = playerStats.Any() ? playerStats.Average(p => p.AverageMiningRate) : 0,
            MiningRateStdDev = CalculateStdDev(playerStats.Select(p => (double)p.AverageMiningRate)),
            AverageRareOreRate = playerStats.Any() ? playerStats.Average(p => p.RareOreRate) : 0,
            RareOreRateStdDev = CalculateStdDev(playerStats.Select(p => (double)p.RareOreRate))
        };
    }
}

public class PlayerStats
{
    public Guid AvatarId { get; set; }
    public float AverageMiningRate { get; set; }
    public float RareOreRate { get; set; }
}

public class CommunityStats
{
    public int TotalPlayers { get; set; }
    public double AverageMiningRate { get; set; }
    public double MiningRateStdDev { get; set; }
    public double AverageRareOreRate { get; set; }
    public double RareOreRateStdDev { get; set; }
}
