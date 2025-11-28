using Ambient.Domain.Entities;
using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to abandon an active quest.
///
/// Side Effects:
/// - Creates QuestAbandoned transaction
/// - Removes quest from avatar's active quest log
/// - Records quest abandonment in transaction history
/// </summary>
public record AbandonQuestCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar abandoning the quest
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the quest
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Quest being abandoned
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// Avatar entity abandoning the quest (for state updates and persistence)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
