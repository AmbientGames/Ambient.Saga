using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Services;
using System.Numerics;

namespace Ambient.Saga.UI.ViewModels;

/// <summary>
/// Simple status-based colors for saga dots on the map.
/// Players care about "can I interact?" not the category.
/// Category is shown in tooltip on hover.
/// </summary>
public static class SagaColors
{
    // Status-based colors - simple and actionable for players
    public static readonly Vector4 Available = new Vector4(50f / 255f, 205f / 255f, 50f / 255f, 1f);   // Lime Green - "go here"
    public static readonly Vector4 Locked = new Vector4(100f / 255f, 100f / 255f, 100f / 255f, 1f);    // Dark Gray - "not yet"
    public static readonly Vector4 Complete = new Vector4(60f / 255f, 60f / 255f, 60f / 255f, 1f);     // Dim Gray - "done"

    /// <summary>
    /// Gets the color for a feature based on its interaction status.
    /// Status is what players care about - category is shown in tooltip.
    /// </summary>
    public static Vector4 GetColor(SagaArcCategory category, InteractionStatus status)
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
