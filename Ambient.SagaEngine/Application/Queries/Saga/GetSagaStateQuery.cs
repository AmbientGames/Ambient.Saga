using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.SagaEngine.Application.Queries.Saga;

/// <summary>
/// Query to get the current state of a Saga (derived from transaction log).
/// Returns full SagaState including triggers, characters, discoveries, etc.
/// </summary>
public record GetSagaStateQuery : IRequest<SagaState?>
{
    /// <summary>
    /// Avatar requesting the state (for avatar-specific data like discoveries)
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga to query
    /// </summary>
    public required string SagaRef { get; init; }
}
