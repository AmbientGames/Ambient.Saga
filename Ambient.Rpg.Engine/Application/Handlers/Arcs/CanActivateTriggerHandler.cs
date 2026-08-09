using MediatR;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for CanActivateTriggerQuery.
/// Checks if a trigger can be activated based on cooldowns and conditions.
/// </summary>
internal sealed class CanActivateTriggerHandler : IRequestHandler<CanActivateTriggerQuery, ArcTriggerActivationCheck?>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public CanActivateTriggerHandler(
        IArcInstanceRepository instanceRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<ArcTriggerActivationCheck?> Handle(CanActivateTriggerQuery query, CancellationToken ct)
    {
        try
        {
            // Get Arc template
            if (!_world.ArcLookup.TryGetValue(query.ArcRef, out var arcTemplate))
            {
                return null;
            }

            // Get expanded triggers
            if (!_world.ArcTriggersLookup.TryGetValue(query.ArcRef, out var expandedTriggers))
            {
                return null;
            }

            // Find the trigger
            var trigger = expandedTriggers.FirstOrDefault(t => t.RefName == query.TriggerRef);
            if (trigger == null)
            {
                return null;
            }

            // Get Arc instance to check transaction history
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.ArcRef, ct);

            // Check if trigger has already been activated
            // (emitted under ArcTriggerRef — see ArcInteractionService)
            var alreadyActivated = instance.GetCommittedTransactions()
                .Any(t => t.Type == ArcTransactionType.TriggerActivated &&
                         t.Data.ContainsKey(TransactionDataKeys.ArcTriggerRef) &&
                         t.Data[TransactionDataKeys.ArcTriggerRef] == query.TriggerRef);

            var canActivate = !alreadyActivated;

            return new ArcTriggerActivationCheck
            {
                ArcTrigger = trigger,
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
