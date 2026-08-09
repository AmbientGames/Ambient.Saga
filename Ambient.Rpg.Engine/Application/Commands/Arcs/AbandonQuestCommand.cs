using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to abandon an active quest.
///
/// Side Effects:
/// - Creates QuestAbandoned transaction
/// - Removes quest from avatar's active quest log
/// - Records quest abandonment in transaction history
/// </summary>
public record AbandonQuestCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar abandoning the quest
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the quest
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Quest being abandoned
    /// </summary>
    public required string QuestRef { get; init; }

    /// <summary>
    /// Avatar entity abandoning the quest (for state updates and persistence)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
