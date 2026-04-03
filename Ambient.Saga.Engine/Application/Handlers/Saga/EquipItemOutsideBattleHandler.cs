using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for EquipItemOutsideBattleCommand.
/// Updates avatar's CombatProfile (equipped slots) outside of battle.
///
/// Hand slot rules (hard-coded):
/// - MainHand: Primary weapon hand
/// - OffHand: Secondary hand (shield, off-hand weapon)
/// - BothHands: Two-handed weapons
///
/// When equipping BothHands: automatically clears MainHand and OffHand
/// When equipping MainHand or OffHand: automatically clears BothHands
/// </summary>
internal sealed class EquipItemOutsideBattleHandler : IRequestHandler<EquipItemOutsideBattleCommand, SagaCommandResult>
{
    // Hard-coded hand slot names for two-handed weapon logic
    private const string MainHandSlot = "MainHand";
    private const string OffHandSlot = "OffHand";
    private const string BothHandsSlot = "BothHands";

    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public EquipItemOutsideBattleHandler(
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

    public async Task<SagaCommandResult> Handle(EquipItemOutsideBattleCommand command, CancellationToken ct)
    {
        try
        {
            // Validate slot exists in world definition
            var loadoutSlots = _world.Gameplay?.LoadoutSlots;
            var validSlots = loadoutSlots?.Select(s => s.RefName).ToHashSet() ?? new HashSet<string>();

            // Fallback to common slots if world doesn't define any
            if (validSlots.Count == 0)
            {
                validSlots = new HashSet<string>
                {
                    "Head", "Chest", "Legs", "Feet", "OffHand", "MainHand",
                    "BothHands", "Hands", "Back", "Ring", "Amulet", "Belt"
                };
            }

            if (!validSlots.Contains(command.SlotRef))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Invalid equipment slot: {command.SlotRef}");
            }

            var isUnequipping = string.IsNullOrEmpty(command.EquipmentRef);
            string? previousEquipmentRef = null;

            if (!isUnequipping)
            {
                // Validate equipment exists in avatar's inventory
                var hasEquipment = command.Avatar.Capabilities?.Equipment?
                    .Any(e => e.EquipmentRef == command.EquipmentRef) ?? false;

                if (!hasEquipment)
                {
                    return SagaCommandResult.Failure(Guid.Empty,
                        $"Avatar does not own equipment '{command.EquipmentRef}'");
                }

                // Validate equipment can go in this slot
                var equipment = _world.GetEquipmentByRefName(command.EquipmentRef);
                if (equipment == null)
                {
                    return SagaCommandResult.Failure(Guid.Empty,
                        $"Equipment '{command.EquipmentRef}' not found in world definition");
                }

                if (equipment.SlotRef != command.SlotRef)
                {
                    return SagaCommandResult.Failure(Guid.Empty,
                        $"Equipment '{command.EquipmentRef}' cannot be equipped in slot '{command.SlotRef}' (requires slot '{equipment.SlotRef}')");
                }
            }

            // Get current equipped item in this slot (for transaction record)
            command.Avatar.CombatProfile ??= new Dictionary<string, string>();
            command.Avatar.CombatProfile.TryGetValue(command.SlotRef, out previousEquipmentRef);

            // Skip if no change
            if (isUnequipping && string.IsNullOrEmpty(previousEquipmentRef))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Slot '{command.SlotRef}' is already empty");
            }

            if (!isUnequipping && previousEquipmentRef == command.EquipmentRef)
            {
                return SagaCommandResult.Failure(Guid.Empty,
                    $"'{command.EquipmentRef}' is already equipped in slot '{command.SlotRef}'");
            }

            // Get or create saga instance for transaction logging
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(
                command.AvatarId, command.SagaArcRef, ct);

            // Create EquipmentChanged transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.EquipmentChanged,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["SlotRef"] = command.SlotRef,
                    ["PreviousEquipmentRef"] = previousEquipmentRef ?? "",
                    ["NewEquipmentRef"] = command.EquipmentRef ?? "",
                    ["IsUnequipping"] = isUnequipping.ToString()
                }
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

            // Update avatar's CombatProfile
            // Handle hand slot interactions (BothHands <-> MainHand/OffHand)
            var clearedSlots = new List<string>();
            if (!isUnequipping)
            {
                if (command.SlotRef == BothHandsSlot)
                {
                    // Equipping two-handed weapon: clear both one-handed slots
                    if (command.Avatar.CombatProfile.Remove(MainHandSlot))
                        clearedSlots.Add(MainHandSlot);
                    if (command.Avatar.CombatProfile.Remove(OffHandSlot))
                        clearedSlots.Add(OffHandSlot);
                }
                else if (command.SlotRef == MainHandSlot || command.SlotRef == OffHandSlot)
                {
                    // Equipping one-handed item: clear two-handed slot
                    if (command.Avatar.CombatProfile.Remove(BothHandsSlot))
                        clearedSlots.Add(BothHandsSlot);
                }
            }

            if (isUnequipping)
            {
                command.Avatar.CombatProfile.Remove(command.SlotRef);
            }
            else
            {
                command.Avatar.CombatProfile[command.SlotRef] = command.EquipmentRef!;
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
                        ["ReversedTransactionId"] = transaction.TransactionId.ToString(),
                        ["Reason"] = $"Avatar persistence failed: {persistEx.Message}",
                        ["OriginalType"] = transaction.Type.ToString()
                    }
                };

                instance.AddTransaction(reversalTransaction);
                await _instanceRepository.AddAndCommitTransactionsAsync(
                    instance.InstanceId,
                    new List<SagaTransaction> { reversalTransaction },
                    ct);

                return SagaCommandResult.Failure(instance.InstanceId,
                    $"Equipment change committed but avatar update failed: {persistEx.Message}");
            }

            var resultData = new Dictionary<string, object>
            {
                ["SlotRef"] = command.SlotRef,
                ["PreviousEquipmentRef"] = previousEquipmentRef ?? "(none)",
                ["NewEquipmentRef"] = command.EquipmentRef ?? "(none)",
                ["Action"] = isUnequipping ? "Unequipped" : "Equipped",
                ["ClearedSlots"] = clearedSlots
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
            return SagaCommandResult.Failure(Guid.Empty, $"Error changing equipment: {ex.Message}");
        }
    }
}
