namespace Ambient.Domain.Enums;

/// <summary>
/// Body-temperature thresholds in Celsius. These are the single source of truth
/// for what counts as a cold, hot, or critical body temperature — used both by
/// the HUD (for colors, warnings, progress bars) and by the survival simulation
/// (for damage triggers and warning messages).
///
/// Sim-only tuning values (drift rate, damage rates, shelter scale, ambient
/// comfort pivot) stay in the simulation code — they describe how fast the
/// model moves, not what the model means.
/// </summary>
public static class ThermalConstants
{
    /// <summary>Normal human body temperature.</summary>
    public const float NormalBodyTemp = 37f;

    /// <summary>Mild hypothermia — HUD should show a cold warning.</summary>
    public const float ColdWarning = 35f;

    /// <summary>Severe hypothermia — health damage begins.</summary>
    public const float ColdCritical = 33f;

    /// <summary>Mild hyperthermia — HUD should show a hot warning.</summary>
    public const float HotWarning = 39f;

    /// <summary>Severe hyperthermia — health damage begins.</summary>
    public const float HotCritical = 41f;
}
