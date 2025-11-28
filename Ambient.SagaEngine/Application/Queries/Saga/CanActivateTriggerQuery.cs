using Ambient.Domain;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using MediatR;

namespace Ambient.SagaEngine.Application.Queries.Saga;

/// <summary>
/// Query to check if a trigger can be activated by an avatar.
/// Returns comprehensive check result including why activation is blocked (if blocked).
/// </summary>
public record CanActivateTriggerQuery : IRequest<SagaTriggerActivationCheck?>
{
    /// <summary>
    /// Avatar attempting activation
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the trigger
    /// </summary>
    public required string SagaRef { get; init; }

    /// <summary>
    /// Trigger to check
    /// </summary>
    public required string TriggerRef { get; init; }

    /// <summary>
    /// Avatar position in Saga-relative coordinates
    /// </summary>
    public required double AvatarX { get; init; }
    public required double AvatarZ { get; init; }

    /// <summary>
    /// Avatar data (for quest token checks)
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
