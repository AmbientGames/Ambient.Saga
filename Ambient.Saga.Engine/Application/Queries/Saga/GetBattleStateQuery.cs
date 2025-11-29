using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Queries.Saga;

/// <summary>
/// Gets the current battle state for a character interaction.
/// Replays battle transactions to reconstruct combatant states, turn number, and battle status.
/// </summary>
public record GetBattleStateQuery : IRequest<BattleStateResult>
{
    public required Guid AvatarId { get; init; }
    public required string SagaRef { get; init; }
    public required Guid BattleInstanceId { get; init; }
    public required AvatarEntity Avatar { get; init; }
}
