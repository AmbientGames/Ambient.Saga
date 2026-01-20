namespace Ambient.Saga.UI.Configuration;

/// <summary>
/// Runtime configuration for UI rendering modes.
/// Controls whether developer-focused information is displayed in modals.
/// </summary>
/// <remarks>
/// In Debug builds, developer info is shown by default (pixel coordinates, RefNames, etc.).
/// In Release builds, only player-facing information is shown.
/// Can be toggled at runtime for testing.
/// </remarks>
public static class GameConfiguration
{
    /// <summary>
    /// When true, modals display developer-focused information such as:
    /// - Pixel coordinates for characters/locations
    /// - RefName identifiers in brackets
    /// - Generation seeds and file paths
    /// - ISO format timestamps
    /// - Transaction IDs and saga instance details
    /// </summary>
    public static bool ShowDeveloperInfo { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Toggle developer info visibility at runtime.
    /// Useful for testing player view in debug builds.
    /// </summary>
    public static void ToggleDeveloperInfo() => ShowDeveloperInfo = !ShowDeveloperInfo;

    /// <summary>
    /// Standard color for developer info text (gray, secondary importance).
    /// </summary>
    public static System.Numerics.Vector4 DevInfoColor { get; } = new(0.6f, 0.6f, 0.6f, 1f);

    /// <summary>
    /// Standard color for developer info headers/labels.
    /// </summary>
    public static System.Numerics.Vector4 DevInfoLabelColor { get; } = new(0.5f, 0.5f, 0.6f, 1f);
}
