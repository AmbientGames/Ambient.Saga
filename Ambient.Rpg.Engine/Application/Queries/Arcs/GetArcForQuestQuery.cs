using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Query to find which Arc contains a specific quest for an avatar.
/// Searches through all arcs to find the one where this quest is either active or completed.
/// </summary>
public record GetArcForQuestQuery : IRequest<string?>
{
    /// <summary>
    /// Avatar whose quest state to check
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Quest to find the parent Arc for
    /// </summary>
    public required string QuestRef { get; init; }
}
