using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Voxel;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to submit a tool wear claim for durability validation.
/// Sent when tool condition changes significantly or periodically.
///
/// Side Effects:
/// - Creates ToolWearClaimed transaction
/// - Validates wear rate against expected values
/// - Detects infinite durability hacks
/// </summary>
public record SubmitToolWearClaimCommand : IRequest<SagaCommandResult>
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
    /// Tool wear claim data
    /// </summary>
    public required ToolWearClaim Claim { get; init; }
}
