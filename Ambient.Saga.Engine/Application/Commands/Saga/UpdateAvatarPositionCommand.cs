using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to update avatar position and check for Saga discoveries/trigger activations.
/// This is the primary command called by the game engine on every position update.
///
/// Side Effects:
/// - May create SagaDiscovered transaction if avatar enters new Saga
/// - May create TriggerActivated transaction if avatar enters trigger radius
/// - May create CharacterSpawned transactions if trigger has spawns
/// - May create QuestTokenAwarded transactions if trigger awards tokens
/// </summary>
public record UpdateAvatarPositionCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar performing the movement
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga to check for interactions (must specify which Saga is being checked)
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Avatar's new latitude (world coordinates)
    /// </summary>
    public required double Latitude { get; init; }

    /// <summary>
    /// Avatar's new longitude (world coordinates)
    /// </summary>
    public required double Longitude { get; init; }

    /// <summary>
    /// Avatar's elevation/height
    /// </summary>
    public required double Y { get; init; }

    /// <summary>
    /// Avatar data (for quest token checks, etc.)
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
