using Ambient.Domain;

namespace Ambient.Saga.Engine.Domain.Rpg.Battle;

/// <summary>
/// Represents a combatant in battle with their current stats.
/// Framework-agnostic - can be populated from AvatarBase, Character, or any entity.
/// </summary>
public class Combatant
{
    // Normalized stat range (all stats are 0-1)
    public const float MAX_STAT = 1.0f;

    public string RefName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    // Current state (0-1 normalized range)
    public float Health { get; set; }
    public float Energy { get; set; }

    // Combat stats (0-1 normalized range)
    public float Strength { get; init; }
    public float Defense { get; init; }
    public float Speed { get; init; }
    public float Magic { get; init; }

    // Affinity for elemental bonuses (settable for testing/runtime changes)
    public string? AffinityRef { get; set; }

    // Reference to capabilities for spell/equipment access
    public ItemCollection? Capabilities { get; init; }

    // Currently equipped items: slot name (e.g., "Head", "RightHand") -> equipment RefName
    public Dictionary<string, string> CombatProfile { get; set; } = new();

    // Combat state
    public bool IsDefending { get; set; }
    public bool IsAdjusting { get; set; }  // Quick defensive positioning (15% damage reduction)

    // Active status effects (tracked during battle)
    public List<ActiveStatusEffect> ActiveStatusEffects { get; set; } = new();

    // Helper properties
    public bool IsAlive => Health > 0;
    public float HealthPercent => Health / MAX_STAT * 100f;
    public float EnergyPercent => Energy / MAX_STAT * 100f;

    /// <summary>
    /// Create a combatant from an AvatarBase.
    /// </summary>
    public static Combatant FromAvatar(AvatarBase avatar)
    {
        if (avatar.Stats == null)
            throw new ArgumentException("Avatar must have Stats defined", nameof(avatar));

        return new Combatant
        {
            RefName = avatar.ArchetypeRef ?? "Player",
            DisplayName = avatar.ArchetypeRef ?? "Player",
            Health = avatar.Stats.Health,
            Energy = avatar.Stats.Stamina,
            Strength = avatar.Stats.Strength,
            Defense = avatar.Stats.Defense,
            Speed = avatar.Stats.Speed,
            Magic = avatar.Stats.Magic,
            AffinityRef = avatar.AffinityRef,
            Capabilities = avatar.Capabilities,
            CombatProfile = avatar.CombatProfile != null
                ? new Dictionary<string, string>(avatar.CombatProfile)
                : new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// Create a combatant from a Character (boss/enemy).
    /// </summary>
    public static Combatant FromCharacter(Character character)
    {
        if (character.Stats == null)
            throw new ArgumentException("Character must have Stats defined", nameof(character));

        return new Combatant
        {
            RefName = character.RefName,
            DisplayName = character.DisplayName,
            Health = character.Stats.Health,
            Energy = character.Stats.Stamina,
            Strength = character.Stats.Strength,
            Defense = character.Stats.Defense,
            Speed = character.Stats.Speed,
            Magic = character.Stats.Magic,
            AffinityRef = character.AffinityRef,
            Capabilities = character.Capabilities,
            CombatProfile = character.CombatProfile != null
                ? new Dictionary<string, string>(character.CombatProfile)
                : new Dictionary<string, string>()
        };
    }
}

/// <summary>
/// Represents an active status effect applied to a combatant during battle.
/// Tracks duration and stacks for turn-based progression.
/// </summary>
public class ActiveStatusEffect
{
    /// <summary>
    /// Reference to the status effect definition
    /// </summary>
    public required string StatusEffectRef { get; init; }

    /// <summary>
    /// Remaining duration in turns (decrements each turn)
    /// </summary>
    public int RemainingTurns { get; set; }

    /// <summary>
    /// Number of stacks currently applied (for stackable effects)
    /// </summary>
    public int Stacks { get; set; } = 1;

    /// <summary>
    /// Turn number when this effect was applied (for tracking)
    /// </summary>
    public int AppliedOnTurn { get; init; }
}
