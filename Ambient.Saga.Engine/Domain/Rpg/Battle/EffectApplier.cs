using System;
using System.Linq;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Saga.Engine.Domain.Rpg.Battle;

/// <summary>
/// Applies combat effects from spells, consumables, and equipment.
/// Extracted from Godot BattleUI.cs - EXACT calculation logic preserved.
/// This is deterministic and can be used by both server and client.
/// </summary>
public static class EffectApplier
{
    /// <summary>
    /// Result of applying effects to a character's stats.
    /// </summary>
    public class EffectResult
    {
        public string StatName { get; set; } = string.Empty;
        public double Change { get; set; }
        public bool AppliedToAttacker { get; set; }  // true if applied to attacker/caster, false if to target
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// Apply all effects from an item (spell/consumable/equipment) to characters.
    /// EXACT COPY of BattleUI.cs ApplyEffects method (lines 758-807).
    /// </summary>
    /// <param name="effects">The character effects to apply</param>
    /// <param name="itemAffinity">Item's affinity (can override attacker affinity)</param>
    /// <param name="condition">Item condition (0.0-1.0, degrades effect strength)</param>
    /// <param name="attackerAffinity">Attacker's base affinity</param>
    /// <param name="defenderAffinity">Defender's affinity</param>
    /// <param name="isOffensive">True for offensive items (damage), false for defensive (healing)</param>
    /// <param name="world">World data for affinity matchup lookups</param>
    /// <param name="itemTypeName">Name of item type (for result source tracking)</param>
    /// <returns>List of stat changes to apply</returns>
    public static EffectResult[] ApplyEffects(
        CharacterEffects effects,
        string? itemAffinity,
        float condition,
        string? attackerAffinity,
        string? defenderAffinity,
        bool isOffensive,
        IWorld world,
        string itemTypeName)
    {
        var results = new List<EffectResult>();

        // === AFFINITY CALCULATION ===
        // Determine attacker's affinity (item affinity overrides battler affinity for that action)
        var effectiveAttackerAffinity = itemAffinity;
        if (string.IsNullOrEmpty(effectiveAttackerAffinity))
        {
            effectiveAttackerAffinity = attackerAffinity;
        }

        // Calculate affinity multiplier (only applies to offensive effects)
        var affinityMultiplier = isOffensive
            ? CalculateAffinityMultiplier(effectiveAttackerAffinity, defenderAffinity, world)
            : 1.0f;

        // Apply each character stat effect (no reflection!)
        // Per XSD: Negative = costs to caster, Positive = effects on target (inverted if Offensive)
        AddEffectResult(results, "Health", effects.Health, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Stamina", effects.Stamina, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Mana", effects.Mana, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Strength", effects.Strength, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Defense", effects.Defense, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Speed", effects.Speed, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Magic", effects.Magic, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Temperature", effects.Temperature, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Hunger", effects.Hunger, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Thirst", effects.Thirst, condition, affinityMultiplier, isOffensive, itemTypeName);
        AddEffectResult(results, "Insulation", effects.Insulation, condition, affinityMultiplier, isOffensive, itemTypeName);

        return results.ToArray();
    }

    /// <summary>
    /// Calculate effect for a single stat.
    /// EXACT COPY of BattleUI.cs ApplySingleEffect method (lines 809-849).
    /// </summary>
    private static void AddEffectResult(
        List<EffectResult> results,
        string statName,
        float effectValueRaw,
        float condition,
        float affinityMultiplier,
        bool isOffensive,
        string itemTypeName)
    {
        if (effectValueRaw == 0) return;

        double effectValue = effectValueRaw;

        // Apply condition degradation to all effects
        effectValue *= condition;

        // Per XSD Specification (Spell.xsd lines 21-26):
        // - NEGATIVE values = costs/penalties to CASTER
        // - POSITIVE values = effects on TARGET (inverted if Offensive)

        if (effectValue < 0)
        {
            // NEGATIVE = Cost to CASTER (e.g., Mana="-0.08" costs caster 0.08 mana)
            // Apply to attacker/caster, not target
            results.Add(new EffectResult
            {
                StatName = statName,
                Change = effectValue,
                AppliedToAttacker = true,
                Source = itemTypeName
            });
        }
        else
        {
            // POSITIVE = Effect on TARGET
            if (isOffensive)
            {
                // OFFENSIVE: Invert positive to negative (damage)
                // e.g., Health="0.25" becomes -0.25 damage to target
                effectValue = -effectValue;

                // Apply affinity multiplier for offensive damage
                if (statName == "Health" || statName == "Temperature")
                {
                    effectValue *= affinityMultiplier;
                }
            }
            // else: DEFENSIVE - apply positive value as-is (healing/buffs)

            // Apply to target
            results.Add(new EffectResult
            {
                StatName = statName,
                Change = effectValue,
                AppliedToAttacker = false,
                Source = itemTypeName
            });
        }
    }

    /// <summary>
    /// Calculate affinity multiplier from attacker affinity vs defender affinity.
    /// EXACT COPY of BattleUI.cs CalculateAffinityMultiplier method (lines 963-991).
    /// Returns 1.5 for strong matchup, 0.5 for weak matchup, NeutralMultiplier for neutral
    /// </summary>
    public static float CalculateAffinityMultiplier(string? attackerAffinity, string? defenderAffinity, IWorld world)
    {
        // No bonus if either affinity is null
        if (string.IsNullOrEmpty(attackerAffinity) || string.IsNullOrEmpty(defenderAffinity))
            return 1.0f;

        // Same affinity = neutral (use NeutralMultiplier if available)
        if (attackerAffinity == defenderAffinity)
        {
            var sameAffinityData = world?.Gameplay?.CharacterAffinities?
                .FirstOrDefault(a => a.RefName == attackerAffinity);
            return sameAffinityData?.NeutralMultiplier ?? 1.0f;
        }

        // Look up matchup in world data
        if (world?.Gameplay?.CharacterAffinities == null)
            return 1.0f;

        var attackerAffinityData = world.Gameplay.CharacterAffinities
            .FirstOrDefault(a => a.RefName == attackerAffinity);

        if (attackerAffinityData?.Matchup == null)
            return attackerAffinityData?.NeutralMultiplier ?? 1.0f;

        var matchup = attackerAffinityData.Matchup
            .FirstOrDefault(m => m.TargetAffinityRef == defenderAffinity);

        if (matchup != null)
            return matchup.Multiplier;

        // No matchup defined = use attacker's NeutralMultiplier
        return attackerAffinityData.NeutralMultiplier;
    }

    /// <summary>
    /// Get display name for affinity (for logging/UI).
    /// EXACT COPY of BattleUI.cs GetAffinityDisplayName method (lines 996-1008).
    /// </summary>
    public static string GetAffinityDisplayName(string? affinityRef, IWorld world)
    {
        if (string.IsNullOrEmpty(affinityRef))
            return "None";

        if (world?.Gameplay?.CharacterAffinities == null)
            return affinityRef;

        var affinityData = world.Gameplay.CharacterAffinities
            .FirstOrDefault(a => a.RefName == affinityRef);

        return affinityData?.DisplayName ?? affinityRef;
    }
}
