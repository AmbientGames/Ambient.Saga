using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Domain.Rpg.Voxel;
using MediatR;

namespace Ambient.SagaEngine.Application.Commands.Saga;

/// <summary>
/// Command to submit a building session claim for material and placement validation.
/// Sent every 1-5 seconds with batched building activity.
///
/// Side Effects:
/// - Creates BuildingSessionClaimed transaction
/// - Validates building rate against plausible limits
/// - Validates reachability of placed blocks
/// - Validates material availability in inventory
/// - Detects infinite inventory hacks
/// </summary>
public record SubmitBuildingClaimCommand : IRequest<SagaCommandResult>
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
    /// Building session claim data
    /// </summary>
    public required BuildingSessionClaim Claim { get; init; }
}
