using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Voxel;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to submit a mining session claim for plausibility validation.
/// Sent every 1-5 seconds with batched mining activity.
///
/// Side Effects:
/// - Creates MiningSessionClaimed transaction
/// - Validates mining rate against plausible limits
/// - Validates reachability of mined blocks
/// - Detects X-ray hacks via rare ore distribution
/// - Detects speed hacks via mining rate
/// </summary>
public record SubmitMiningClaimCommand : IRequest<SagaCommandResult>
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
    /// Mining session claim data
    /// </summary>
    public required MiningSessionClaim Claim { get; init; }
}
