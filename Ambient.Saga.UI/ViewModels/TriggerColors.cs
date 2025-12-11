using Ambient.Saga.Engine.Domain.Services;
using System.Numerics;

namespace Ambient.Saga.UI.ViewModels;

/// <summary>
/// Pre-calculated colors for trigger rings based on interaction status.
/// Uses ImGui Vector4 colors (RGBA from 0-1).
/// </summary>
public static class TriggerColors
{
    // Trigger status colors (RGB values divided by 255 for 0-1 range)
    private static readonly Vector4 Available = new Vector4(50f / 255f, 205f / 255f, 50f / 255f, 1f); // Lime Green
    private static readonly Vector4 Locked = new Vector4(1f, 0f, 0f, 1f); // Red
    private static readonly Vector4 Complete = new Vector4(128f / 255f, 128f / 255f, 128f / 255f, 1f); // Gray

    /// <summary>
    /// Gets the color for a trigger based on its interaction status.
    /// </summary>
    public static Vector4 GetColor(InteractionStatus status)
    {
        return status switch
        {
            InteractionStatus.Available => Available,
            InteractionStatus.Locked => Locked,
            InteractionStatus.Complete => Complete,
            _ => Available
        };
    }

    // Expose colors for legend binding
    public static Vector4 AvailableColor => Available;
    public static Vector4 LockedColor => Locked;
    public static Vector4 CompleteColor => Complete;
}
