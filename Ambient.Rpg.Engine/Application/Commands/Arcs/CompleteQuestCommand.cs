using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to complete a quest and claim rewards.
///
/// Side Effects:
/// - Creates QuestCompleted transaction
/// - Removes quest from avatar's active quest log
/// - Awards quest rewards (items, currency, experience)
/// - Persists updated avatar state
/// </summary>
public record CompleteQuestCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar completing the quest
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc where quest is being turned in
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Quest being completed
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// NPC accepting the completion (for tracking)
    /// </summary>
    public required string QuestReceiverRef { get; init; }

    /// <summary>
    /// Avatar entity completing the quest (for state updates and persistence)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }

    /// <summary>
    /// When true, skips stage completion check. Used for dialogue-driven quest completion
    /// where the dialogue author explicitly triggers completion via CompleteQuest action.
    /// </summary>
    public bool DialogueDriven { get; init; }
}
