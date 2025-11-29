using MediatR;

namespace Ambient.Saga.Engine.Application.Queries.Saga;

/// <summary>
/// Query to find which Saga contains a specific quest for an avatar.
/// Searches through all sagas to find the one where this quest is either active or completed.
/// </summary>
public record GetSagaForQuestQuery : IRequest<string?>
{
    /// <summary>
    /// Avatar whose quest state to check
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Quest to find the parent Saga for
    /// </summary>
    public required string QuestRef { get; init; }
}
