using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

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
internal sealed class UseConsumableHandler : IRequestHandler<UseConsumableCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public UseConsumableHandler(
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

    public async Task<SagaCommandResult> Handle(UseConsumableCommand command, CancellationToken ct)
    {
        try
        {
            // Validate consumable exists in avatar's inventory
            var consumableEntry = command.Avatar.Capabilities?.Consumables?
                .FirstOrDefault(c => c.ConsumableRef == command.ConsumableRef);

            if (consumableEntry == null || consumableEntry.Quantity <= 0)
            {
                return SagaCommandResult.Failure(Guid.Empty,
                    $"Avatar does not have any '{command.ConsumableRef}' to use");
            }

            // Get consumable definition from world
            var consumable = _world.GetConsumableByRefName(command.ConsumableRef);
            if (consumable == null)
            {
                return SagaCommandResult.Failure(Guid.Empty,
                    $"Consumable '{command.ConsumableRef}' not found in world definition");
            }

            // Get or create saga instance for transaction logging
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(
                command.AvatarId, command.SagaArcRef, ct);

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

                // Temperature adjustment (move toward normal body temperature of 37)
                if (consumable.Effects.Temperature != 37) // Non-default temperature effect
                {
                    var tempChange = consumable.Effects.Temperature - 37; // Relative change
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
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.ConsumableUsed,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = transactionData
            };

            instance.AddTransaction(transaction);

            // Persist transaction
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { transaction },
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId,
                    "Concurrency conflict - transaction rolled back");
            }

            // Update transaction status
            transaction.Status = TransactionStatus.Committed;

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

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
                var reversalTransaction = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.TransactionReversed,
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
                    new List<SagaTransaction> { reversalTransaction },
                    ct);

                return SagaCommandResult.Failure(instance.InstanceId,
                    $"Consumable used but avatar update failed: {persistEx.Message}");
            }

            var resultData = new Dictionary<string, object>
            {
                [TransactionDataKeys.ConsumableRef] = command.ConsumableRef,
                [TransactionDataKeys.ConsumableDisplayName] = consumable.DisplayName ?? command.ConsumableRef,
                [TransactionDataKeys.EffectsApplied] = effectsApplied,
                [TransactionDataKeys.RemainingQuantity] = consumableEntry.Quantity
            };

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData,
                command.Avatar);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error using consumable: {ex.Message}");
        }
    }
}
