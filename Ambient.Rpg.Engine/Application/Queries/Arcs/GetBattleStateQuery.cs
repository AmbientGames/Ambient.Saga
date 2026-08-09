using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Gets the current battle state for a character interaction.
/// Replays battle transactions to reconstruct combatant states, turn number, and battle status.
/// </summary>
public record GetBattleStateQuery : IRequest<BattleStateResult>
{
    public required Guid AvatarId { get; init; }
    public required string ArcRef { get; init; }
    public required Guid BattleInstanceId { get; init; }
    public required AvatarEntity Avatar { get; init; }
}
