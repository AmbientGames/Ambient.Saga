using System;
using System.Linq;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Battle;

namespace Ambient.Saga.Engine;

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

        // Demo: Change equipment every other turn (turn 2, 4, 6...)
        if (_turnCount % 2 == 0 && me.Capabilities?.Equipment != null)
        {
            // Find all available right-hand weapons
            var rightHandWeapons = new List<string>();
            foreach (var entry in me.Capabilities.Equipment)
            {
                var equipment = _world.GetEquipmentByRefName(entry.EquipmentRef);
                if (equipment != null && equipment.SlotRef == "RightHand")
                {
                    rightHandWeapons.Add(entry.EquipmentRef);
                }
            }

            // If we have at least 2 weapons, change to a different one
            if (rightHandWeapons.Count > 1)
            {
                // Cycle through weapons
                var currentIndex = _turnCount / 2 - 1; // Turn 2 -> index 0, turn 4 -> index 1, etc.
                var weaponRef = rightHandWeapons[currentIndex % rightHandWeapons.Count];

                return new CombatAction
                {
                    ActionType = ActionType.ChangeLoadout,
                    Parameter = weaponRef
                };
            }
        }

        // 1. Emergency: Use healing consumable if critically low on health
        if (IsLowHealth(me) && me.Capabilities?.Consumables != null)
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
        if (IsLowHealth(me))
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

    private bool IsLowHealth(Combatant combatant)
    {
        return combatant.HealthPercent < LOW_HEALTH_THRESHOLD * 100f;
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
