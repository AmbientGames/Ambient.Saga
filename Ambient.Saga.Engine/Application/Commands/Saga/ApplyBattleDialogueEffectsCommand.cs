using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Applies the mechanical effects of mid-battle dialogue (boss taunts and parley:
/// HealSelf, ChangeStance, ChangeAffinity, EndBattle actions) to the running battle.
/// The effects are recorded as a battle turn transaction so replay and state
/// reconstruction see them; EndBattle records the battle's conclusion.
/// </summary>
public class ApplyBattleDialogueEffectsCommand : IRequest<SagaCommandResult>
{
    public required Guid AvatarId { get; init; }
    public required string SagaArcRef { get; init; }
    public required Guid BattleInstanceId { get; init; }
    public required AvatarEntity Avatar { get; init; }

    /// <summary>Normalized (0-1) healing applied to the enemy (HealSelf actions; content authors percent).</summary>
    public float EnemyHealAmount { get; init; }

    /// <summary>New stance for the enemy, if a ChangeStance action fired.</summary>
    public string? EnemyStanceRef { get; init; }

    /// <summary>New affinity for the enemy, if a ChangeAffinity action fired.</summary>
    public string? EnemyAffinityRef { get; init; }

    /// <summary>Battle conclusion from an EndBattle action: "Victory", "Defeat", "Flee"/"Draw" (truce), or null.</summary>
    public string? EndBattleResult { get; init; }
}
