using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to interact with a SagaFeature (landmark, loot chest, quest marker, structure).
///
/// Side Effects:
/// - Creates EntityInteracted transaction
/// - May create LootAwarded transaction if feature has loot
/// - May create QuestTokenAwarded transactions if feature gives tokens
/// - Respects MaxInteractions and cooldowns
/// </summary>
public record InteractWithFeatureCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar interacting with the feature
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the feature
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Feature being interacted with
    /// </summary>
    public required string FeatureRef { get; init; }

    /// <summary>
    /// Avatar data (for quest token checks)
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
