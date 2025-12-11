using Ambient.Domain.DefinitionExtensions;
using MediatR;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Application.Queries.Saga;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for CanActivateTriggerQuery.
/// Checks if a trigger can be activated based on cooldowns and conditions.
/// </summary>
internal sealed class CanActivateTriggerHandler : IRequestHandler<CanActivateTriggerQuery, SagaTriggerActivationCheck?>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public CanActivateTriggerHandler(
        ISagaInstanceRepository instanceRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<SagaTriggerActivationCheck?> Handle(CanActivateTriggerQuery query, CancellationToken ct)
    {
        try
        {
            // Get Saga template
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return null;
            }

            // Get expanded triggers
            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
            {
                return null;
            }

            // Find the trigger
            var trigger = expandedTriggers.FirstOrDefault(t => t.RefName == query.TriggerRef);
            if (trigger == null)
            {
                return null;
            }

            // Get Saga instance to check transaction history
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Check if trigger has already been activated
            var alreadyActivated = instance.GetCommittedTransactions()
                .Any(t => t.Type == SagaTransactionType.TriggerActivated &&
                         t.Data.ContainsKey("TriggerRef") &&
                         t.Data["TriggerRef"] == query.TriggerRef);

            var canActivate = !alreadyActivated;

            return new SagaTriggerActivationCheck
            {
                SagaTrigger = trigger,
                CanActivate = canActivate,
                DistanceFromCenter = 0, // Would calculate this from query.AvatarX, query.AvatarZ
                IsWithinRadius = true,  // Assuming within radius if query was made
                HasRequiredQuestTokens = true // Would check query.Avatar for quest tokens
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
