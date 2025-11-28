using Ambient.Domain.Entities;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Domain.Rpg.Battle;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to execute ONE turn in an interactive battle.
/// Similar to SelectDialogueChoiceCommand - processes player action and creates transaction.
///
/// Side Effects:
/// - Creates BattleTurnExecuted transaction for player turn
/// - Creates BattleTurnExecuted transaction for enemy turn (if battle continues)
/// - Creates BattleEnded transaction if battle finishes
/// - Creates CharacterDefeated transaction if enemy is defeated
/// - Updates avatar state (health, energy, equipment condition) if battle ends
/// </summary>
public record ExecuteBattleTurnCommand : IRequest<SagaCommandResult>
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
    /// Battle instance ID (from BattleStarted transaction)
    /// </summary>
    public required Guid BattleInstanceId { get; init; }

    /// <summary>
    /// Player's combat action for this turn
    /// </summary>
    public required CombatAction PlayerAction { get; init; }

    /// <summary>
    /// Avatar entity participating in battle (for state updates if battle ends)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
