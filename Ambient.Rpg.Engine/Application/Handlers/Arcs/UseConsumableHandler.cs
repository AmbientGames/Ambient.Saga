using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for UseConsumableCommand.
/// Applies consumable effects to avatar's stats and decrements quantity.
///
/// Effects Applied:
/// - Health restoration (capped at 1.0)
/// - Stamina restoration (capped at 1.0)
/// - Mana restoration (capped at 1.0)
/// - Temperature adjustment
///
/// Status effects (StatusEffectRef) and cleansing (CleansesStatusEffects) are
/// logged in the transaction for battle-time processing.
/// </summary>
internal sealed class UseConsumableHandler : IRequestHandler<UseConsumableCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public UseConsumableHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(UseConsumableCommand command, CancellationToken ct)
    {
        try
        {
            // Validate consumable exists in avatar's inventory
            var consumableEntry = command.Avatar.Capabilities?.Consumables?
                .FirstOrDefault(c => c.ConsumableRef == command.ConsumableRef);

            if (consumableEntry == null || consumableEntry.Quantity <= 0)
            {
                return ArcCommandResult.Failure(Guid.Empty,
                    $"Avatar does not have any '{command.ConsumableRef}' to use");
            }

            // Get consumable definition from world
            var consumable = _world.GetConsumableByRefName(command.ConsumableRef);
            if (consumable == null)
            {
                return ArcCommandResult.Failure(Guid.Empty,
                    $"Consumable '{command.ConsumableRef}' not found in world definition");
            }

            // Get or create arc instance for transaction logging
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(
                command.AvatarId, command.ArcRef, ct);

            // Ensure avatar has Stats initialized
            command.Avatar.Stats ??= new CharacterStats();

            // Store original stats for transaction record
            var originalHealth = command.Avatar.Stats.Health;
            var originalStamina = command.Avatar.Stats.Stamina;
            var originalMana = command.Avatar.Stats.Mana;
            var originalTemperature = command.Avatar.Stats.Temperature;

            // Apply consumable effects
            var effectsApplied = new List<string>();

            if (consumable.Effects != null)
            {
                // Health restoration (additive, capped at 1.0)
                if (consumable.Effects.Health > 0)
                {
                    var healthGain = consumable.Effects.Health;
                    var newHealth = Math.Min(1.0f, command.Avatar.Stats.Health + healthGain);
                    var actualGain = newHealth - command.Avatar.Stats.Health;
                    if (actualGain > 0)
                    {
                        command.Avatar.Stats.Health = newHealth;
                        effectsApplied.Add($"Health +{actualGain:P0}");
                    }
                }

                // Stamina restoration (additive, capped at 1.0)
                if (consumable.Effects.Stamina > 0)
                {
                    var staminaGain = consumable.Effects.Stamina;
                    var newStamina = Math.Min(1.0f, command.Avatar.Stats.Stamina + staminaGain);
                    var actualGain = newStamina - command.Avatar.Stats.Stamina;
                    if (actualGain > 0)
                    {
                        command.Avatar.Stats.Stamina = newStamina;
                        effectsApplied.Add($"Stamina +{actualGain:P0}");
                    }
                }

                // Mana restoration (additive, capped at 1.0)
                if (consumable.Effects.Mana > 0)
                {
                    var manaGain = consumable.Effects.Mana;
                    var newMana = Math.Min(1.0f, command.Avatar.Stats.Mana + manaGain);
                    var actualGain = newMana - command.Avatar.Stats.Mana;
                    if (actualGain > 0)
                    {
                        command.Avatar.Stats.Mana = newMana;
                        effectsApplied.Add($"Mana +{actualGain:P0}");
                    }
                }

                // Temperature delta from the consumable. Positive warms the avatar,
                // negative cools it. 0 = no effect (matches the XSD default).
                if (consumable.Effects.Temperature != 0)
                {
                    var tempChange = consumable.Effects.Temperature;
                    command.Avatar.Stats.Temperature += tempChange;
                    effectsApplied.Add($"Temperature {(tempChange >= 0 ? "+" : "")}{tempChange:F1}°");
                }
            }

            // Build transaction data
            var transactionData = new Dictionary<string, string>
            {
                [TransactionDataKeys.ConsumableRef] = command.ConsumableRef,
                [TransactionDataKeys.ConsumableDisplayName] = consumable.DisplayName ?? command.ConsumableRef,
                [TransactionDataKeys.QuantityBefore] = consumableEntry.Quantity.ToString(),
                [TransactionDataKeys.QuantityAfter] = (consumableEntry.Quantity - 1).ToString(),
                [TransactionDataKeys.EffectsApplied] = string.Join(", ", effectsApplied),
                [TransactionDataKeys.OriginalHealth] = originalHealth.ToString("F3"),
                [TransactionDataKeys.OriginalStamina] = originalStamina.ToString("F3"),
                [TransactionDataKeys.OriginalMana] = originalMana.ToString("F3"),
                [TransactionDataKeys.NewHealth] = command.Avatar.Stats.Health.ToString("F3"),
                [TransactionDataKeys.NewStamina] = command.Avatar.Stats.Stamina.ToString("F3"),
                [TransactionDataKeys.NewMana] = command.Avatar.Stats.Mana.ToString("F3")
            };

            // Log status effect info if present (for battle-time processing)
            if (!string.IsNullOrEmpty(consumable.StatusEffectRef))
            {
                transactionData[TransactionDataKeys.StatusEffectRef] = consumable.StatusEffectRef;
                transactionData[TransactionDataKeys.StatusEffectChance] = consumable.StatusEffectChance.ToString("F2");
            }

            if (consumable.CleansesStatusEffects)
            {
                transactionData[TransactionDataKeys.CleansesStatusEffects] = "true";
                transactionData[TransactionDataKeys.CleanseTargetSelf] = consumable.CleanseTargetSelf.ToString();
            }

            // Create ConsumableUsed transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.ConsumableUsed,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = transactionData
            };

            instance.AddTransaction(transaction);

            // Persist transaction
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<ArcTransaction> { transaction },
                ct);

            if (!committed)
            {
                // Commit failed — roll back the stat effects applied above (audit M2:
                // they used to survive a failed commit, a free durable heal). Same
                // rollback pattern as SharpenToolHandler.
                RestoreOriginalStats(command.Avatar,
                    originalHealth, originalStamina, originalMana, originalTemperature);

                return ArcCommandResult.Failure(instance.InstanceId,
                    "Concurrency conflict - transaction rolled back");
            }

            // Update transaction status
            transaction.Status = TransactionStatus.Committed;

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            // Decrement consumable quantity
            consumableEntry.Quantity--;

            // Remove from inventory if quantity reaches 0
            if (consumableEntry.Quantity <= 0 && command.Avatar.Capabilities?.Consumables != null)
            {
                command.Avatar.Capabilities.Consumables = command.Avatar.Capabilities.Consumables
                    .Where(c => c.ConsumableRef != command.ConsumableRef)
                    .ToArray();
            }

            // Persist avatar
            try
            {
                await _avatarUpdateService.PersistAvatarAsync(command.Avatar, ct);
            }
            catch (Exception persistEx)
            {
                // Avatar update failed after transaction committed - create compensating transaction
                var reversalTransaction = new ArcTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = ArcTransactionType.TransactionReversed,
                    AvatarId = command.AvatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        [TransactionDataKeys.ReversedTransactionId] = transaction.TransactionId.ToString(),
                        [TransactionDataKeys.Reason] = $"Avatar persistence failed: {persistEx.Message}",
                        [TransactionDataKeys.OriginalType] = transaction.Type.ToString()
                    }
                };

                instance.AddTransaction(reversalTransaction);
                await _instanceRepository.AddAndCommitTransactionsAsync(
                    instance.InstanceId,
                    new List<ArcTransaction> { reversalTransaction },
                    ct);

                // Compensation must also un-mutate the in-memory avatar (audit M2/M4
                // pattern): restore stats and the consumed quantity so a later periodic
                // save can't persist state the ledger says was reversed.
                RestoreOriginalStats(command.Avatar,
                    originalHealth, originalStamina, originalMana, originalTemperature);
                consumableEntry.Quantity++;
                if (command.Avatar.Capabilities != null &&
                    (command.Avatar.Capabilities.Consumables == null ||
                     !command.Avatar.Capabilities.Consumables.Any(c => c.ConsumableRef == command.ConsumableRef)))
                {
                    var list = command.Avatar.Capabilities.Consumables?.ToList() ?? new List<ConsumableEntry>();
                    list.Add(consumableEntry);
                    command.Avatar.Capabilities.Consumables = list.ToArray();
                }

                return ArcCommandResult.Failure(instance.InstanceId,
                    $"Consumable used but avatar update failed: {persistEx.Message}");
            }

            var resultData = new Dictionary<string, object>
            {
                [TransactionDataKeys.ConsumableRef] = command.ConsumableRef,
                [TransactionDataKeys.ConsumableDisplayName] = consumable.DisplayName ?? command.ConsumableRef,
                [TransactionDataKeys.EffectsApplied] = effectsApplied,
                [TransactionDataKeys.RemainingQuantity] = consumableEntry.Quantity
            };

            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData,
                command.Avatar);
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error using consumable: {ex.Message}");
        }
    }

    /// <summary>Rolls the avatar's vitals back to their pre-consumable values (audit M2).</summary>
    private static void RestoreOriginalStats(
        AvatarEntity avatar,
        float originalHealth,
        float originalStamina,
        float originalMana,
        float originalTemperature)
    {
        if (avatar.Stats == null)
            return;

        avatar.Stats.Health = originalHealth;
        avatar.Stats.Stamina = originalStamina;
        avatar.Stats.Mana = originalMana;
        avatar.Stats.Temperature = originalTemperature;
    }
}
