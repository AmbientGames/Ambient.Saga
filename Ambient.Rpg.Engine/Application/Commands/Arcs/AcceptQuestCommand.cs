using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to accept a quest offered by an NPC quest giver.
///
/// Side Effects:
/// - Creates QuestAccepted transaction
/// - Adds quest to avatar's active quest log
/// - Tracks where/when quest was accepted for audit trail
/// </summary>
public record AcceptQuestCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar accepting the quest
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the quest giver NPC
    /// </summary>
    public required string ArcRef { get; init; }

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
