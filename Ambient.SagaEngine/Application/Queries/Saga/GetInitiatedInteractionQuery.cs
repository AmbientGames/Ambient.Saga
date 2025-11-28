using Ambient.Domain;
using Ambient.SagaEngine.Application.Results.Saga;
using MediatR;

namespace Ambient.SagaEngine.Application.Queries.Saga;

/// <summary>
/// Aggregates all available interactions across ALL Sagas and determines which single
/// interaction should be initiated based on priority rules.
///
/// This is the "arbiter" that decides: "Given everything nearby, what ONE thing wants to interact?"
///
/// Priority Rules:
/// 1. SpawnAndInitiate characters (aggressive/quest) > SpawnPassive characters
/// 2. Closer distance > farther distance
/// 3. Characters > Features (when both are equally close)
/// </summary>
public record GetInitiatedInteractionQuery : IRequest<InitiatedInteractionResult>
{
    public required Guid AvatarId { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required AvatarBase Avatar { get; init; }
}
