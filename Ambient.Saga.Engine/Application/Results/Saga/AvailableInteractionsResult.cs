using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Application.Results.Saga;

/// <summary>
/// Complete snapshot of what the avatar can interact with at their current position.
/// This is a read-only view derived from the transaction log.
///
/// Client Usage:
/// - Query this whenever you need to update UI
/// - Shows what's available, not what happened (that's in command results)
/// - All "Can*" flags tell you if interaction is allowed right now
/// </summary>
public class AvailableInteractionsResult
{
    /// <summary>
    /// Characters currently spawned and alive that the avatar can interact with
    /// </summary>
    public List<InteractableCharacter> NearbyCharacters { get; set; } = new();

    /// <summary>
    /// Triggers currently active at this position
    /// </summary>
    public List<ActiveTriggerInfo> ActiveTriggers { get; set; } = new();

    /// <summary>
    /// Whether the Saga itself has been discovered
    /// </summary>
    public bool SagaDiscovered { get; set; }

    /// <summary>
    /// Current Saga status
    /// </summary>
    public SagaStatus SagaStatus { get; set; }
}

/// <summary>
/// A character the avatar can potentially interact with
/// </summary>
public class InteractableCharacter
{
    /// <summary>
    /// Instance ID for this specific spawned character
    /// </summary>
    public Guid CharacterInstanceId { get; set; }

    /// <summary>
    /// Reference to character template
    /// </summary>
    public string CharacterRef { get; set; } = string.Empty;

    /// <summary>
    /// Display name from template
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Character type (from template Interactable)
    /// </summary>
    public string? CharacterType { get; set; }

    /// <summary>
    /// Current character state
    /// </summary>
    public CharacterState State { get; set; } = new();

    /// <summary>
    /// Available interaction options
    /// </summary>
    public CharacterInteractionOptions Options { get; set; } = new();
}

/// <summary>
/// What you can do with a character
/// </summary>
public class CharacterInteractionOptions
{
    /// <summary>
    /// Can start dialogue with this character
    /// </summary>
    public bool CanDialogue { get; set; }

    /// <summary>
    /// Dialogue tree to use (if CanDialogue is true)
    /// </summary>
    public string? DialogueTreeRef { get; set; }

    /// <summary>
    /// Can trade with this character
    /// </summary>
    public bool CanTrade { get; set; }

    /// <summary>
    /// Can attack this character
    /// </summary>
    public bool CanAttack { get; set; }

    /// <summary>
    /// Can loot this character (usually only if defeated and not yet looted)
    /// </summary>
    public bool CanLoot { get; set; }

    /// <summary>
    /// Why interactions are blocked (if any are false)
    /// </summary>
    public string? BlockedReason { get; set; }
}

/// <summary>
/// Information about an active trigger
/// </summary>
public class ActiveTriggerInfo
{
    /// <summary>
    /// Trigger reference
    /// </summary>
    public string TriggerRef { get; set; } = string.Empty;

    /// <summary>
    /// Trigger status
    /// </summary>
    public SagaTriggerStatus Status { get; set; }

    /// <summary>
    /// Distance from Saga center
    /// </summary>
    public double DistanceFromCenter { get; set; }

    /// <summary>
    /// Whether avatar is within this trigger's radius
    /// </summary>
    public bool IsWithinRadius { get; set; }
}
