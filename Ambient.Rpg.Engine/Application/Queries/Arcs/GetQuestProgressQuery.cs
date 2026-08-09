using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Query to get quest progress for a specific quest in an arc.
/// Uses transaction logs to compute stage/objective progress dynamically.
/// </summary>
public record GetQuestProgressQuery : IRequest<QuestProgressSnapshot?>
{
    /// <summary>
    /// Avatar whose quest progress to check
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the quest
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Quest to check progress for
    /// </summary>
    public required string QuestRef { get; init; }
}

/// <summary>
/// Query to get all active quests for an avatar.
/// </summary>
public record GetActiveQuestsQuery : IRequest<List<QuestProgressSnapshot>>
{
    /// <summary>
    /// Avatar whose quests to retrieve
    /// </summary>
    public required Guid AvatarId { get; init; }
}
