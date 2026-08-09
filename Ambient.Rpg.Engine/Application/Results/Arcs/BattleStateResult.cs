using Ambient.Rpg.Engine.Domain.Battle;

namespace Ambient.Rpg.Engine.Application.Results.Arcs;

/// <summary>
/// Current state of a battle interaction.
/// Derived from transaction log replay.
/// </summary>
public class BattleStateResult
{
    /// <summary>
    /// Whether battle is active
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Current battle state (AvatarTurn, EnemyTurn, Victory, Defeat, Fled)
    /// </summary>
    public BattleState BattleState { get; set; }

    /// <summary>
    /// Battle instance ID (from BattleStarted transaction)
    /// </summary>
    public Guid BattleInstanceId { get; set; }

    /// <summary>
    /// Current turn number
    /// </summary>
    public int TurnNumber { get; set; }

    /// <summary>
    /// Avatar combatant current state
    /// </summary>
    public Combatant? AvatarCombatant { get; set; }

    /// <summary>
    /// Enemy combatant current state
    /// </summary>
    public Combatant? EnemyCombatant { get; set; }

    /// <summary>
    /// Companion combatants' current state (empty when the party has none)
    /// </summary>
    public List<Combatant> Companions { get; set; } = new();

    /// <summary>
    /// When <see cref="BattleState"/> is AwaitingReaction: the telegraphed attack's
    /// tell ref, so a resuming UI can re-show the telegraph.
    /// </summary>
    public string? PendingTellRefName { get; set; }

    /// <summary>Base damage of the pending telegraphed attack (AwaitingReaction only).</summary>
    public float PendingTellBaseDamage { get; set; }

    /// <summary>Authored reaction window in milliseconds (AwaitingReaction only).</summary>
    public int PendingTellReactionWindowMs { get; set; }

    /// <summary>UTC timestamp the tell was issued (AwaitingReaction only).</summary>
    public DateTime? PendingTellIssuedAtUtc { get; set; }

    /// <summary>
    /// Battle log messages (reconstructed from transactions)
    /// </summary>
    public List<string> BattleLog { get; set; } = new();

    /// <summary>
    /// Whether avatar won the battle
    /// </summary>
    public bool? AvatarVictory { get; set; }

    /// <summary>
    /// Whether battle has ended
    /// </summary>
    public bool HasEnded { get; set; }

    /// <summary>
    /// Avatar's available affinities for switching
    /// </summary>
    public List<string> AvatarAffinityRefs { get; set; } = new();

    /// <summary>
    /// Enemy character instance ID
    /// </summary>
    public Guid EnemyCharacterInstanceId { get; set; }

    /// <summary>
    /// Error message if query failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    // Reaction phase fields (Expedition 33-inspired active defense)

    /// <summary>
    /// Whether battle is awaiting avatar reaction to an incoming attack
    /// </summary>
    public bool IsAwaitingReaction { get; set; }

    /// <summary>
    /// The narrative "tell" text for the pending attack (e.g., "The goblin lunges forward...")
    /// </summary>
    public string? PendingTellText { get; set; }

    /// <summary>
    /// Time remaining in milliseconds for avatar to react
    /// </summary>
    public int PendingReactionWindowMs { get; set; }

    /// <summary>
    /// The attack tell reference name (for looking up optimal defense)
    /// </summary>
    public string? PendingTellRef { get; set; }
}
