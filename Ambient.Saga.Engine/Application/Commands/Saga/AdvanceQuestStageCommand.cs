using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to advance a quest to the next stage.
///
/// This is called after all required objectives in the current stage are complete.
/// For branching stages, this is called after a branch choice is made.
///
/// Side Effects:
/// - Creates QuestStageAdvanced transaction
/// - May trigger QuestCompleted if this was the final stage
/// - Updates CurrentStage in QuestState
/// </summary>
public record AdvanceQuestStageCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar progressing the quest
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the quest
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Quest being advanced
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// Avatar entity (for state updates)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
