using Ambient.Domain;
using MediatR;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Battle;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to submit a player's defensive reaction during the reaction phase.
/// Part of the Expedition 33-inspired active defense system.
/// </summary>
public class SubmitReactionCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar submitting the reaction
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga arc reference
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Battle instance ID
    /// </summary>
    public required Guid BattleInstanceId { get; init; }

    /// <summary>
    /// The player's chosen defensive reaction
    /// </summary>
    public required PlayerDefenseType Reaction { get; init; }

    /// <summary>
    /// Avatar entity for updates
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
