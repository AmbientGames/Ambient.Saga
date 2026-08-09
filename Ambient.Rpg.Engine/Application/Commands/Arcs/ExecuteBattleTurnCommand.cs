using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Domain.Battle;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to execute ONE turn in an interactive battle.
/// Similar to SelectDialogueChoiceCommand - processes avatar action and creates transaction.
///
/// Side Effects:
/// - Creates BattleTurnExecuted transaction for avatar turn
/// - Creates BattleTurnExecuted transaction for enemy turn (if battle continues)
/// - Creates BattleEnded transaction if battle finishes
/// - Creates CharacterDefeated transaction if enemy is defeated
/// - Updates avatar state (health, energy, equipment condition) if battle ends
/// </summary>
public record ExecuteBattleTurnCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar participating in battle
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the battle
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Battle instance ID (from BattleStarted transaction)
    /// </summary>
    public required Guid BattleInstanceId { get; init; }

    /// <summary>
    /// Avatar's combat action for this turn
    /// </summary>
    public required CombatAction AvatarAction { get; init; }

    /// <summary>
    /// Avatar entity participating in battle (for state updates if battle ends)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
