using Ambient.Domain.Entities;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Domain.Rpg.Battle;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to start an interactive battle with a character.
///
/// Side Effects:
/// - Creates BattleStarted transaction (with equipment/affinity snapshot)
/// - Initializes battle state for turn-by-turn execution
/// - Does NOT execute any turns (use ExecuteBattleTurnCommand for that)
/// </summary>
public record StartBattleCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar participating in battle
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the battle
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Character instance being fought
    /// </summary>
    public required Guid EnemyCharacterInstanceId { get; init; }

    /// <summary>
    /// Player's combatant configuration
    /// </summary>
    public required Combatant PlayerCombatant { get; init; }

    /// <summary>
    /// Enemy's combatant configuration
    /// </summary>
    public required Combatant EnemyCombatant { get; init; }

    /// <summary>
    /// Player's selected affinities (for switching during battle)
    /// </summary>
    public required List<string> PlayerAffinityRefs { get; init; }

    /// <summary>
    /// Enemy AI (must be created with same random seed for determinism)
    /// </summary>
    public required ICombatAI EnemyMind { get; init; }

    /// <summary>
    /// Random seed for deterministic battle replay
    /// </summary>
    public required int RandomSeed { get; init; }

    /// <summary>
    /// Avatar entity participating in battle (for state updates)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
