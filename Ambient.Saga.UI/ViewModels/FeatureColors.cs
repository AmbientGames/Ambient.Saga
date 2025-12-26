using Ambient.Saga.Engine.Domain.Services;
using System.Numerics;

namespace Ambient.Saga.UI.ViewModels;

/// <summary>
/// Simple status-based colors for feature dots.
/// Players care about "can I interact?" not the feature type.
/// Feature type is shown in tooltip on hover.
/// </summary>
public static class FeatureColors
{
    // Status-based colors - simple and actionable for players
    public static readonly Vector4 Available = new Vector4(50f / 255f, 205f / 255f, 50f / 255f, 1f);   // Lime Green - "go here"
    public static readonly Vector4 Locked = new Vector4(100f / 255f, 100f / 255f, 100f / 255f, 1f);    // Dark Gray - "not yet"
    public static readonly Vector4 Complete = new Vector4(60f / 255f, 60f / 255f, 60f / 255f, 1f);     // Dim Gray - "done"

    /// <summary>
    /// Gets the color for a feature based on its interaction status.
    /// Status is what players care about - type is shown in tooltip.
    /// </summary>
    public static Vector4 GetColor(FeatureType featureType, InteractionStatus status)
    {
        return status switch
        {
            InteractionStatus.Available => Available,
            InteractionStatus.Locked => Locked,
            InteractionStatus.Complete => Complete,
            _ => Available // Default to available
        };
    }
}
