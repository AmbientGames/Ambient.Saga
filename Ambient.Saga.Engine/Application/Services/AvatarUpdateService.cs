using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.Extensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Achievements;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Services;

/// <summary>
/// Service for updating avatar state based on Saga transactions.
/// Applies transaction effects to avatar and handles persistence.
/// </summary>
public class AvatarUpdateService : IAvatarUpdateService
{
    // Lazily resolved: this service is a singleton but its dependencies are only
    // populated after a world is loaded. Constructing them eagerly would throw.
    private readonly Func<IGameAvatarRepository> _avatarRepositoryAccessor;
    private readonly Func<IWorld> _worldAccessor;

    private IGameAvatarRepository _avatarRepository => _avatarRepositoryAccessor();
    private IWorld _world => _worldAccessor();

    /// <inheritdoc/>
    public event Action<CreditChangeNotification>? CreditsChanged;

    public AvatarUpdateService(Func<IGameAvatarRepository> avatarRepositoryAccessor, Func<IWorld> worldAccessor)
    {
        _avatarRepositoryAccessor = avatarRepositoryAccessor ?? throw new ArgumentNullException(nameof(avatarRepositoryAccessor));
        _worldAccessor = worldAccessor ?? throw new ArgumentNullException(nameof(worldAccessor));
    }

    private void RaiseCreditsChanged(Guid avatarId, int delta, string reason, Guid transactionId)
    {
        if (delta == 0) return;
        CreditsChanged?.Invoke(new CreditChangeNotification(avatarId, delta, reason, transactionId));
    }

    /// <inheritdoc/>
    public async Task<AvatarEntity> UpdateAvatarForTradeAsync(
        AvatarEntity avatar,
        SagaInstance sagaInstance,
        Guid tradeTransactionId,
        CancellationToken ct = default)
    {
        // Find the ItemTraded transaction
        var transaction = sagaInstance.GetCommittedTransactions()
            .FirstOrDefault(t => t.TransactionId == tradeTransactionId && t.Type == SagaTransactionType.ItemTraded);

        if (transaction == null)
        {
            throw new InvalidOperationException($"Trade transaction '{tradeTransactionId}' not found or not committed");
        }

        // Parse transaction data
        if (!transaction.Data.TryGetValue(TransactionDataKeys.ItemRef, out var itemRef))
            throw new InvalidOperationException("Trade transaction missing ItemRef");

        if (!transaction.Data.TryGetValue(TransactionDataKeys.Quantity, out var quantityStr) || !int.TryParse(quantityStr, out var quantity))
            throw new InvalidOperationException("Trade transaction missing or invalid Quantity");

        if (!transaction.Data.TryGetValue(TransactionDataKeys.IsBuying, out var isBuyingStr) || !bool.TryParse(isBuyingStr, out var isBuying))
            throw new InvalidOperationException("Trade transaction missing or invalid IsBuying");

        if (!transaction.Data.TryGetValue(TransactionDataKeys.TotalPrice, out var totalPriceStr) || !int.TryParse(totalPriceStr, out var totalPrice))
            throw new InvalidOperationException("Trade transaction missing or invalid TotalPrice");

        // Initialize Capabilities if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        // Initialize Stats if needed
        if (avatar.Stats == null)
        {
            avatar.Stats = new CharacterStats();
        }

        // Update credits
        var creditDelta = isBuying ? -totalPrice : totalPrice;
        avatar.Stats.Credits += creditDelta;
        RaiseCreditsChanged(avatar.Id, creditDelta, "Trade", tradeTransactionId);

        // Dispatch on the world's catalog to determine the real category of itemRef, not on
        // the avatar's current inventory. Previously the Consumables check ran first with
        // `|| isBuying`, so every purchased item became a Consumable regardless of its true type.
        if (_world.ConsumablesLookup.ContainsKey(itemRef))
        {
            var consumable = avatar.Capabilities.GetOrAddConsumable(itemRef);
            if (isBuying)
            {
                consumable.Quantity += quantity;
            }
            else
            {
                consumable.Quantity -= quantity;
                if (consumable.Quantity <= 0)
                {
                    var consumables = avatar.Capabilities.Consumables?.ToList() ?? new List<ConsumableEntry>();
                    consumables.RemoveAll(c => c.ConsumableRef == itemRef);
                    avatar.Capabilities.Consumables = consumables.ToArray();
                }
            }
        }
        else if (_world.BuildingMaterialsLookup.ContainsKey(itemRef))
        {
            var material = avatar.Capabilities.GetOrAddBuildingMaterial(itemRef);
            if (isBuying)
            {
                material.Quantity += quantity;
            }
            else
            {
                material.Quantity -= quantity;
                if (material.Quantity <= 0)
                {
                    var materials = avatar.Capabilities.BuildingMaterials?.ToList() ?? new List<BuildingMaterialEntry>();
                    materials.RemoveAll(m => m.BuildingMaterialRef == itemRef);
                    avatar.Capabilities.BuildingMaterials = materials.ToArray();
                }
            }
        }
        else if (_world.EquipmentLookup.ContainsKey(itemRef))
        {
            var equipment = avatar.Capabilities.Equipment?.ToList() ?? new List<EquipmentEntry>();
            if (isBuying)
            {
                if (!equipment.Any(e => e.EquipmentRef == itemRef))
                {
                    equipment.Add(new EquipmentEntry { EquipmentRef = itemRef, Condition = 1f });
                    avatar.Capabilities.Equipment = equipment.ToArray();
                }
            }
            else
            {
                equipment.RemoveAll(e => e.EquipmentRef == itemRef);
                avatar.Capabilities.Equipment = equipment.ToArray();
            }
        }
        else if (_world.ToolsLookup.ContainsKey(itemRef))
        {
            var tools = avatar.Capabilities.Tools?.ToList() ?? new List<ToolEntry>();
            if (isBuying)
            {
                if (!tools.Any(t => t.ToolRef == itemRef))
                {
                    tools.Add(new ToolEntry { ToolRef = itemRef, Condition = 1f });
                    avatar.Capabilities.Tools = tools.ToArray();
                }
            }
            else
            {
                tools.RemoveAll(t => t.ToolRef == itemRef);
                avatar.Capabilities.Tools = tools.ToArray();
            }
        }
        else if (_world.SpellsLookup.ContainsKey(itemRef))
        {
            var spells = avatar.Capabilities.Spells?.ToList() ?? new List<SpellEntry>();
            if (isBuying)
            {
                if (!spells.Any(s => s.SpellRef == itemRef))
                {
                    spells.Add(new SpellEntry { SpellRef = itemRef, Condition = 1f });
                    avatar.Capabilities.Spells = spells.ToArray();
                }
            }
            else
            {
                spells.RemoveAll(s => s.SpellRef == itemRef);
                avatar.Capabilities.Spells = spells.ToArray();
            }
        }
        else
        {
            var block = avatar.Capabilities.GetOrAddBlock(itemRef);
            if (isBuying)
            {
                block.Quantity += quantity;
            }
            else
            {
                block.Quantity -= quantity;
                if (block.Quantity <= 0)
                {
                    var blocks = avatar.Capabilities.Blocks?.ToList() ?? new List<BlockEntry>();
                    blocks.Remove(block);
                    avatar.Capabilities.Blocks = blocks.ToArray();
                }
            }
        }

        return avatar;
    }

    /// <inheritdoc/>
    public async Task<AvatarEntity> UpdateAvatarForBattleAsync(
        AvatarEntity avatar,
        SagaInstance sagaInstance,
        Guid battleStartedTransactionId,
        CancellationToken ct = default)
    {
        // Find BattleStarted transaction
        var battleStartedTx = sagaInstance.GetCommittedTransactions()
            .FirstOrDefault(t => t.TransactionId == battleStartedTransactionId && t.Type == SagaTransactionType.BattleStarted);

        if (battleStartedTx == null)
        {
            throw new InvalidOperationException($"BattleStarted transaction '{battleStartedTransactionId}' not found or not committed");
        }

        // Get ALL battle turn transactions (avatar AND enemy turns) for this battle
        var allBattleTurns = sagaInstance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                       t.Data.ContainsKey(TransactionDataKeys.BattleTransactionId) &&
                       t.Data[TransactionDataKeys.BattleTransactionId] == battleStartedTransactionId.ToString())
            .OrderBy(t => t.Data.TryGetValue(TransactionDataKeys.TurnNumber, out var turnStr) && int.TryParse(turnStr, out var turn) ? turn : 0)
            .ToList();

        if (!allBattleTurns.Any())
        {
            // No turns executed (shouldn't happen, but handle gracefully)
            return avatar;
        }

        // Initialize Stats if needed
        if (avatar.Stats == null)
        {
            avatar.Stats = new CharacterStats();
        }

        // Initialize Capabilities if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        // Track avatar's final health by looking at ALL turns where avatar was affected
        float? finalHealth = null;
        float? finalStamina = null;  // Battle uses "Energy" but avatar stores as "Stamina"
        string? finalAffinity = null;
        Dictionary<string, string>? finalCombatProfile = null;

        foreach (var turn in allBattleTurns)
        {
            var isAvatarTurn = turn.Data.TryGetValue(TransactionDataKeys.IsAvatarTurn, out var isAvatarStr) &&
                               bool.TryParse(isAvatarStr, out var parsedAvatarTurn) && parsedAvatarTurn;
            var isReaction = turn.Data.TryGetValue(TransactionDataKeys.ActionType, out var actionType) && actionType == "Reaction";

            // Preferred path: absolute post-turn avatar snapshots, written on every turn
            // since the round-3 fix. No turn-parity inference — companion turns, enemy
            // self-heals, avatar self-heals and reactions all read back correctly.
            if (turn.Data.TryGetValue(TransactionDataKeys.AvatarHealthAfterTurn, out var absHealthStr) &&
                BattleTransactionHelper.TryParseFloat(absHealthStr, out var absHealth))
            {
                finalHealth = absHealth;

                if (turn.Data.TryGetValue(TransactionDataKeys.AvatarEnergyAfterTurn, out var absEnergyStr) &&
                    BattleTransactionHelper.TryParseFloat(absEnergyStr, out var absEnergy))
                {
                    finalStamina = absEnergy;
                }
            }
            else if (isReaction)
            {
                // Legacy reaction tx: TargetHealthAfter/ActorEnergyAfter are the AVATAR's
                // (the avatar reacted to a telegraphed enemy attack)
                if (turn.Data.TryGetValue(TransactionDataKeys.TargetHealthAfter, out var reactionHealthStr) &&
                    BattleTransactionHelper.TryParseFloat(reactionHealthStr, out var reactionHealth))
                {
                    finalHealth = reactionHealth;
                }
                if (turn.Data.TryGetValue(TransactionDataKeys.ActorEnergyAfter, out var reactionEnergyStr) &&
                    BattleTransactionHelper.TryParseFloat(reactionEnergyStr, out var reactionEnergy))
                {
                    finalStamina = reactionEnergy;
                }
            }
            else if (isAvatarTurn)
            {
                // Legacy avatar turn: Actor = Avatar, Target = Enemy
                // Update avatar's stamina (battle transactions call it "Energy")
                if (turn.Data.TryGetValue(TransactionDataKeys.ActorEnergyAfter, out var energyStr) &&
                    BattleTransactionHelper.TryParseFloat(energyStr, out var energy))
                {
                    finalStamina = energy;  // Map Energy -> Stamina
                }
            }
            else
            {
                // Legacy non-avatar turn. Only treat TargetHealthAfter as the avatar's
                // health when the avatar actually was the target — companion turns and
                // enemy self-heals record the ENEMY's health there.
                var targetsAvatar = !turn.Data.TryGetValue(TransactionDataKeys.Target, out var targetName) ||
                                    string.IsNullOrEmpty(targetName) ||
                                    targetName == "Avatar" ||
                                    targetName == avatar.DisplayName;
                if (targetsAvatar &&
                    turn.Data.TryGetValue(TransactionDataKeys.TargetHealthAfter, out var healthStr) &&
                    BattleTransactionHelper.TryParseFloat(healthStr, out var health))
                {
                    finalHealth = health;
                }
            }

            // Affinity/loadout/equipment-condition ride on the avatar's own turns
            // (including reactions) regardless of which health path applied above
            if (isAvatarTurn || isReaction)
            {
                if (turn.Data.TryGetValue(TransactionDataKeys.AffinitySnapshot, out var affinity) && !string.IsNullOrEmpty(affinity))
                {
                    finalAffinity = affinity;
                }

                if (turn.Data.TryGetValue(TransactionDataKeys.LoadoutSlotSnapshot, out var loadoutSnapshot) && !string.IsNullOrEmpty(loadoutSnapshot))
                {
                    finalCombatProfile = ParseLoadoutSnapshot(loadoutSnapshot);
                    UpdateEquipmentConditionsFromSnapshot(avatar, loadoutSnapshot);
                }
            }
        }

        // Apply final state to avatar
        if (finalHealth.HasValue)
        {
            avatar.Stats.Health = finalHealth.Value;
        }

        if (finalStamina.HasValue)
        {
            avatar.Stats.Stamina = finalStamina.Value;  // Battle "Energy" -> Avatar "Stamina"
        }

        if (finalAffinity != null)
        {
            avatar.AffinityRef = finalAffinity;
        }

        if (finalCombatProfile != null)
        {
            avatar.CombatProfile = finalCombatProfile;
        }

        return avatar;
    }

    /// <inheritdoc/>
    public async Task<AvatarEntity> UpdateAvatarForEffectsAsync(
        AvatarEntity avatar,
        SagaInstance sagaInstance,
        Guid effectTransactionId,
        CancellationToken ct = default)
    {
        // Find the EffectApplied transaction
        var transaction = sagaInstance.GetCommittedTransactions()
            .FirstOrDefault(t => t.TransactionId == effectTransactionId && t.Type == SagaTransactionType.EffectApplied);

        if (transaction == null)
        {
            throw new InvalidOperationException($"EffectApplied transaction '{effectTransactionId}' not found or not committed");
        }

        // Initialize Stats if needed
        if (avatar.Stats == null)
        {
            avatar.Stats = new CharacterStats();
        }

        // Parse and apply stat effects (additive bonuses)
        if (transaction.Data.TryGetValue(TransactionDataKeys.Health, out var healthStr) && float.TryParse(healthStr, out var health) && health != 1.0f)
        {
            avatar.Stats.Health += health - 1.0f; // Treat as multiplier: 1.0 = no change, 1.5 = +50%
        }

        if (transaction.Data.TryGetValue(TransactionDataKeys.Stamina, out var staminaStr) && float.TryParse(staminaStr, out var stamina) && stamina != 1.0f)
        {
            avatar.Stats.Stamina += stamina - 1.0f;
        }

        if (transaction.Data.TryGetValue(TransactionDataKeys.Mana, out var manaStr) && float.TryParse(manaStr, out var mana) && mana != 1.0f)
        {
            avatar.Stats.Mana += mana - 1.0f;
        }

        if (transaction.Data.TryGetValue(TransactionDataKeys.Strength, out var strengthStr) && float.TryParse(strengthStr, out var strength) && strength != 0.0f)
        {
            avatar.Stats.Strength += strength; // Additive for non-vital stats
        }

        if (transaction.Data.TryGetValue(TransactionDataKeys.Defense, out var defenseStr) && float.TryParse(defenseStr, out var defense) && defense != 0.0f)
        {
            avatar.Stats.Defense += defense;
        }

        if (transaction.Data.TryGetValue(TransactionDataKeys.Speed, out var speedStr) && float.TryParse(speedStr, out var speed) && speed != 0.0f)
        {
            avatar.Stats.Speed += speed;
        }

        if (transaction.Data.TryGetValue(TransactionDataKeys.Magic, out var magicStr) && float.TryParse(magicStr, out var magic) && magic != 0.0f)
        {
            avatar.Stats.Magic += magic;
        }

        if (transaction.Data.TryGetValue(TransactionDataKeys.Credits, out var creditsStr) && float.TryParse(creditsStr, out var credits) && credits != 0.0f)
        {
            var creditDelta = (int)credits;
            avatar.Stats.Credits += creditDelta;
            RaiseCreditsChanged(avatar.Id, creditDelta, "Effect", effectTransactionId);
        }

        if (transaction.Data.TryGetValue(TransactionDataKeys.Experience, out var experienceStr) && float.TryParse(experienceStr, out var experience) && experience != 0.0f)
        {
            avatar.Stats.Experience += experience;
            // Levels derive from accumulated XP; never demote
            avatar.Stats.Level = Math.Max(avatar.Stats.Level,
                Domain.Rpg.Progression.LevelCurve.GetLevelForExperience(avatar.Stats.Experience));
        }

        return avatar;
    }

    /// <inheritdoc/>
    public async Task<AvatarEntity> UpdateAvatarForMiningAsync(
        AvatarEntity avatar,
        Dictionary<string, int> blocksMined,
        AvatarArchetype archetype,
        IWorldConfiguration worldConfig,
        CancellationToken ct = default)
    {
        // Initialize Capabilities if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        // Add mined blocks to inventory, skipping blocks that would exceed capacity
        var blockWeight = worldConfig.BlockWeight;
        foreach (var kvp in blocksMined)
        {
            var blockRef = kvp.Key;
            var quantity = kvp.Value;

            // Check how many blocks we can still carry
            var remaining = CarryWeightCalculator.GetRemainingCapacity(avatar.Capabilities, archetype, worldConfig);
            var canCarry = blockWeight > 0 ? (int)(remaining / blockWeight) : quantity;
            if (canCarry <= 0) break;

            var toAdd = Math.Min(quantity, canCarry);
            var block = avatar.Capabilities.GetOrAddBlock(blockRef);
            block.Quantity += toAdd;
        }

        return avatar;
    }

    /// <inheritdoc/>
    public async Task<AvatarEntity> UpdateAvatarForBuildingAsync(
        AvatarEntity avatar,
        Dictionary<string, int> materialsConsumed,
        CancellationToken ct = default)
    {
        // Initialize Capabilities if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        // Remove consumed materials from inventory
        foreach (var kvp in materialsConsumed)
        {
            var materialRef = kvp.Key;
            var quantity = kvp.Value;

            // Try blocks first
            var existingBlock = avatar.Capabilities.Blocks?.FirstOrDefault(b => b.BlockRef == materialRef);
            if (existingBlock != null)
            {
                existingBlock.Quantity -= quantity;
                if (existingBlock.Quantity <= 0)
                {
                    var blocks = avatar.Capabilities.Blocks?.ToList() ?? new List<BlockEntry>();
                    blocks.Remove(existingBlock);
                    avatar.Capabilities.Blocks = blocks.ToArray();
                }
                continue;
            }

            // Try building materials
            var existingMaterial = avatar.Capabilities.BuildingMaterials?.FirstOrDefault(m => m.BuildingMaterialRef == materialRef);
            if (existingMaterial != null)
            {
                existingMaterial.Quantity -= quantity;
                if (existingMaterial.Quantity <= 0)
                {
                    var materials = avatar.Capabilities.BuildingMaterials?.ToList() ?? new List<BuildingMaterialEntry>();
                    materials.RemoveAll(m => m.BuildingMaterialRef == materialRef);
                    avatar.Capabilities.BuildingMaterials = materials.ToArray();
                }
            }
        }

        return avatar;
    }

    /// <inheritdoc/>
    public async Task<AvatarEntity> UpdateAvatarForToolWearAsync(
        AvatarEntity avatar,
        string toolRef,
        float newCondition,
        CancellationToken ct = default)
    {
        // Initialize Capabilities if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        // Find and update tool condition
        var tool = avatar.Capabilities.Tools?.FirstOrDefault(t => t.ToolRef == toolRef);
        if (tool != null)
        {
            tool.Condition = Math.Max(0f, Math.Min(1f, newCondition)); // Clamp to 0-1

            // Remove tool if condition is 0 (broken)
            if (tool.Condition <= 0f)
            {
                var tools = avatar.Capabilities.Tools?.ToList() ?? new List<ToolEntry>();
                tools.RemoveAll(t => t.ToolRef == toolRef);
                avatar.Capabilities.Tools = tools.ToArray();
            }
        }

        return avatar;
    }

    /// <inheritdoc/>
    public async Task PersistAvatarAsync(AvatarEntity avatar, CancellationToken ct = default)
    {
        await _avatarRepository.SaveAvatarAsync(avatar);
    }

    /// <summary>
    /// Parse LoadoutSlotSnapshot into CombatProfile dictionary.
    /// Format: "SlotName:EquipmentRef:Condition,SlotName:EquipmentRef:Condition,..."
    /// Example: "MainHand:WoodenSword:0.85,Head:IronHelm:1.00"
    /// </summary>
    private Dictionary<string, string> ParseLoadoutSnapshot(string loadoutSnapshot)
    {
        var combatProfile = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(loadoutSnapshot))
            return combatProfile;

        var slots = loadoutSnapshot.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var slot in slots)
        {
            var parts = slot.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;  // Need at least SlotName:EquipmentRef

            var slotName = parts[0];
            var equipmentRef = parts[1];

            combatProfile[slotName] = equipmentRef;
        }

        return combatProfile;
    }

    /// <summary>
    /// Update equipment condition from LoadoutSlotSnapshot.
    /// Format: "SlotName:EquipmentRef:Condition,SlotName:EquipmentRef:Condition,..."
    /// Example: "MainHand:WoodenSword:0.85,Head:IronHelm:1.00"
    /// </summary>
    private void UpdateEquipmentConditionsFromSnapshot(AvatarEntity avatar, string loadoutSnapshot)
    {
        if (string.IsNullOrWhiteSpace(loadoutSnapshot)) return;

        var slots = loadoutSnapshot.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var slot in slots)
        {
            var parts = slot.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) continue;  // Need SlotName:EquipmentRef:Condition

            var equipmentRef = parts[1];
            if (!BattleTransactionHelper.TryParseFloat(parts[2], out var condition)) continue;

            // Update condition only for items the avatar still owns — the battle
            // snapshot must never re-create equipment sold/removed mid-battle
            var equipment = avatar.Capabilities?.GetEquipment(equipmentRef);
            if (equipment != null)
            {
                equipment.Condition = condition;
            }
        }
    }

    // Achievement unlock state is backed by the avatar's persisted Achievements list —
    // the previous static in-memory cache was empty on every app start, so every
    // already-unlocked achievement re-fired as "newly unlocked" each session. This
    // also makes the avatar's list the single unlock ledger instead of one of three
    // divergent stores.

    /// <inheritdoc/>
    public async Task<List<AchievementInstance>> GetAchievementInstancesAsync(Guid avatarId, CancellationToken ct = default)
    {
        var avatar = await _avatarRepository.LoadAvatarAsync<AvatarEntity>();
        if (avatar?.Achievements == null)
            return new List<AchievementInstance>();

        return avatar.Achievements
            .Where(a => !string.IsNullOrEmpty(a.AchievementRef))
            .Select(a => new AchievementInstance
            {
                InstanceId = a.AchievementRef,
                TemplateRef = a.AchievementRef,
                IsUnlocked = true,
                UnlockedAt = DateTime.TryParse(a.UnlockedDate, out var unlockedAt) ? unlockedAt : null
            })
            .ToList();
    }

    /// <inheritdoc/>
    public async Task UpdateAchievementInstancesAsync(Guid avatarId, List<AchievementInstance> instances, CancellationToken ct = default)
    {
        var avatar = await _avatarRepository.LoadAvatarAsync<AvatarEntity>();
        if (avatar == null)
            return;

        var achievements = avatar.Achievements?.ToList() ?? new List<AchievementEntry>();
        var changed = false;

        foreach (var instance in instances.Where(i => i.IsUnlocked && !string.IsNullOrEmpty(i.TemplateRef)))
        {
            if (achievements.Any(a => a.AchievementRef == instance.TemplateRef))
                continue;

            achievements.Add(new AchievementEntry
            {
                AchievementRef = instance.TemplateRef,
                UnlockedDate = (instance.UnlockedAt ?? DateTime.UtcNow).ToString("O"),
                ProgressPercentage = 1.0f
            });
            changed = true;
        }

        if (changed)
        {
            avatar.Achievements = achievements.ToArray();
            await PersistAvatarAsync(avatar, ct);
        }
    }
}
