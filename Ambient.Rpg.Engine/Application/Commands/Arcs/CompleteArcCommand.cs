using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to mark an arc as completed (all objectives done, quest finished).
///
/// Side Effects:
/// - Creates ArcCompleted transaction
/// - Tracks completion for achievements
/// - May trigger cleanup/despawn logic
/// </summary>
public record CompleteArcCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar completing the arc
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc being completed
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Completion method (for tracking/achievements)
    /// </summary>
    public string? CompletionMethod { get; init; }
}
