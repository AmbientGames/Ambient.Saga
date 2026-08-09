namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

/// <summary>
/// Encodes a substance as its bit in a tool's suitability mask (tool.Class).
/// The bit position is the substance's ordinal, so the mask covers all 16 substances.
/// </summary>
public static class SubstanceSuitabilityEncoder
{
    public static uint Encode(SubstanceType substance)
    {
        return 1u << (int)substance;
    }
}
