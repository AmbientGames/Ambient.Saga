using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using MediatR;

namespace Ambient.Saga.Engine.Application.Queries.Saga;

/// <summary>
/// Query to get all triggers within range at a given position.
/// Returns proximity info for all triggers (active, inactive, completed).
/// </summary>
public record GetTriggersInRangeQuery : IRequest<List<SagaTriggerProximityInfo>>
{
    /// <summary>
    /// Avatar checking triggers
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga to check
    /// </summary>
    public required string SagaRef { get; init; }

    /// <summary>
    /// Avatar position in Saga-relative coordinates (X, Z)
    /// </summary>
    public required double AvatarX { get; init; }
    public required double AvatarZ { get; init; }
}
