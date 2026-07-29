using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Queries.Arcs;

/// <summary>
/// Query to get all spawned characters in an arc.
/// Returns character states from the transaction log replay.
/// </summary>
public record GetSpawnedCharactersQuery : IRequest<List<CharacterState>>
{
    /// <summary>
    /// Avatar requesting character list
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc to query
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Filter: Only include alive characters
    /// </summary>
    public bool AliveOnly { get; init; } = false;

    /// <summary>
    /// Filter: Only include spawned (not despawned) characters
    /// </summary>
    public bool SpawnedOnly { get; init; } = true;
}
