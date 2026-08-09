using System;
using System.Linq;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain.Battle;

namespace Ambient.Rpg.Engine;

/// <summary>
/// Battle AI implementation for opponent decision-making.
/// Simple reference implementation demonstrating basic tactical decision-making.
/// </summary>
public class CombatAI : ICombatAI
{
    private readonly Random _random;
    private readonly IWorld _world;
    private const float LOW_HEALTH_THRESHOLD = 0.3f;  // 30% health
    private const float LOW_ENERGY_THRESHOLD = 0.2f;  // 20% energy
    private int _turnCount = 0;  // Track turns for equipment change demo

    public CombatAI(IWorld world, int? randomSeed = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
    }

    public CombatAction DecideTurn(BattleView state)
    {
        _turnCount++;
        var me = state.Self;
        var opponent = state.Opponent;

        // Battle-behavior traits authored on the character template (Dialogue.xsd
        // CharacterTraitType). Only AI-driven characters resolve to a template —
        // an avatar's RefName is its archetype, which is not in CharactersLookup.
        _world.CharactersLookup.TryGetValue(me.RefName, out var template);

        // LastStand: "Health % that triggers desperate all-out attack". Checked
        // before everything else — a cornered fighter stops healing, defending,
        // swapping gear, or trying to run.
        var lastStand = GetTraitValue(template, CharacterTraitType.LastStand);
        if (lastStand is > 0 && me.HealthPercent <= lastStand)
        {
            return new CombatAction
            {
                ActionType = ActionType.Attack
            };
        }

        // FleeThreshold: "Health percentage at which the character will attempt to
        // flee combat" (0 = never flees). Companions share this AI but must never
        // flee — a companion flee would end the WHOLE battle as Fled. Companion
        // turns are the only ones whose opponent is itself a templated character:
        // the enemy always faces the avatar, whose RefName is an archetype.
        var fleeThreshold = GetTraitValue(template, CharacterTraitType.FleeThreshold);
        if (fleeThreshold is > 0 && me.HealthPercent <= fleeThreshold &&
            !_world.CharactersLookup.ContainsKey(opponent.RefName))
        {
            return new CombatAction
            {
                ActionType = ActionType.Flee
            };
        }

        // Aggression: "How aggressively the character fights (0-100, higher = more
        // aggressive)". Scales the low-health point where the AI turns defensive
        // (heal/defend): 50 keeps the default 30%, 100 never turtles, 0 turtles
        // from 60%. Characters without the trait keep the historical 30%.
        var lowHealthThresholdPct = LOW_HEALTH_THRESHOLD * 100f;
        var aggression = GetTraitValue(template, CharacterTraitType.Aggression);
        if (aggression.HasValue)
        {
            lowHealthThresholdPct *= Math.Clamp(100 - aggression.Value, 0, 200) / 50f;
        }

        // Demo: Change equipment every other turn (turn 2, 4, 6...)
        if (_turnCount % 2 == 0 && me.Capabilities?.Equipment != null)
        {
            // Find all available main-hand weapons
            var mainHandWeapons = new List<string>();
            foreach (var entry in me.Capabilities.Equipment)
            {
                var equipment = _world.GetEquipmentByRefName(entry.EquipmentRef);
                if (equipment != null && equipment.SlotRef == "MainHand")
                {
                    mainHandWeapons.Add(entry.EquipmentRef);
                }
            }

            // If we have at least 2 weapons, change to a different one
            if (mainHandWeapons.Count > 1)
            {
                // Cycle through weapons
                var currentIndex = _turnCount / 2 - 1; // Turn 2 -> index 0, turn 4 -> index 1, etc.
                var weaponRef = mainHandWeapons[currentIndex % mainHandWeapons.Count];

                // ExecuteChangeLoadout expects "Slot:Value" pairs — a bare weapon ref
                // used to fail every swap and wedge the enemy's turn
                return new CombatAction
                {
                    ActionType = ActionType.ChangeLoadout,
                    Parameter = $"MainHand:{weaponRef}"
                };
            }
        }

        // 1. Emergency: Use healing consumable if critically low on health
        if (me.HealthPercent < lowHealthThresholdPct && me.Capabilities?.Consumables != null)
        {
            var healingItemRef = FindHealingConsumable(me);
            if (healingItemRef != null)
            {
                return new CombatAction
                {
                    ActionType = ActionType.UseConsumable,
                    Parameter = healingItemRef
                };
            }
        }

        // 2. Defend if low health and no healing items available
        if (me.HealthPercent < lowHealthThresholdPct)
        {
            return new CombatAction
            {
                ActionType = ActionType.Defend
            };
        }

        // 3. Cast spell if we have spells and enough energy
        if (me.Capabilities?.Spells != null && me.Capabilities.Spells.Length > 0 && !IsLowEnergy(me))
        {
            var spellRef = PickBestSpell(me, opponent);
            if (spellRef != null)
            {
                return new CombatAction
                {
                    ActionType = ActionType.CastSpell,
                    Parameter = spellRef
                };
            }
        }

        // 5. Fallback: attack
        return new CombatAction
        {
            ActionType = ActionType.Attack
        };
    }

    /// <summary>
    /// Numeric value of a battle-behavior trait on the character template, or null
    /// when the character has no template or does not carry the trait. A trait
    /// element without a Value attribute (a boolean-style trait) yields null for
    /// these numeric-threshold semantics unless a non-zero value was set in code.
    /// </summary>
    private static int? GetTraitValue(Character? template, CharacterTraitType trait)
    {
        if (template?.Traits == null) return null;

        foreach (var t in template.Traits)
        {
            if (t.Name == trait)
            {
                return t.ValueSpecified || t.Value != 0 ? t.Value : (int?)null;
            }
        }

        return null;
    }

    private bool IsLowEnergy(Combatant combatant)
    {
        return combatant.EnergyPercent < LOW_ENERGY_THRESHOLD * 100f;
    }

    private string? FindHealingConsumable(Combatant combatant)
    {
        if (combatant.Capabilities?.Consumables == null) return null;

        foreach (var entry in combatant.Capabilities.Consumables)
        {
            if (entry.Quantity <= 0) continue;

            var consumable = _world.GetConsumableByRefName(entry.ConsumableRef);
            if (consumable == null) continue;

            // Check if this consumable heals (has positive Health effect)
            if (consumable.Effects?.Health > 0)
            {
                return entry.ConsumableRef;
            }
        }

        return null;
    }

    private string? PickBestSpell(Combatant me, Combatant opponent)
    {
        if (me.Capabilities?.Spells == null) return null;

        // Simple strategy: Pick first usable spell with good condition
        foreach (var entry in me.Capabilities.Spells)
        {
            if (entry.Condition < 0.2f) continue; // Don't use nearly broken spells

            var spell = _world.GetSpellByRefName(entry.SpellRef);
            if (spell != null)
            {
                return entry.SpellRef;
            }
        }

        return null;
    }
}
