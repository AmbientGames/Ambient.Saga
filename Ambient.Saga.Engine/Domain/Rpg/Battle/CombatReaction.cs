using Ambient.Domain;

namespace Ambient.Saga.Engine.Domain.Rpg.Battle;

/// <summary>
/// Defense reactions available to players during enemy attack telegraphs.
/// Inspired by Expedition 33's active defense mechanics adapted for text-based play.
/// Named PlayerDefenseType to avoid conflict with XSD-generated PlayerDefenseType.
/// </summary>
public enum PlayerDefenseType
{
    /// <summary>Active dodging - best against lunges, charges, targeted attacks</summary>
    Dodge,
    /// <summary>Shield/arm block - best against physical strikes, projectiles</summary>
    Block,
    /// <summary>Timed deflection - best against weapon attacks, enables counter</summary>
    Parry,
    /// <summary>Hunker down - best against AoE, breath attacks, magic</summary>
    Brace,
    /// <summary>No reaction (ran out of time or chose not to react)</summary>
    None
}

/// <summary>
/// Attack pattern types that determine tells and optimal defenses.
/// </summary>
public enum AttackPatternType
{
    // Physical melee
    Slash,      // Horizontal/diagonal weapon swing
    Thrust,     // Forward stab/lunge
    Overhead,   // Vertical downward strike
    Sweep,      // Low attack at legs
    Flurry,     // Rapid multi-hit combo

    // Physical ranged
    Projectile, // Arrow, thrown weapon
    Charge,     // Rush/tackle attack

    // Creature attacks
    Bite,       // Jaw attack
    Claw,       // Slashing appendage
    Tail,       // Tail whip/slam
    Breath,     // Breath weapon (fire, frost, etc.)
    Spit,       // Ranged creature attack

    // Magic
    Bolt,       // Direct targeted spell
    Wave,       // Cone/area spell
    Burst,      // AoE centered spell
    Drain,      // Life/mana drain

    // Special
    Grapple,    // Grab/hold attack
    Feint       // Deceptive attack
}

/// <summary>
/// Outcome modifiers based on player's defense choice.
/// </summary>
public class DefenseOutcome
{
    public PlayerDefenseType Reaction { get; init; }
    /// <summary>Damage multiplier (0.0 = no damage, 1.0 = full damage)</summary>
    public float DamageMultiplier { get; init; } = 1.0f;
    /// <summary>Stat effects applied for successful defense (e.g., Stamina recovery)</summary>
    public CharacterEffects? Effects { get; init; }
    /// <summary>Counter-attack enabled</summary>
    public bool EnablesCounter { get; init; }
    /// <summary>Counter damage multiplier (if EnablesCounter is true)</summary>
    public float CounterMultiplier { get; init; } = 0.5f;
    /// <summary>Prevents status effect application</summary>
    public bool PreventsStatusEffect { get; init; }
    /// <summary>Narrative response text</summary>
    public string ResponseText { get; init; } = string.Empty;
}

/// <summary>
/// An attack tell - the narrative preview shown to the player
/// and the defense matrix that determines outcomes.
/// </summary>
public class AttackTell
{
    public required string RefName { get; init; }
    public required AttackPatternType Pattern { get; init; }
    /// <summary>Narrative text shown to player (the "tell")</summary>
    public required string TellText { get; init; }
    /// <summary>Time window in milliseconds for player to react (0 = no time limit)</summary>
    public int ReactionWindowMs { get; init; } = 3000;
    /// <summary>The optimal defense reaction for this attack</summary>
    public required PlayerDefenseType OptimalDefense { get; init; }
    /// <summary>Secondary good defense (partial success)</summary>
    public PlayerDefenseType? SecondaryDefense { get; init; }
    /// <summary>Weapon categories this tell applies to (space-separated, e.g., "OneHanded TwoHanded")</summary>
    public string? WeaponCategories { get; init; }
    /// <summary>Outcomes for each defense reaction type</summary>
    public required Dictionary<PlayerDefenseType, DefenseOutcome> Outcomes { get; init; }

    /// <summary>
    /// Get the outcome for a specific defense reaction.
    /// Falls back to None outcome if reaction not found.
    /// </summary>
    public DefenseOutcome GetOutcome(PlayerDefenseType reaction)
    {
        if (Outcomes.TryGetValue(reaction, out var outcome))
            return outcome;

        // Fallback to None outcome or default
        if (Outcomes.TryGetValue(PlayerDefenseType.None, out var noneOutcome))
            return noneOutcome;

        return new DefenseOutcome
        {
            Reaction = reaction,
            DamageMultiplier = 1.0f,
            ResponseText = "You fail to react in time!"
        };
    }

    /// <summary>
    /// Check if this tell is compatible with the given weapon category.
    /// Returns true if WeaponCategories is null/empty (universal) or contains the category.
    /// </summary>
    public bool IsCompatibleWithWeapon(string? weaponCategory)
    {
        // No restriction means compatible with all weapons
        if (string.IsNullOrWhiteSpace(WeaponCategories))
            return true;

        // No weapon means only universal tells are compatible
        if (string.IsNullOrWhiteSpace(weaponCategory))
            return false;

        // Check if any of the weapon categories match
        var categories = WeaponCategories.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return categories.Any(c => c.Equals(weaponCategory, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Represents an incoming attack that the player can react to.
/// Used as the "pending attack" state during the reaction phase.
/// </summary>
public class PendingAttack
{
    public required Combatant Attacker { get; init; }
    public required Combatant Target { get; init; }
    public required AttackTell Tell { get; init; }
    public required int BaseDamage { get; init; }
    public DateTime TellShownAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Check if the reaction window has expired.
    /// </summary>
    public bool IsExpired => Tell.ReactionWindowMs > 0 &&
        (DateTime.UtcNow - TellShownAt).TotalMilliseconds > Tell.ReactionWindowMs;

    /// <summary>
    /// Milliseconds remaining in the reaction window.
    /// </summary>
    public int RemainingMs => Tell.ReactionWindowMs > 0
        ? Math.Max(0, Tell.ReactionWindowMs - (int)(DateTime.UtcNow - TellShownAt).TotalMilliseconds)
        : int.MaxValue;
}

/// <summary>
/// Result of resolving a combat reaction.
/// </summary>
public class ReactionResult
{
    public required PlayerDefenseType ChosenReaction { get; init; }
    public required DefenseOutcome Outcome { get; init; }
    public required int FinalDamage { get; init; }
    public required string NarrativeText { get; init; }
    public int? CounterDamage { get; init; }
    /// <summary>Effects applied from skilled defense (if any)</summary>
    public CharacterEffects? EffectsApplied { get; init; }
    /// <summary>Actual stamina gained from defense effects (may be less if already full)</summary>
    public float StaminaGained { get; init; }
    public bool WasOptimal { get; init; }
    public bool WasSecondary { get; init; }
    public bool TimedOut { get; init; }
}
