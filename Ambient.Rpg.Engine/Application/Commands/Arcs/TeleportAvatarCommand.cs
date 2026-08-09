using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

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
public record TeleportAvatarCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar being teleported
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc context (for transaction logging)
    /// </summary>
    public required string ArcRef { get; init; }

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
