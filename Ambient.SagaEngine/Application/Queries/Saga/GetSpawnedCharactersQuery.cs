using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.SagaEngine.Application.Queries.Saga;

/// <summary>
/// Query to get all spawned characters in a Saga.
/// Returns character states from the transaction log replay.
/// </summary>
public record GetSpawnedCharactersQuery : IRequest<List<CharacterState>>
{
    /// <summary>
    /// Avatar requesting character list
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga to query
    /// </summary>
    public required string SagaRef { get; init; }

    /// <summary>
    /// Filter: Only include alive characters
    /// </summary>
    public bool AliveOnly { get; init; } = false;

    /// <summary>
    /// Filter: Only include spawned (not despawned) characters
    /// </summary>
    public bool SpawnedOnly { get; init; } = true;
}
