using Ambient.SagaEngine.Domain.Services;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.ViewModels;

/// <summary>
/// Pre-calculated colors for feature dots based on type and interaction status.
/// Uses ImGui Vector4 colors (RGBA from 0-1).
/// </summary>
public static class FeatureColors
{
    // Complete status - same gray for all types
    private static readonly Vector4 CompletedGray = new Vector4(128f / 255f, 128f / 255f, 128f / 255f, 1f);

    // Structure colors (Blue)
    private static readonly Vector4 StructureAvailable = new Vector4(30f / 255f, 144f / 255f, 1f, 1f); // Dodger Blue (bright)
    private static readonly Vector4 StructureLocked = new Vector4(70f / 255f, 130f / 255f, 180f / 255f, 1f); // Steel Blue (dull)

    // Landmark colors (Red)
    private static readonly Vector4 LandmarkAvailable = new Vector4(1f, 69f / 255f, 0f, 1f); // Orange Red (bright)
    private static readonly Vector4 LandmarkLocked = new Vector4(178f / 255f, 34f / 255f, 34f / 255f, 1f); // Firebrick (dull)

    // QuestSignpost colors (Green)
    private static readonly Vector4 QuestSignpostAvailable = new Vector4(50f / 255f, 205f / 255f, 50f / 255f, 1f); // Lime Green (bright)
    private static readonly Vector4 QuestSignpostLocked = new Vector4(107f / 255f, 142f / 255f, 35f / 255f, 1f); // Olive Drab (dull)

    /// <summary>
    /// Gets the color for a feature based on its type and interaction status.
    /// </summary>
    public static Vector4 GetColor(FeatureType featureType, InteractionStatus status)
    {
        // Completed is always gray regardless of type
        if (status == InteractionStatus.Complete)
            return CompletedGray;

        // Type-specific colors for Available/Locked
        return (featureType, status) switch
        {
            (FeatureType.Structure, InteractionStatus.Available) => StructureAvailable,
            (FeatureType.Structure, InteractionStatus.Locked) => StructureLocked,

            (FeatureType.Landmark, InteractionStatus.Available) => LandmarkAvailable,
            (FeatureType.Landmark, InteractionStatus.Locked) => LandmarkLocked,

            (FeatureType.QuestSignpost, InteractionStatus.Available) => QuestSignpostAvailable,
            (FeatureType.QuestSignpost, InteractionStatus.Locked) => QuestSignpostLocked,

            _ => new Vector4(1f, 1f, 1f, 1f) // White fallback
        };
    }

    // Expose colors for legend binding
    public static Vector4 StructureAvailableColor => StructureAvailable;
    public static Vector4 StructureLockedColor => StructureLocked;
    public static Vector4 StructureCompletedColor => CompletedGray;

    public static Vector4 LandmarkAvailableColor => LandmarkAvailable;
    public static Vector4 LandmarkLockedColor => LandmarkLocked;
    public static Vector4 LandmarkCompletedColor => CompletedGray;

    public static Vector4 QuestSignpostAvailableColor => QuestSignpostAvailable;
    public static Vector4 QuestSignpostLockedColor => QuestSignpostLocked;
    public static Vector4 QuestSignpostCompletedColor => CompletedGray;
}
