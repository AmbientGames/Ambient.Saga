using Ambient.Domain.Entities;
using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to record progress on a quest objective.
///
/// This is called automatically when the system detects an objective has advanced
/// (e.g., player defeats a dragon, completes dialogue, collects item).
///
/// The command checks if the objective threshold has been met, and if so,
/// creates a QuestObjectiveCompleted transaction.
///
/// Side Effects:
/// - Creates QuestObjectiveCompleted transaction if threshold met
/// - May trigger QuestStageAdvanced if all stage objectives complete
/// - Tracks objective progress for UI display
/// </summary>
public record ProgressQuestObjectiveCommand : IRequest<SagaCommandResult>
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
    /// Quest being progressed
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// Stage containing the objective
    /// </summary>
    public required string StageRef { get; init; }

    /// <summary>
    /// Objective being progressed
    /// </summary>
    public required string ObjectiveRef { get; init; }

    /// <summary>
    /// Avatar entity (for state updates)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
