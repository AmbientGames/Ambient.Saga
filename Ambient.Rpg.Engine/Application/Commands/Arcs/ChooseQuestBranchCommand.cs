using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to choose a branch in a quest stage with branching paths.
///
/// Branching stages present the avatar with multiple choices that affect
/// the quest progression. For exclusive branches (default), only one
/// branch can be chosen.
///
/// Side Effects:
/// - Creates QuestBranchChosen transaction
/// - Records ChosenBranch in QuestState
/// - Advances quest to the branch's NextStage
/// </summary>
public record ChooseQuestBranchCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar making the branch choice
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the quest
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Quest with branching stage
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// Stage containing the branches
    /// </summary>
    public required string StageRef { get; init; }

    /// <summary>
    /// Branch being chosen
    /// </summary>
    public required string BranchRef { get; init; }

    /// <summary>
    /// Avatar entity (for state updates)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
