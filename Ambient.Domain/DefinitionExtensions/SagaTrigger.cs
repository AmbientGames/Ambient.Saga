namespace Ambient.Domain;

/// <summary>
/// Extensions for Trigger (generated from XSD).
/// Triggers are identity-less spatial zones - no type, just radius and spawn logic.
/// </summary>
public partial class SagaTrigger
{
    // Priority for z-ordering triggers in UI (set dynamically during expansion, not from XML)
    // Lower priority = outer triggers (back layer), higher = inner triggers (front layer)
    public int Priority { get; set; } = 0;
}
