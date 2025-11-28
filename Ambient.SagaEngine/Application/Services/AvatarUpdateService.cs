using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Entities;
using Ambient.Domain.Extensions;
using Ambient.SagaEngine.Contracts.Services;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Application.Services;

/// <summary>
/// Service for updating avatar state based on Saga transactions.
/// Applies transaction effects to avatar and handles persistence.
/// </summary>
public class AvatarUpdateService : IAvatarUpdateService
{
    private readonly IGameAvatarRepository _avatarRepository;

    public AvatarUpdateService(IGameAvatarRepository avatarRepository)
    {
        _avatarRepository = avatarRepository ?? throw new ArgumentNullException(nameof(avatarRepository));
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
        if (!transaction.Data.TryGetValue("ItemRef", out var itemRef))
            throw new InvalidOperationException("Trade transaction missing ItemRef");

        if (!transaction.Data.TryGetValue("Quantity", out var quantityStr) || !int.TryParse(quantityStr, out var quantity))
            throw new InvalidOperationException("Trade transaction missing or invalid Quantity");

        if (!transaction.Data.TryGetValue("IsBuying", out var isBuyingStr) || !bool.TryParse(isBuyingStr, out var isBuying))
            throw new InvalidOperationException("Trade transaction missing or invalid IsBuying");

        if (!transaction.Data.TryGetValue("TotalPrice", out var totalPriceStr) || !int.TryParse(totalPriceStr, out var totalPrice))
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
        if (isBuying)
        {
            avatar.Stats.Credits -= totalPrice;
        }
        else // selling
        {
            avatar.Stats.Credits += totalPrice;
        }

        // Update inventory - need to determine item type from avatar's current inventory
        // Try each item type in order

        // Try consumables
        var existingConsumable = avatar.Capabilities.Consumables?.FirstOrDefault(c => c.ConsumableRef == itemRef);
        if (existingConsumable != null || isBuying)
        {
            var consumable = avatar.Capabilities.GetOrAddConsumable(itemRef);
            if (isBuying)
            {
                consumable.Quantity += quantity;
            }
            else // selling
            {
                consumable.Quantity -= quantity;
                if (consumable.Quantity <= 0)
                {
                    var consumables = avatar.Capabilities.Consumables?.ToList() ?? new List<ConsumableEntry>();
                    consumables.RemoveAll(c => c.ConsumableRef == itemRef);
                    avatar.Capabilities.Consumables = consumables.ToArray();
                }
            }
            return avatar;
        }

        // Try blocks
        var existingBlock = avatar.Capabilities.Blocks?.FirstOrDefault(b => b.BlockRef == itemRef);
        if (existingBlock != null || isBuying)
        {
            var block = avatar.Capabilities.GetOrAddBlock(itemRef);
            if (isBuying)
            {
                block.Quantity += quantity;
            }
            else // selling
            {
                block.Quantity -= quantity;
                if (block.Quantity <= 0)
                {
                    var blocks = avatar.Capabilities.Blocks?.ToList() ?? new List<BlockEntry>();
                    blocks.RemoveAll(b => b.BlockRef == itemRef);
                    avatar.Capabilities.Blocks = blocks.ToArray();
                }
            }
            return avatar;
        }

        // Try building materials
        var existingMaterial = avatar.Capabilities.BuildingMaterials?.FirstOrDefault(m => m.BuildingMaterialRef == itemRef);
        if (existingMaterial != null || isBuying)
        {
            var material = avatar.Capabilities.GetOrAddBuildingMaterial(itemRef);
            if (isBuying)
            {
                material.Quantity += quantity;
            }
            else // selling
            {
                material.Quantity -= quantity;
                if (material.Quantity <= 0)
                {
                    var materials = avatar.Capabilities.BuildingMaterials?.ToList() ?? new List<BuildingMaterialEntry>();
                    materials.RemoveAll(m => m.BuildingMaterialRef == itemRef);
                    avatar.Capabilities.BuildingMaterials = materials.ToArray();
                }
            }
            return avatar;
        }

        // Try equipment (single item trade, quantity should be 1)
        var existingEquipment = avatar.Capabilities.Equipment?.FirstOrDefault(e => e.EquipmentRef == itemRef);
        if (existingEquipment != null)
        {
            if (isBuying)
            {
                // Already have it, skip (or could upgrade condition logic here)
            }
            else // selling
            {
                var equipment = avatar.Capabilities.Equipment?.ToList() ?? new List<EquipmentEntry>();
                equipment.RemoveAll(e => e.EquipmentRef == itemRef);
                avatar.Capabilities.Equipment = equipment.ToArray();
            }
            return avatar;
        }

        // Try tools (single item trade, quantity should be 1)
        var existingTool = avatar.Capabilities.Tools?.FirstOrDefault(t => t.ToolRef == itemRef);
        if (existingTool != null)
        {
            if (isBuying)
            {
                // Already have it, skip (or could upgrade condition logic here)
            }
            else // selling
            {
                var tools = avatar.Capabilities.Tools?.ToList() ?? new List<ToolEntry>();
                tools.RemoveAll(t => t.ToolRef == itemRef);
                avatar.Capabilities.Tools = tools.ToArray();
            }
            return avatar;
        }

        // Try spells (single item trade, quantity should be 1)
        var existingSpell = avatar.Capabilities.Spells?.FirstOrDefault(s => s.SpellRef == itemRef);
        if (existingSpell != null)
        {
            if (isBuying)
            {
                // Already have it, skip
            }
            else // selling
            {
                var spells = avatar.Capabilities.Spells?.ToList() ?? new List<SpellEntry>();
                spells.RemoveAll(s => s.SpellRef == itemRef);
                avatar.Capabilities.Spells = spells.ToArray();
            }
            return avatar;
        }

        // If buying and not found in any category, default to consumable
        if (isBuying)
        {
            var consumable = avatar.Capabilities.GetOrAddConsumable(itemRef);
            consumable.Quantity += quantity;
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

        // Get ALL battle turn transactions (player AND enemy turns) for this battle
        var allBattleTurns = sagaInstance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                       t.Data.ContainsKey("BattleTransactionId") &&
                       t.Data["BattleTransactionId"] == battleStartedTransactionId.ToString())
            .OrderBy(t => t.Data.TryGetValue("TurnNumber", out var turnStr) && int.TryParse(turnStr, out var turn) ? turn : 0)
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

        // Track player's final health by looking at ALL turns where player was affected
        float? finalHealth = null;
        float? finalStamina = null;  // Battle uses "Energy" but avatar stores as "Stamina"
        string? finalAffinity = null;
        Dictionary<string, string>? finalCombatProfile = null;

        foreach (var turn in allBattleTurns)
        {
            var isPlayerTurn = turn.Data.TryGetValue("IsPlayerTurn", out var isPlayerStr) && isPlayerStr == "True";

            if (isPlayerTurn)
            {
                // Player's turn: Actor = Player, Target = Enemy
                // Update player's stamina (battle transactions call it "Energy")
                if (turn.Data.TryGetValue("ActorEnergyAfter", out var energyStr) && float.TryParse(energyStr, out var energy))
                {
                    finalStamina = energy;  // Map Energy -> Stamina
                }

                // Update player's affinity if changed
                if (turn.Data.TryGetValue("AffinitySnapshot", out var affinity) && !string.IsNullOrEmpty(affinity))
                {
                    finalAffinity = affinity;
                }

                // Update player's combat profile (equipped slots) if changed
                if (turn.Data.TryGetValue("LoadoutSlotSnapshot", out var loadoutSnapshot) && !string.IsNullOrEmpty(loadoutSnapshot))
                {
                    finalCombatProfile = ParseLoadoutSnapshot(loadoutSnapshot);
                }

                // Update equipment condition from LoadoutSlotSnapshot
                if (turn.Data.TryGetValue("LoadoutSlotSnapshot", out var equipmentSnapshot) && !string.IsNullOrEmpty(equipmentSnapshot))
                {
                    UpdateEquipmentConditionsFromSnapshot(avatar, equipmentSnapshot);
                }
            }
            else
            {
                // Enemy's turn: Actor = Enemy, Target = Player
                // Player's health is recorded as TargetHealthAfter (enemy damaged player)
                if (turn.Data.TryGetValue("TargetHealthAfter", out var healthStr) && float.TryParse(healthStr, out var health))
                {
                    finalHealth = health;
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
    public async Task<AvatarEntity> UpdateAvatarForLootAsync(
        AvatarEntity avatar,
        SagaInstance sagaInstance,
        Guid lootTransactionId,
        CancellationToken ct = default)
    {
        // Find the LootAwarded transaction
        var transaction = sagaInstance.GetCommittedTransactions()
            .FirstOrDefault(t => t.TransactionId == lootTransactionId && t.Type == SagaTransactionType.LootAwarded);

        if (transaction == null)
        {
            throw new InvalidOperationException($"LootAwarded transaction '{lootTransactionId}' not found or not committed");
        }

        // Initialize Capabilities and Stats if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        if (avatar.Stats == null)
        {
            avatar.Stats = new CharacterStats();
        }

        // Parse and transfer equipment
        if (transaction.Data.TryGetValue("Equipment", out var equipmentData) && !string.IsNullOrEmpty(equipmentData))
        {
            var equipmentEntries = equipmentData.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in equipmentEntries)
            {
                var parts = entry.Split(':');
                if (parts.Length >= 2 && float.TryParse(parts[1], out var condition))
                {
                    var equipmentRef = parts[0];
                    var equipment = avatar.Capabilities.GetOrAddEquipment(equipmentRef);
                    equipment.Condition = Math.Max(equipment.Condition, condition); // Keep best condition if duplicate
                }
            }
        }

        // Parse and transfer consumables
        if (transaction.Data.TryGetValue("Consumables", out var consumablesData) && !string.IsNullOrEmpty(consumablesData))
        {
            var consumableEntries = consumablesData.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in consumableEntries)
            {
                var parts = entry.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var quantity))
                {
                    var consumableRef = parts[0];
                    var consumable = avatar.Capabilities.GetOrAddConsumable(consumableRef);
                    consumable.Quantity += quantity;
                }
            }
        }

        // Parse and transfer spells
        if (transaction.Data.TryGetValue("Spells", out var spellsData) && !string.IsNullOrEmpty(spellsData))
        {
            var spellEntries = spellsData.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in spellEntries)
            {
                var parts = entry.Split(':');
                var spellRef = parts[0];

                // Add spell if not already known
                var spell = avatar.Capabilities.GetSpell(spellRef);
                if (spell == null)
                {
                    var newSpell = avatar.Capabilities.GetOrAddSpell(spellRef);
                    // If condition was provided, use it
                    if (parts.Length >= 2 && float.TryParse(parts[1], out var condition))
                    {
                        newSpell.Condition = condition;
                    }
                }
            }
        }

        // Parse and transfer blocks
        if (transaction.Data.TryGetValue("Blocks", out var blocksData) && !string.IsNullOrEmpty(blocksData))
        {
            var blockEntries = blocksData.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in blockEntries)
            {
                var parts = entry.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var quantity))
                {
                    var blockRef = parts[0];
                    var block = avatar.Capabilities.GetOrAddBlock(blockRef);
                    block.Quantity += quantity;
                }
            }
        }

        // Parse and transfer tools
        if (transaction.Data.TryGetValue("Tools", out var toolsData) && !string.IsNullOrEmpty(toolsData))
        {
            var toolEntries = toolsData.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in toolEntries)
            {
                var parts = entry.Split(':');
                if (parts.Length >= 2 && float.TryParse(parts[1], out var condition))
                {
                    var toolRef = parts[0];
                    var tool = avatar.Capabilities.GetOrAddTool(toolRef);
                    tool.Condition = Math.Max(tool.Condition, condition); // Keep best condition if duplicate
                }
            }
        }

        // Parse and transfer building materials
        if (transaction.Data.TryGetValue("BuildingMaterials", out var materialsData) && !string.IsNullOrEmpty(materialsData))
        {
            var materialEntries = materialsData.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in materialEntries)
            {
                var parts = entry.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var quantity))
                {
                    var materialRef = parts[0];
                    var material = avatar.Capabilities.GetOrAddBuildingMaterial(materialRef);
                    material.Quantity += quantity;
                }
            }
        }

        // Transfer credits
        if (transaction.Data.TryGetValue("Credits", out var creditsData) && int.TryParse(creditsData, out var credits))
        {
            avatar.Stats.Credits += credits;
        }

        return avatar;
    }

    /// <inheritdoc/>
    public async Task<AvatarEntity> AddQuestTokenAsync(
        AvatarEntity avatar,
        string questTokenRef,
        CancellationToken ct = default)
    {
        // Initialize Capabilities if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        // Add quest token (extension method handles duplicates)
        avatar.Capabilities.AddQuestToken(questTokenRef);

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
        if (transaction.Data.TryGetValue("Health", out var healthStr) && float.TryParse(healthStr, out var health) && health != 1.0f)
        {
            avatar.Stats.Health += health - 1.0f; // Treat as multiplier: 1.0 = no change, 1.5 = +50%
        }

        if (transaction.Data.TryGetValue("Stamina", out var staminaStr) && float.TryParse(staminaStr, out var stamina) && stamina != 1.0f)
        {
            avatar.Stats.Stamina += stamina - 1.0f;
        }

        if (transaction.Data.TryGetValue("Mana", out var manaStr) && float.TryParse(manaStr, out var mana) && mana != 1.0f)
        {
            avatar.Stats.Mana += mana - 1.0f;
        }

        if (transaction.Data.TryGetValue("Strength", out var strengthStr) && float.TryParse(strengthStr, out var strength) && strength != 0.0f)
        {
            avatar.Stats.Strength += strength; // Additive for non-vital stats
        }

        if (transaction.Data.TryGetValue("Defense", out var defenseStr) && float.TryParse(defenseStr, out var defense) && defense != 0.0f)
        {
            avatar.Stats.Defense += defense;
        }

        if (transaction.Data.TryGetValue("Speed", out var speedStr) && float.TryParse(speedStr, out var speed) && speed != 0.0f)
        {
            avatar.Stats.Speed += speed;
        }

        if (transaction.Data.TryGetValue("Magic", out var magicStr) && float.TryParse(magicStr, out var magic) && magic != 0.0f)
        {
            avatar.Stats.Magic += magic;
        }

        if (transaction.Data.TryGetValue("Credits", out var creditsStr) && float.TryParse(creditsStr, out var credits) && credits != 0.0f)
        {
            avatar.Stats.Credits += (int)credits;
        }

        if (transaction.Data.TryGetValue("Experience", out var experienceStr) && float.TryParse(experienceStr, out var experience) && experience != 0.0f)
        {
            avatar.Stats.Experience += experience;
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
    /// Example: "RightHand:WoodenSword:0.85,Head:IronHelm:1.00"
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
    /// Example: "RightHand:WoodenSword:0.85,Head:IronHelm:1.00"
    /// </summary>
    private void UpdateEquipmentConditionsFromSnapshot(AvatarEntity avatar, string loadoutSnapshot)
    {
        if (string.IsNullOrWhiteSpace(loadoutSnapshot)) return;

        // Initialize Capabilities if needed
        if (avatar.Capabilities == null)
        {
            avatar.Capabilities = new ItemCollection();
        }

        var slots = loadoutSnapshot.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var slot in slots)
        {
            var parts = slot.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) continue;  // Need SlotName:EquipmentRef:Condition

            var equipmentRef = parts[1];
            if (!float.TryParse(parts[2], out var condition)) continue;

            // Update the equipment condition in avatar's inventory
            var equipment = avatar.Capabilities.GetOrAddEquipment(equipmentRef);
            equipment.Condition = condition;
        }
    }
}
