using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to accept a quest offered by an NPC quest giver.
///
/// Side Effects:
/// - Creates QuestAccepted transaction
/// - Adds quest to avatar's active quest log
/// - Tracks where/when quest was accepted for audit trail
/// </summary>
public record AcceptQuestCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar accepting the quest
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the quest giver NPC
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Quest being accepted
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// NPC offering the quest (for tracking)
    /// </summary>
    public required string QuestGiverRef { get; init; }

    /// <summary>
    /// Avatar entity accepting the quest (for state updates and persistence)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
