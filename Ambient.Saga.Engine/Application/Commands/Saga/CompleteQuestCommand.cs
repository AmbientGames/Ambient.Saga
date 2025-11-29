using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to complete a quest and claim rewards.
///
/// Side Effects:
/// - Creates QuestCompleted transaction
/// - Removes quest from avatar's active quest log
/// - Awards quest rewards (items, currency, experience)
/// - Persists updated avatar state
/// </summary>
public record CompleteQuestCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar completing the quest
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga where quest is being turned in
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Quest being completed
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// Quest signpost or NPC accepting the completion (for tracking)
    /// </summary>
    public required string QuestReceiverRef { get; init; }

    /// <summary>
    /// Avatar entity completing the quest (for state updates and persistence)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
