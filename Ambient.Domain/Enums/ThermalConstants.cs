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

    /// <summary>Mild hypothermia — the cold WARNING band begins (no damage yet: mild
    /// damage starts one degree past this, at 34 °C — SurvivalCalculator's damage-onset
    /// tuning, 2026-07-15).</summary>
    public const float ColdWarning = 35f;

    /// <summary>Severe hypothermia — damage escalates to the SEVERE rate.</summary>
    public const float ColdCritical = 33f;

    /// <summary>Mild hyperthermia — the hot WARNING band begins (mild damage starts at
    /// 40 °C).</summary>
    public const float HotWarning = 39f;

    /// <summary>Severe hyperthermia — damage escalates to the SEVERE rate.</summary>
    public const float HotCritical = 41f;
}
