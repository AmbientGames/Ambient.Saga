using Ambient.Rpg.Engine.Domain.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Query to get all triggers within range at a given position.
/// Returns proximity info for all triggers (active, inactive, completed).
/// </summary>
public record GetTriggersInRangeQuery : IRequest<List<ArcTriggerProximityInfo>>
{
    /// <summary>
    /// Avatar checking triggers
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc to check
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Avatar position in Arc-relative coordinates (X, Z)
    /// </summary>
    public required double AvatarX { get; init; }
    public required double AvatarZ { get; init; }
}
