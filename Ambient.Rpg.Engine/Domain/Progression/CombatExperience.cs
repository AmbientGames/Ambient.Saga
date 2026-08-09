using Ambient.Domain;

namespace Ambient.Rpg.Engine.Domain.Progression;

/// <summary>
/// Experience an avatar earns for WINNING a battle, scaled by the defeated enemy's
/// difficulty so a boss is worth many times a trivial creature. This is the combat
/// counterpart to an authored quest <c>Experience</c> reward — it feeds the very same
/// <see cref="LevelCurve"/> the quest-reward path uses
/// (see QuestRewardDistributor.DistributeReward), so both producers advance the avatar
/// identically and neither ever demotes its level.
///
/// The amount is derived ENTIRELY from data already authored on the enemy's template,
/// so no new content is required and a designer re-tunes an enemy's worth simply by
/// editing that enemy:
///   • <see cref="CharacterStats.Level"/> — the authored difficulty tier (a boss sits at
///     a much higher level than a roadside creature);
///   • the enemy's normalized combat stats (Strength / Defense / Speed / Magic, each in
///     the engine's 0–1 range), summed into a 0–4 "combat power" so a well-armed enemy of
///     the same tier is worth more than a frail one, without letting any single stat
///     dominate.
///
///   xp = BaseVictoryExperience × tier × (1 + Strength + Defense + Speed + Magic)
///
/// Calibrated against <see cref="LevelCurve"/> (100 XP to reach level 2): a plain level-1
/// creature with no combat stats yields exactly <see cref="BaseVictoryExperience"/>
/// (so early levels take several kills), while a high-level, high-stat boss is worth many
/// multiples. The formula is deliberately simple and TUNABLE — adjust
/// <see cref="BaseVictoryExperience"/> to re-pace combat progression globally.
/// </summary>
public static class CombatExperience
{
    /// <summary>
    /// Experience for defeating a level-1 enemy with no combat stats. The single tuning
    /// knob for combat progression pace; not a per-enemy magic number.
    /// </summary>
    public const float BaseVictoryExperience = 10f;

    /// <summary>
    /// Experience awarded for defeating an enemy with the given template stats. Returns
    /// <see cref="BaseVictoryExperience"/> for a statless/unknown enemy.
    /// </summary>
    public static float GetVictoryExperience(CharacterStats? enemyStats)
    {
        if (enemyStats == null)
            return BaseVictoryExperience;

        // Level is the authored difficulty tier; clamp to >= 1 so a level-0/unset enemy
        // still yields the base award rather than zero.
        var tier = Math.Max(1, enemyStats.Level);

        // Normalized 0–1 combat stats; their sum (0–4) rewards tougher enemies of the same
        // tier without any single stat dominating the multiplier.
        var combatPower = enemyStats.Strength + enemyStats.Defense + enemyStats.Speed + enemyStats.Magic;

        return BaseVictoryExperience * tier * (1f + combatPower);
    }
}
