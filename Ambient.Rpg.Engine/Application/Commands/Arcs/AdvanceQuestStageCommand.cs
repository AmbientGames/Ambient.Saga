using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

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
public record AdvanceQuestStageCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar progressing the quest
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the quest
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Quest being advanced
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// Avatar entity (for state updates)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
