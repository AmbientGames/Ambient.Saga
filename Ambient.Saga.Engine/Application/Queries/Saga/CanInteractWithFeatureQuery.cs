using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using MediatR;

namespace Ambient.Saga.Engine.Application.Queries.Saga;

/// <summary>
/// Query to check if an avatar can interact with a SagaFeature.
/// Returns comprehensive check result including cooldowns, max interactions, quest tokens.
/// </summary>
public record CanInteractWithFeatureQuery : IRequest<FeatureInteractionCheck?>
{
    /// <summary>
    /// Avatar attempting interaction
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the feature
    /// </summary>
    public required string SagaRef { get; init; }

    /// <summary>
    /// Feature to interact with
    /// </summary>
    public required string FeatureRef { get; init; }

    /// <summary>
    /// Avatar data (for quest token checks)
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
