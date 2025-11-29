using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Voxel;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to submit a location claim for movement validation.
/// Sent periodically (every 1-5 seconds) to track player position and detect teleportation/fly hacks.
///
/// Side Effects:
/// - Creates LocationClaimed transaction
/// - Validates movement speed against plausible limits
/// - Detects teleportation and fly hacks
/// </summary>
public record SubmitLocationClaimCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar submitting the claim
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga instance (typically the avatar's personal Saga for voxel state)
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Location claim data
    /// </summary>
    public required LocationClaim Claim { get; init; }
}
