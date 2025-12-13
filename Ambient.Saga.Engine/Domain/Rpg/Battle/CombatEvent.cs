namespace Ambient.Saga.Engine.Domain.Rpg.Battle;

/// <summary>
/// Actions that can be taken during combat.
/// </summary>
public enum BattleActionType
{
    Attack,
    Defend,
    Flee,
    UseItem,
    SpecialAttack,
    AdjustLoadout,
    ChangeLoadout
}

/// <summary>
/// Represents a combat action with its result.
/// </summary>
public class CombatEvent
{
    public BattleActionType ActionType { get; init; }
    public string ActorName { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public float Damage { get; init; }
    public float Healing { get; init; }
    public bool IsCritical { get; init; }
    public bool IsDefending { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    // Additional data for transaction logging
    public int TurnNumber { get; init; }
    public ActionType DecisionType { get; init; }
    public string? ItemRefName { get; init; }  // Weapon/spell/consumable used
    public bool IsPlayerTurn { get; init; }
    public float TargetHealthAfter { get; init; }
    public float ActorEnergyAfter { get; init; }
    public string? EquipmentChanged { get; init; }  // For ChangeLoadout actions
    public string? AffinityChanged { get; init; }    // For affinity switches
    public string? StatusEffectApplied { get; init; }  // Status effect applied during this action

    // Trait assignment from combat outcomes (flee, spare, etc.)
    /// <summary>Trait to assign as result of this action (e.g., Disengaged on successful flee)</summary>
    public string? TraitToAssign { get; init; }
    /// <summary>Character ref that receives the trait (enemy when player flees)</summary>
    public string? TraitTargetCharacterRef { get; init; }
}
