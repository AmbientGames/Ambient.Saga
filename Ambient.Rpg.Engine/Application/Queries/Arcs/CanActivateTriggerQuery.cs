using Ambient.Domain;
using Ambient.Rpg.Engine.Domain.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Query to check if a trigger can be activated by an avatar.
/// Returns comprehensive check result including why activation is blocked (if blocked).
/// </summary>
public record CanActivateTriggerQuery : IRequest<ArcTriggerActivationCheck?>
{
    /// <summary>
    /// Avatar attempting activation
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the trigger
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Trigger to check
    /// </summary>
    public required string TriggerRef { get; init; }

    /// <summary>
    /// Avatar position in Arc-relative coordinates
    /// </summary>
    public required double AvatarX { get; init; }
    public required double AvatarZ { get; init; }

    /// <summary>
    /// Avatar data (for quest token checks)
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
