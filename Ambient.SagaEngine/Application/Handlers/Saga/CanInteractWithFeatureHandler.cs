using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using MediatR;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using Ambient.SagaEngine.Application.Queries.Saga;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for CanInteractWithFeatureQuery.
/// Checks if a feature can be interacted with based on conditions and cooldowns.
/// </summary>
internal sealed class CanInteractWithFeatureHandler : IRequestHandler<CanInteractWithFeatureQuery, FeatureInteractionCheck?>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly World _world;

    public CanInteractWithFeatureHandler(
        ISagaInstanceRepository instanceRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<FeatureInteractionCheck?> Handle(CanInteractWithFeatureQuery query, CancellationToken ct)
    {
        try
        {
            // Get Saga template
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return null;
            }

            // Find the feature using unified SagaFeatures lookup
            var feature = _world.TryGetSagaFeatureByRefName(query.FeatureRef);

            if (feature == null)
            {
                return null;
            }

            // Get Saga instance to check transaction history
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Check if feature has been interacted with recently
            var recentInteractions = instance.GetCommittedTransactions()
                .Where(t => t.Type == SagaTransactionType.EntityInteracted &&
                           t.Data.ContainsKey("FeatureRef") &&
                           t.Data["FeatureRef"] == query.FeatureRef)
                .OrderByDescending(t => t.GetCanonicalTimestamp())
                .ToList();

            // If feature has never been interacted with, it's available
            if (!recentInteractions.Any())
            {
                return new FeatureInteractionCheck
                {
                    Feature = feature,
                    CanInteract = true,
                    BlockedReason = null
                };
            }

            // Check cooldown using schema value (ReinteractIntervalSeconds)
            var interactable = feature.Interactable;
            if (interactable != null && interactable.ReinteractIntervalSeconds > 0)
            {
                var lastInteraction = recentInteractions.First();
                var elapsedSeconds = (DateTime.UtcNow - lastInteraction.GetCanonicalTimestamp()).TotalSeconds;

                if (elapsedSeconds < interactable.ReinteractIntervalSeconds)
                {
                    var remainingSeconds = (int)(interactable.ReinteractIntervalSeconds - elapsedSeconds);
                    var minutes = remainingSeconds / 60;
                    var seconds = remainingSeconds % 60;
                    return new FeatureInteractionCheck
                    {
                        Feature = feature,
                        CanInteract = false,
                        BlockedReason = $"Feature on cooldown. Wait {remainingSeconds} seconds ({minutes}m {seconds}s)."
                    };
                }
            }

            return new FeatureInteractionCheck
            {
                Feature = feature,
                CanInteract = true,
                BlockedReason = null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
