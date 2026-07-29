using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to update avatar position and check for Arc discoveries/trigger activations.
/// This is the primary command called by the game engine on every position update.
///
/// Side Effects:
/// - May create ArcDiscovered transaction if avatar enters new Arc
/// - May create TriggerActivated transaction if avatar enters trigger radius
/// - May create CharacterSpawned transactions if trigger has spawns
/// - May create QuestTokenAwarded transactions if trigger awards tokens
/// </summary>
public record UpdateAvatarPositionCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar performing the movement
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc to check for interactions (must specify which Arc is being checked)
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Avatar's new latitude (world coordinates)
    /// </summary>
    public required double Latitude { get; init; }

    /// <summary>
    /// Avatar's new longitude (world coordinates)
    /// </summary>
    public required double Longitude { get; init; }

    /// <summary>
    /// Avatar data (for quest token checks, etc.)
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
