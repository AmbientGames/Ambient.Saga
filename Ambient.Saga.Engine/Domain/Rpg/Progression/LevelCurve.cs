namespace Ambient.Saga.Engine.Domain.Rpg.Progression;

/// <summary>
/// Converts accumulated experience into avatar levels.
/// Triangular curve: each level-up costs ExperiencePerLevelBase × current level,
/// so the cumulative cost to reach level L is Base × L × (L − 1) / 2.
/// Calibrated against EverestTrail's authored XP economy (~178k total XP,
/// MinimumLevel gates 2–50): level 23 ≈ 25.3k XP, level 50 ≈ 122.5k XP —
/// the deepest gates require near-completionist play. Tune ExperiencePerLevelBase
/// to re-pace progression globally.
/// </summary>
public static class LevelCurve
{
    public const int ExperiencePerLevelBase = 100;
    public const int MaxLevel = 99;

    /// <summary>The level an avatar with this much accumulated experience has reached (≥ 1).</summary>
    public static int GetLevelForExperience(float experience)
    {
        var level = 1;
        while (level < MaxLevel && experience >= GetExperienceForLevel(level + 1))
            level++;
        return level;
    }

    /// <summary>Cumulative experience required to reach the given level.</summary>
    public static float GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        return ExperiencePerLevelBase * (level * (level - 1)) / 2f;
    }
}
