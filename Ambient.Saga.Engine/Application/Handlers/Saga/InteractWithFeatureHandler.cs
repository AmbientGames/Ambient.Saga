using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for InteractWithFeatureCommand.
/// Creates FeatureInteracted transaction and handles loot transfer.
/// </summary>
internal sealed class InteractWithFeatureHandler : IRequestHandler<InteractWithFeatureCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public InteractWithFeatureHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(InteractWithFeatureCommand command, CancellationToken ct)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[InteractWithFeature] Avatar {command.AvatarId} interacting with feature '{command.FeatureRef}' in Saga '{command.SagaArcRef}'");

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Get Saga template and triggers
            if (!_world.SagaArcLookup.TryGetValue(command.SagaArcRef, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga '{command.SagaArcRef}' not found");
            }

            if (!_world.SagaTriggersLookup.TryGetValue(command.SagaArcRef, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{command.SagaArcRef}'");
            }

            // Get feature template (could be Landmark, Structure, or QuestSignpost)
            var feature = GetFeatureTemplate(command.FeatureRef);
            if (feature == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Feature '{command.FeatureRef}' not found");
            }

            var interactable = feature.Interactable;
            if (interactable == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Feature '{command.FeatureRef}' is not interactable");
            }

            // Replay state to check requirements and interaction limits
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Check quest token requirements
            if (interactable.RequiresQuestTokenRef != null && interactable.RequiresQuestTokenRef.Length > 0)
            {
                if (!HasAllQuestTokens(interactable.RequiresQuestTokenRef, command.Avatar))
                {
                    return SagaCommandResult.Failure(instance.InstanceId,
                        $"Missing required quest tokens: {string.Join(", ", interactable.RequiresQuestTokenRef)}");
                }
            }

            // Check MaxInteractions limit
            if (currentState.FeatureInteractions.TryGetValue(command.FeatureRef, out var featureState))
            {
                if (interactable.MaxInteractions > 0 && featureState.TotalInteractionCount >= interactable.MaxInteractions)
                {
                    return SagaCommandResult.Failure(instance.InstanceId,
                        $"Feature has reached maximum interactions ({interactable.MaxInteractions})");
                }

                // Check cooldown (ReinteractIntervalSeconds)
                if (interactable.ReinteractIntervalSeconds > 0 && featureState.LastInteractedAt != null)
                {
                    var elapsedSeconds = (DateTime.UtcNow - featureState.LastInteractedAt.Value).TotalSeconds;
                    if (elapsedSeconds < interactable.ReinteractIntervalSeconds)
                    {
                        var remainingSeconds = interactable.ReinteractIntervalSeconds - (int)elapsedSeconds;
                        return SagaCommandResult.Failure(instance.InstanceId,
                            $"Feature on cooldown. Wait {remainingSeconds} seconds.");
                    }
                }
            }

            // Create transactions
            var transactions = new List<SagaTransaction>();
            SagaTransaction? lootTransaction = null; // Track loot transaction for avatar update
            SagaTransaction? effectTransaction = null; // Track effect transaction for avatar update

            // 1. EntityInteracted transaction (tracks interaction count)
            var interactionTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.EntityInteracted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["FeatureRef"] = command.FeatureRef
                }
            };
            transactions.Add(interactionTx);

            // 2. LootAwarded transaction (if feature has loot)
            if (interactable.Loot != null && HasAnyItems(interactable.Loot))
            {
                // Serialize loot items (same format as LootCharacterHandler)
                var lootedEquipment = interactable.Loot.Equipment?
                    .Select(e => $"{e.EquipmentRef}:{e.Condition:F2}")
                    .ToList() ?? new List<string>();

                var lootedConsumables = interactable.Loot.Consumables?
                    .Select(c => $"{c.ConsumableRef}:{c.Quantity}")
                    .ToList() ?? new List<string>();

                var lootedSpells = interactable.Loot.Spells?
                    .Select(s => $"{s.SpellRef}:{s.Condition:F2}")
                    .ToList() ?? new List<string>();

                var lootedBlocks = interactable.Loot.Blocks?
                    .Select(b => $"{b.BlockRef}:{b.Quantity}")
                    .ToList() ?? new List<string>();

                var lootedTools = interactable.Loot.Tools?
                    .Select(t => $"{t.ToolRef}:{t.Condition:F2}")
                    .ToList() ?? new List<string>();

                var lootedMaterials = interactable.Loot.BuildingMaterials?
                    .Select(m => $"{m.BuildingMaterialRef}:{m.Quantity}")
                    .ToList() ?? new List<string>();

                lootTransaction = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.LootAwarded,
                    AvatarId = command.AvatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        ["FeatureRef"] = command.FeatureRef,
                        ["LootSource"] = $"Feature '{command.FeatureRef}' interaction",
                        ["Equipment"] = string.Join(",", lootedEquipment),
                        ["Consumables"] = string.Join(",", lootedConsumables),
                        ["Spells"] = string.Join(",", lootedSpells),
                        ["Blocks"] = string.Join(",", lootedBlocks),
                        ["Tools"] = string.Join(",", lootedTools),
                        ["BuildingMaterials"] = string.Join(",", lootedMaterials),
                        ["Credits"] = "0" // Features don't give credits (only characters do)
                    }
                };
                transactions.Add(lootTransaction);
                System.Diagnostics.Debug.WriteLine($"[InteractWithFeature] Awarding loot from feature '{command.FeatureRef}'");
            }

            // 3. QuestTokenAwarded transactions (if feature gives quest tokens)
            if (interactable.GivesQuestTokenRef != null)
            {
                foreach (var tokenRef in interactable.GivesQuestTokenRef)
                {
                    var tokenTx = new SagaTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        Type = SagaTransactionType.QuestTokenAwarded,
                        AvatarId = command.AvatarId.ToString(),
                        Status = TransactionStatus.Pending,
                        LocalTimestamp = DateTime.UtcNow,
                        Data = new Dictionary<string, string>
                        {
                            ["QuestTokenRef"] = tokenRef,
                            ["Source"] = $"Feature '{command.FeatureRef}'"
                        }
                    };
                    transactions.Add(tokenTx);
                    System.Diagnostics.Debug.WriteLine($"[InteractWithFeature] Awarding quest token '{tokenRef}' from feature '{command.FeatureRef}'");
                }
            }

            // 4. EffectApplied transaction (if feature has stat effects)
            if (interactable.Effects != null && HasAnyEffects(interactable.Effects))
            {
                effectTransaction = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.EffectApplied,
                    AvatarId = command.AvatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        ["FeatureRef"] = command.FeatureRef,
                        ["EffectSource"] = $"Feature '{command.FeatureRef}' interaction",
                        ["Health"] = interactable.Effects.Health.ToString(),
                        ["Stamina"] = interactable.Effects.Stamina.ToString(),
                        ["Mana"] = interactable.Effects.Mana.ToString(),
                        ["Strength"] = interactable.Effects.Strength.ToString(),
                        ["Defense"] = interactable.Effects.Defense.ToString(),
                        ["Speed"] = interactable.Effects.Speed.ToString(),
                        ["Magic"] = interactable.Effects.Magic.ToString(),
                        ["Credits"] = interactable.Effects.Credits.ToString(),
                        ["Experience"] = interactable.Effects.Experience.ToString()
                    }
                };
                transactions.Add(effectTransaction);
                System.Diagnostics.Debug.WriteLine($"[InteractWithFeature] Applying stat effects from feature '{command.FeatureRef}'");
            }

            // Add all transactions
            foreach (var tx in transactions)
            {
                instance.AddTransaction(tx);
            }

            // Persist transactions
            var sequenceNumbers = await _instanceRepository.AddTransactionsAsync(
                instance.InstanceId,
                transactions,
                ct);

            // Commit transactions
            var transactionIds = transactions.Select(t => t.TransactionId).ToList();
            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                transactionIds,
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Update avatar with rewards (loot and/or stat effects)
            AvatarEntity? updatedAvatar = null;
            if ((lootTransaction != null || effectTransaction != null) && command.Avatar is AvatarEntity avatarEntity)
            {
                updatedAvatar = avatarEntity;

                // Apply loot if awarded
                if (lootTransaction != null)
                {
                    updatedAvatar = await _avatarUpdateService.UpdateAvatarForLootAsync(
                        updatedAvatar,
                        instance,
                        lootTransaction.TransactionId,
                        ct);
                }

                // Apply stat effects if present
                if (effectTransaction != null)
                {
                    updatedAvatar = await _avatarUpdateService.UpdateAvatarForEffectsAsync(
                        updatedAvatar,
                        instance,
                        effectTransaction.TransactionId,
                        ct);
                }

                // CRITICAL: Wrap avatar persistence with compensating transaction on failure
                try
                {
                    await _avatarUpdateService.PersistAvatarAsync(updatedAvatar, ct);
                }
                catch (Exception persistEx)
                {
                    // Create compensating transaction
                    var reversalTransactionId = lootTransaction?.TransactionId ?? effectTransaction!.TransactionId;
                    var reversalType = lootTransaction?.Type.ToString() ?? effectTransaction!.Type.ToString();

                    var reversalTransaction = new SagaTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        Type = SagaTransactionType.TransactionReversed,
                        AvatarId = command.AvatarId.ToString(),
                        Status = TransactionStatus.Pending,
                        LocalTimestamp = DateTime.UtcNow,
                        Data = new Dictionary<string, string>
                        {
                            ["ReversedTransactionId"] = reversalTransactionId.ToString(),
                            ["Reason"] = $"Avatar persistence failed: {persistEx.Message}",
                            ["OriginalType"] = reversalType
                        }
                    };

                    instance.AddTransaction(reversalTransaction);
                    await _instanceRepository.AddTransactionsAsync(
                        instance.InstanceId,
                        new List<SagaTransaction> { reversalTransaction },
                        ct);
                    await _instanceRepository.CommitTransactionsAsync(
                        instance.InstanceId,
                        new List<Guid> { reversalTransaction.TransactionId },
                        ct);

                    return SagaCommandResult.Failure(
                        instance.InstanceId,
                        $"Rewards awarded but avatar update failed: {persistEx.Message}");
                }
            }

            return SagaCommandResult.Success(
                instance.InstanceId,
                transactionIds,
                sequenceNumbers.Last(),
                data: null,
                updatedAvatar);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error interacting with feature: {ex.Message}");
        }
    }

    private SagaFeature? GetFeatureTemplate(string featureRef)
    {
        // Unified lookup - all features (Landmarks, Structures, Quests, etc.) in one dictionary
        return _world.TryGetSagaFeatureByRefName(featureRef);
    }

    private static bool HasAllQuestTokens(string[] requiredTokens, AvatarBase avatar)
    {
        if (requiredTokens == null || requiredTokens.Length == 0)
            return true;

        if (avatar.Capabilities?.QuestTokens == null)
            return false;

        foreach (var required in requiredTokens)
        {
            if (!Array.Exists(avatar.Capabilities.QuestTokens, qt => qt.QuestTokenRef == required))
                return false;
        }

        return true;
    }

    private static bool HasAnyItems(ItemCollection loot)
    {
        if (loot == null) return false;

        return loot.Consumables != null && loot.Consumables.Length > 0 ||
               loot.BuildingMaterials != null && loot.BuildingMaterials.Length > 0 ||
               loot.Blocks != null && loot.Blocks.Length > 0 ||
               loot.Equipment != null && loot.Equipment.Length > 0 ||
               loot.Tools != null && loot.Tools.Length > 0 ||
               loot.Spells != null && loot.Spells.Length > 0;
    }

    private static bool HasAnyEffects(RewardEffects effects)
    {
        if (effects == null) return false;

        // Check if any stat differs from default value
        // Vitals default to 1.0 (multiplicative), others default to 0.0 (additive)
        return effects.Health != 1.0f ||
               effects.Stamina != 1.0f ||
               effects.Mana != 1.0f ||
               effects.Strength != 0.0f ||
               effects.Defense != 0.0f ||
               effects.Speed != 0.0f ||
               effects.Magic != 0.0f ||
               effects.Credits != 0.0f ||
               effects.Experience != 0.0f;
    }
}
