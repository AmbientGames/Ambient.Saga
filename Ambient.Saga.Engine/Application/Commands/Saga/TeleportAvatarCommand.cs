using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to teleport the avatar to a new location.
/// Deducts currency from the avatar.
///
/// Side Effects:
/// - Creates AvatarTeleported transaction
/// - Deducts currency cost from avatar
/// - Updates avatar position (latitude/longitude)
/// - Persists updated avatar state
/// </summary>
public record TeleportAvatarCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar being teleported
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga context (for transaction logging)
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Destination latitude
    /// </summary>
    public required double DestinationLatitude { get; init; }

    /// <summary>
    /// Destination longitude
    /// </summary>
    public required double DestinationLongitude { get; init; }

    /// <summary>
    /// Cost in currency to teleport
    /// </summary>
    public required int Cost { get; init; }

    /// <summary>
    /// Avatar entity for state updates and persistence
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
