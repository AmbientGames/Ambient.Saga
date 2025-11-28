using Ambient.SagaEngine.Domain.Rpg.Battle;

namespace Ambient.SagaEngine.Application.Results.Saga;

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
    /// Current battle state (PlayerTurn, EnemyTurn, Victory, Defeat, Fled)
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
    /// Player combatant current state
    /// </summary>
    public Combatant? PlayerCombatant { get; set; }

    /// <summary>
    /// Enemy combatant current state
    /// </summary>
    public Combatant? EnemyCombatant { get; set; }

    /// <summary>
    /// Battle log messages (reconstructed from transactions)
    /// </summary>
    public List<string> BattleLog { get; set; } = new();

    /// <summary>
    /// Whether player won the battle
    /// </summary>
    public bool? PlayerVictory { get; set; }

    /// <summary>
    /// Whether battle has ended
    /// </summary>
    public bool HasEnded { get; set; }

    /// <summary>
    /// Player's available affinities for switching
    /// </summary>
    public List<string> PlayerAffinityRefs { get; set; } = new();

    /// <summary>
    /// Enemy character instance ID
    /// </summary>
    public Guid EnemyCharacterInstanceId { get; set; }

    /// <summary>
    /// Error message if query failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}
