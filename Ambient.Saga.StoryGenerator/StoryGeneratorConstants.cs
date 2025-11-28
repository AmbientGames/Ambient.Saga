namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Constants for story generation - centralized configuration
/// </summary>
public static class StoryGeneratorConstants
{
    // === Generation Parameters ===

    /// <summary>
    /// Number of locations generated in spiral pattern (for 0-1 source locations)
    /// </summary>
    public const int SpiralLocationCount = 50;

    /// <summary>
    /// Maximum distance between consecutive waypoints in meters
    /// Paths longer than this are subdivided with intermediate landmarks
    /// </summary>
    public const double MaxWaypointDistanceMeters = 5000.0;

    // === Folder Structure ===

    /// <summary>
    /// Default folder name for generated content (used when Template is not specified)
    /// </summary>
    public const string DefaultOutputFolder = "Procedural";

    /// <summary>
    /// Subfolder for gameplay-related content
    /// </summary>
    public const string GameplayFolder = "Gameplay";

    // === Content Type Subfolders ===

    public const string StructuresFolder = "Structures";
    public const string ActorsFolder = "Actors";
    public const string AcquirablesFolder = "Acquirables";
    public const string LandmarksFolder = "Landmarks";
    public const string QuestsFolder = "Quests";

    // === Saga/Zone Defaults ===

    /// <summary>
    /// Default enter radius for saga triggers in meters
    /// </summary>
    public const double DefaultEnterRadius = 50.0;

    /// <summary>
    /// Default exit radius for saga triggers in meters
    /// </summary>
    public const double DefaultExitRadius = 52.0;

    // === Character Defaults ===

    // Boss stats
    public const double BossHealth = 0.8;
    public const double BossStamina = 1.0;
    public const double BossMana = 1.0;
    public const double BossStrength = 0.15;
    public const double BossDefense = 0.10;
    public const double BossSpeed = 0.05;
    public const double BossMagic = 0.08;
    public const int BossCredits = 50;

    // Merchant stats
    public const double MerchantHealth = 0.5;
    public const double MerchantStamina = 1.0;
    public const double MerchantMana = 1.0;
    public const double MerchantStrength = 0.05;
    public const double MerchantDefense = 0.05;
    public const double MerchantSpeed = 0.05;
    public const double MerchantMagic = 0.05;
    public const int MerchantCredits = 1000;

    // NPC/QuestGiver stats
    public const double NpcHealth = 0.6;
    public const double NpcStamina = 1.0;
    public const double NpcMana = 1.0;
    public const double NpcStrength = 0.05;
    public const double NpcDefense = 0.05;
    public const double NpcSpeed = 0.05;
    public const double NpcMagic = 0.05;
    public const int NpcCredits = 10;

    // === Item Defaults ===

    /// <summary>
    /// Default quantity for merchant inventory items
    /// </summary>
    public const int DefaultItemQuantity = 50;

    // === Arrangement Detection ===

    /// <summary>
    /// Cluster score bonus when hub node detected
    /// </summary>
    public const int ClusterDetectionBonus = 50;

    // === Achievement Defaults ===

    /// <summary>
    /// Multiplier for merchant achievement threshold (merchant count * this value)
    /// </summary>
    public const int MerchantAchievementMultiplier = 5;
}
