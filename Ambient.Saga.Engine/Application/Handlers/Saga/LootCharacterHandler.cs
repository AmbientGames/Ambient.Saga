using Ambient.Domain.Contracts;
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
/// Handler for LootCharacterCommand.
/// Creates LootAwarded transaction, updates avatar inventory, and persists changes.
/// </summary>
internal sealed class LootCharacterHandler : IRequestHandler<LootCharacterCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public LootCharacterHandler(
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

    public async Task<SagaCommandResult> Handle(LootCharacterCommand command, CancellationToken ct)
    {
        try
        {
            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Verify Saga and get expanded triggers
            if (!_world.SagaArcLookup.TryGetValue(command.SagaArcRef, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga '{command.SagaArcRef}' not found");
            }

            if (!_world.SagaTriggersLookup.TryGetValue(command.SagaArcRef, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{command.SagaArcRef}'");
            }

            // Replay to get current state
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Verify character exists and is defeated
            var characterKey = command.CharacterInstanceId.ToString();
            if (!currentState.Characters.TryGetValue(characterKey, out var character))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character '{command.CharacterInstanceId}' not found");
            }

            if (character.IsAlive)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Cannot loot living character");
            }

            if (character.HasBeenLooted)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Character already looted");
            }

            // Serialize character inventory for transaction
            var lootedEquipment = character.CurrentInventory?.Equipment?
                .Select(e => $"{e.EquipmentRef}:{e.Condition:F2}")
                .ToList() ?? new List<string>();

            var lootedConsumables = character.CurrentInventory?.Consumables?
                .Select(c => $"{c.ConsumableRef}:{c.Quantity}")
                .ToList() ?? new List<string>();

            var lootedSpells = character.CurrentInventory?.Spells?
                .Select(s => $"{s.SpellRef}:{s.Condition:F2}")
                .ToList() ?? new List<string>();

            var lootedBlocks = character.CurrentInventory?.Blocks?
                .Select(b => $"{b.BlockRef}:{b.Quantity}")
                .ToList() ?? new List<string>();

            var lootedTools = character.CurrentInventory?.Tools?
                .Select(t => $"{t.ToolRef}:{t.Condition:F2}")
                .ToList() ?? new List<string>();

            var lootedMaterials = character.CurrentInventory?.BuildingMaterials?
                .Select(m => $"{m.BuildingMaterialRef}:{m.Quantity}")
                .ToList() ?? new List<string>();

            var lootedCredits = character.CurrentStats?.Credits ?? 0;

            // Create LootAwarded transaction with complete inventory data
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.LootAwarded,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = command.CharacterInstanceId.ToString(),
                    ["CharacterRef"] = character.CharacterRef,
                    ["LootSource"] = $"Character '{character.CharacterRef}' defeated",
                    ["Equipment"] = string.Join(",", lootedEquipment),
                    ["Consumables"] = string.Join(",", lootedConsumables),
                    ["Spells"] = string.Join(",", lootedSpells),
                    ["Blocks"] = string.Join(",", lootedBlocks),
                    ["Tools"] = string.Join(",", lootedTools),
                    ["BuildingMaterials"] = string.Join(",", lootedMaterials),
                    ["Credits"] = lootedCredits.ToString()
                }
            };

            instance.AddTransaction(transaction);

            // Persist transaction
            var sequenceNumbers = await _instanceRepository.AddTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { transaction },
                ct);

            // Commit transaction
            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Update avatar inventory with looted items, then persist
            AvatarEntity? updatedAvatar = null;
            if (command.Avatar is AvatarEntity avatarEntity)
            {
                updatedAvatar = await _avatarUpdateService.UpdateAvatarForLootAsync(
                    avatarEntity,
                    instance,
                    transaction.TransactionId,
                    ct);

                // CRITICAL FIX: Wrap avatar persistence with compensating transaction on failure
                try
                {
                    await _avatarUpdateService.PersistAvatarAsync(updatedAvatar, ct);
                }
                catch (Exception persistEx)
                {
                    // Create compensating transaction
                    var reversalTransaction = new SagaTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        Type = SagaTransactionType.TransactionReversed,
                        AvatarId = command.AvatarId.ToString(),
                        Status = TransactionStatus.Pending,
                        LocalTimestamp = DateTime.UtcNow,
                        Data = new Dictionary<string, string>
                        {
                            ["ReversedTransactionId"] = transaction.TransactionId.ToString(),
                            ["Reason"] = $"Avatar persistence failed: {persistEx.Message}",
                            ["OriginalType"] = transaction.Type.ToString()
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
                        $"Loot awarded but avatar update failed: {persistEx.Message}");
                }
            }

            // Return self-contained result with updated avatar
            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                data: null,
                updatedAvatar);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error looting character: {ex.Message}");
        }
    }
}
