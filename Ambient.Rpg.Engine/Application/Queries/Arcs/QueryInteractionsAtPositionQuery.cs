using Ambient.Domain;
using Ambient.Rpg.Engine.Domain.Services;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Query to find all arc interactions (triggers, features, characters) at a specific model position.
/// This is a general-purpose query for "what's at this point?" - used for map clicks, hover, etc.
///
/// Different from GetAvailableInteractionsQuery which queries one specific arc.
/// This queries ALL arcs and returns everything at the position.
/// </summary>
public record QueryInteractionsAtPositionQuery : IRequest<List<ArcInteraction>>
{
    /// <summary>
    /// Model X coordinate (world space)
    /// </summary>
    public required double ModelX { get; init; }

    /// <summary>
    /// Model Z coordinate (world space)
    /// </summary>
    public required double ModelZ { get; init; }

    /// <summary>
    /// Avatar for checking availability/status (can be null for pure proximity check)
    /// </summary>
    public AvatarBase? Avatar { get; init; }
}
