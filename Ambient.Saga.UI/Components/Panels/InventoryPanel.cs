using Ambient.Domain;
using Ambient.Domain.Hotbar;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Inventory panel showing what is available to the character.
/// Includes equipment, consumables, spells, tools, blocks, materials, and quest tokens.
/// Accessible via I key.
///
/// Equipment items have Equip/Unequip actions following the canonical RPG pattern:
/// - Inventory = Action space (equip/unequip verbs)
/// - Character panel = State space (read-only, shows equipped items)
/// </summary>
public class InventoryPanel
{
    // Track pending equip operations for async feedback
    private HashSet<string> _pendingEquipOperations = new();
    // Track pending consumable use operations for async feedback
    private HashSet<string> _pendingUseOperations = new();

    // Hotbar assignment state
    private bool _showHotbarAssignPopup = false;
    private bool _openHotbarPopupRequested = false;
    private HotbarItemType _assignItemType;
    private string? _assignItemRef;
    private string? _assignItemName;
    private Vector2 _popupPosition;

    // Layout constants
    private const float AssignButtonWidth = 25f;  // "[+]" button
    private const float ActionButtonWidth = 55f;  // "Equip"/"Unequip"/"Use" buttons (fixed width)
    private const float ButtonSpacing = 5f;
    private const float ButtonAreaWidth = AssignButtonWidth + ActionButtonWidth + ButtonSpacing * 2 + 10f; // Total reserved space

    public void Render(SagaMainViewModel viewModel)
    {
        if (viewModel.PlayerAvatar?.Capabilities == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No avatar created");
            ImGui.TextWrapped("Enter a world to select archetype");
            return;
        }

        var caps = viewModel.PlayerAvatar.Capabilities;
        var hasBlockProvider = viewModel.CurrentWorld?.BlockProvider != null;
        var columnCount = hasBlockProvider ? 3 : 2;

        // Calculate column width
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var columnSpacing = 10f;
        var columnWidth = (availableWidth - (columnSpacing * (columnCount - 1))) / columnCount;
        var contentHeight = ImGui.GetContentRegionAvail().Y;

        // Column 1: Equipment
        ImGui.BeginChild("EquipmentColumn", new Vector2(columnWidth, contentHeight), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        ImGui.TextColored(new Vector4(1, 0.7f, 0.7f, 1), "EQUIPMENT");
        ImGui.Separator();
        RenderEquipmentBySlot(viewModel, caps);
        ImGui.EndChild();

        ImGui.SameLine(0, columnSpacing);

        // Column 2: RPG Elements (Consumables, Spells, Quest Tokens)
        ImGui.BeginChild("RpgColumn", new Vector2(columnWidth, contentHeight), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        ImGui.TextColored(new Vector4(0.7f, 1f, 0.7f, 1), "ITEMS & MAGIC");
        ImGui.Separator();
        RenderConsumables(viewModel, caps);
        RenderSpells(viewModel, caps);
        RenderQuestTokens(viewModel, caps);
        ImGui.EndChild();

        // Column 3: Block-related (only if BlockProvider exists)
        if (hasBlockProvider)
        {
            ImGui.SameLine(0, columnSpacing);

            ImGui.BeginChild("BlocksColumn", new Vector2(columnWidth, contentHeight), ImGuiChildFlags.None, ImGuiWindowFlags.None);
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1), "BUILDING");
            ImGui.Separator();
            RenderTools(viewModel, caps);
            RenderBlocks(viewModel, caps);
            RenderMaterials(viewModel, caps);
            ImGui.EndChild();
        }

        // Render hotbar popup at root level (after all child windows)
        RenderHotbarAssignPopup(viewModel);
    }

    /// <summary>
    /// Renders the Consumables section.
    /// </summary>
    private void RenderConsumables(SagaMainViewModel viewModel, ItemCollection caps)
    {
        if (ImGui.CollapsingHeader($"Consumables ({caps.Consumables?.Length ?? 0})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (caps.Consumables != null && caps.Consumables.Length > 0)
            {
                foreach (var consumable in caps.Consumables)
                {
                    var consumableItem = viewModel.CurrentWorld?.Gameplay?.Consumables?.FirstOrDefault(c => c.RefName == consumable.ConsumableRef);
                    var name = consumableItem?.DisplayName ?? consumable.ConsumableRef;
                    var isPending = _pendingUseOperations.Contains(consumable.ConsumableRef);

                    ImGui.Indent();

                    var maxTextWidth = GetAvailableTextWidth();
                    var quantityText = $" x{consumable.Quantity}";
                    var truncatedName = TruncateToFit(name, maxTextWidth - ImGui.CalcTextSize(quantityText).X - 30f);
                    var treeNodeOpen = ImGui.TreeNode($"{truncatedName}{quantityText}##{consumable.ConsumableRef}");

                    if (truncatedName != name && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{name}{quantityText}");
                    }

                    ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ButtonAreaWidth);
                    RenderHotbarAssignButton(HotbarItemType.Consumable, consumable.ConsumableRef, name);
                    ImGui.SameLine();
                    var buttonSize = new Vector2(ActionButtonWidth, ImGui.GetFrameHeight());
                    if (isPending)
                    {
                        ImGui.BeginDisabled();
                        ImGui.Button("...", buttonSize);
                        ImGui.EndDisabled();
                    }
                    else if (consumable.Quantity > 0)
                    {
                        if (ImGui.Button($"Use##{consumable.ConsumableRef}", buttonSize))
                        {
                            _pendingUseOperations.Add(consumable.ConsumableRef);
                            _ = UseConsumableAsync(viewModel, consumable.ConsumableRef);
                        }
                    }
                    else
                    {
                        ImGui.Dummy(buttonSize);
                    }

                    if (treeNodeOpen)
                    {
                        if (consumableItem != null)
                        {
                            if (!string.IsNullOrEmpty(consumableItem.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), consumableItem.Description);
                                ImGui.Spacing();
                            }
                            if (consumableItem.Effects != null)
                            {
                                ImGuiHelpers.RenderAttributes(consumableItem.Effects);
                            }
                        }
                        ImGui.TreePop();
                    }

                    ImGui.Unindent();
                }
            }
            else
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No consumables");
                ImGui.Unindent();
            }
        }
    }

    /// <summary>
    /// Renders the Spells section.
    /// </summary>
    private void RenderSpells(SagaMainViewModel viewModel, ItemCollection caps)
    {
        if (ImGui.CollapsingHeader($"Spells ({caps.Spells?.Length ?? 0})"))
        {
            if (caps.Spells != null && caps.Spells.Length > 0)
            {
                foreach (var spell in caps.Spells)
                {
                    var spellItem = viewModel.CurrentWorld?.Gameplay?.Spells?.FirstOrDefault(s => s.RefName == spell.SpellRef);
                    var name = spellItem?.DisplayName ?? spell.SpellRef;

                    ImGui.Indent();

                    var maxTextWidth = GetAvailableTextWidth();
                    var conditionText = $" ({spell.Condition:P0})";
                    var truncatedName = TruncateToFit(name, maxTextWidth - ImGui.CalcTextSize(conditionText).X - 30f);
                    var treeNodeOpen = ImGui.TreeNode($"{truncatedName}{conditionText}##{spell.SpellRef}");

                    if (truncatedName != name && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{name}{conditionText}");
                    }

                    if (treeNodeOpen)
                    {
                        if (spellItem != null)
                        {
                            if (!string.IsNullOrEmpty(spellItem.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), spellItem.Description);
                                ImGui.Spacing();
                            }
                            if (spellItem.Effects != null)
                            {
                                ImGuiHelpers.RenderAttributes(spellItem.Effects);
                            }
                        }
                        ImGui.TreePop();
                    }

                    ImGui.Unindent();
                }
            }
            else
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No spells");
                ImGui.Unindent();
            }
        }
    }

    /// <summary>
    /// Renders the Quest Tokens section.
    /// </summary>
    private void RenderQuestTokens(SagaMainViewModel viewModel, ItemCollection caps)
    {
        if (caps.QuestTokens != null && caps.QuestTokens.Length > 0)
        {
            if (ImGui.CollapsingHeader($"Quest Tokens ({caps.QuestTokens.Length})"))
            {
                foreach (var token in caps.QuestTokens)
                {
                    var tokenDef = viewModel.CurrentWorld?.Gameplay?.QuestTokens?.FirstOrDefault(t => t.RefName == token.QuestTokenRef);
                    var name = tokenDef?.DisplayName ?? token.QuestTokenRef;

                    ImGui.Indent();
                    ImGui.BulletText(name);
                    if (tokenDef != null && !string.IsNullOrEmpty(tokenDef.Description))
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"- {tokenDef.Description}");
                    }
                    ImGui.Unindent();
                }
            }
        }
    }

    /// <summary>
    /// Renders the Tools section.
    /// </summary>
    private void RenderTools(SagaMainViewModel viewModel, ItemCollection caps)
    {
        if (ImGui.CollapsingHeader($"Tools ({caps.Tools?.Length ?? 0})"))
        {
            if (caps.Tools != null && caps.Tools.Length > 0)
            {
                foreach (var tool in caps.Tools)
                {
                    var toolDef = viewModel.CurrentWorld?.Gameplay?.Tools?.FirstOrDefault(t => t.RefName == tool.ToolRef);
                    var toolName = toolDef?.DisplayName ?? tool.ToolRef;
                    ImGui.Indent();
                    var maxTextWidth = GetAvailableTextWidth();
                    var conditionText = $" ({tool.Condition:P0})";
                    var truncatedName = TruncateToFit(toolName, maxTextWidth - ImGui.CalcTextSize(conditionText).X - 30f);
                    ImGui.BulletText($"{truncatedName}{conditionText}");
                    if (truncatedName != toolName && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{toolName}{conditionText}");
                    }
                    ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ButtonAreaWidth);
                    RenderHotbarAssignButton(HotbarItemType.Tool, tool.ToolRef, toolName);
                    ImGui.Unindent();
                }
            }
            else
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No tools");
                ImGui.Unindent();
            }
        }
    }

    /// <summary>
    /// Renders the Blocks section.
    /// </summary>
    private void RenderBlocks(SagaMainViewModel viewModel, ItemCollection caps)
    {
        if (ImGui.CollapsingHeader($"Blocks ({caps.Blocks?.Length ?? 0})"))
        {
            if (caps.Blocks != null && caps.Blocks.Length > 0)
            {
                foreach (var block in caps.Blocks)
                {
                    var blockDef = viewModel.CurrentWorld?.BlockProvider?.GetBlockByRefName(block.BlockRef);
                    var blockName = blockDef?.DisplayName ?? block.BlockRef;
                    ImGui.Indent();
                    var maxTextWidth = GetAvailableTextWidth();
                    var quantityText = $" x{block.Quantity}";
                    var truncatedName = TruncateToFit(blockName, maxTextWidth - ImGui.CalcTextSize(quantityText).X - 30f);
                    ImGui.BulletText($"{truncatedName}{quantityText}");
                    if (truncatedName != blockName && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{blockName}{quantityText}");
                    }
                    ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ButtonAreaWidth);
                    RenderHotbarAssignButton(HotbarItemType.Block, block.BlockRef, blockName);
                    ImGui.Unindent();
                }
            }
            else
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No blocks");
                ImGui.Unindent();
            }
        }
    }

    /// <summary>
    /// Renders the Materials section.
    /// </summary>
    private void RenderMaterials(SagaMainViewModel viewModel, ItemCollection caps)
    {
        if (ImGui.CollapsingHeader($"Materials ({caps.BuildingMaterials?.Length ?? 0})"))
        {
            if (caps.BuildingMaterials != null && caps.BuildingMaterials.Length > 0)
            {
                foreach (var material in caps.BuildingMaterials)
                {
                    var materialItem = viewModel.CurrentWorld?.TryGetBuildingMaterialByRefName(material.BuildingMaterialRef);
                    var name = materialItem?.DisplayName ?? material.BuildingMaterialRef;

                    ImGui.Indent();

                    var maxTextWidth = GetAvailableTextWidth();
                    var quantityText = $" x{material.Quantity}";
                    var truncatedName = TruncateToFit(name, maxTextWidth - ImGui.CalcTextSize(quantityText).X - 30f);
                    var treeNodeOpen = ImGui.TreeNode($"{truncatedName}{quantityText}##{material.BuildingMaterialRef}");

                    if (truncatedName != name && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{name}{quantityText}");
                    }

                    ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ButtonAreaWidth);
                    RenderHotbarAssignButton(HotbarItemType.BuildingMaterial, material.BuildingMaterialRef, name);

                    if (treeNodeOpen)
                    {
                        if (materialItem != null)
                        {
                            if (!string.IsNullOrEmpty(materialItem.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), materialItem.Description);
                                ImGui.Spacing();
                            }
                            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Price: {materialItem.WholesalePrice}");
                            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"Markup: {materialItem.MerchantMarkupMultiplier}x");
                        }
                        ImGui.TreePop();
                    }

                    ImGui.Unindent();
                }
            }
            else
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No materials");
                ImGui.Unindent();
            }
        }
    }

    private async Task EquipItemAsync(SagaMainViewModel viewModel, string equipmentRef, string slotRef)
    {
        try
        {
            await viewModel.EquipItemAsync(equipmentRef, slotRef);
        }
        finally
        {
            _pendingEquipOperations.Remove(equipmentRef);
        }
    }

    private async Task UnequipItemAsync(SagaMainViewModel viewModel, string equipmentRef, string slotRef)
    {
        try
        {
            // Pass null/empty equipmentRef to unequip the slot
            await viewModel.EquipItemAsync(null, slotRef);
        }
        finally
        {
            _pendingEquipOperations.Remove(equipmentRef);
        }
    }

    private async Task UseConsumableAsync(SagaMainViewModel viewModel, string consumableRef)
    {
        try
        {
            await viewModel.UseConsumableAsync(consumableRef);
        }
        finally
        {
            _pendingUseOperations.Remove(consumableRef);
        }
    }

    /// <summary>
    /// Renders equipment organized by loadout slot.
    /// </summary>
    private void RenderEquipmentBySlot(SagaMainViewModel viewModel, ItemCollection caps)
    {
        var world = viewModel.CurrentWorld;
        if (world == null) return;

        // Get all loadout slots from the world
        var loadoutSlots = world.LoadoutSlotsLookup?.Values.ToList() ?? new List<LoadoutSlot>();
        if (loadoutSlots.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No equipment slots defined");
            return;
        }

        // Build a lookup of equipment by slot
        var equipmentBySlot = new Dictionary<string, List<(EquipmentEntry entry, Equipment? def)>>();
        foreach (var slot in loadoutSlots)
        {
            equipmentBySlot[slot.RefName] = new List<(EquipmentEntry entry, Equipment? def)>();
        }

        // Group player's equipment by slot
        if (caps.Equipment != null)
        {
            foreach (var equip in caps.Equipment)
            {
                var equipDef = world.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == equip.EquipmentRef);
                var slotRef = equipDef?.SlotRef ?? "Unknown";
                if (equipmentBySlot.ContainsKey(slotRef))
                {
                    equipmentBySlot[slotRef].Add((equip, equipDef));
                }
            }
        }

        // Render each slot
        foreach (var slot in loadoutSlots)
        {
            var slotEquipment = equipmentBySlot.GetValueOrDefault(slot.RefName, new List<(EquipmentEntry entry, Equipment? def)>());
            var equippedItem = slotEquipment.FirstOrDefault(e => viewModel.IsItemEquipped(e.entry.EquipmentRef));
            var hasEquipped = equippedItem.entry != null;

            // Build header text
            var slotDisplayName = slot.DisplayName ?? slot.RefName;
            var headerText = hasEquipped
                ? $"{slotDisplayName}: {equippedItem.def?.DisplayName ?? equippedItem.entry!.EquipmentRef}"
                : $"{slotDisplayName}";

            // Color the header based on equipped status
            if (hasEquipped)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 1f, 0.5f, 1f));
            }

            var isOpen = ImGui.CollapsingHeader($"{headerText}##{slot.RefName}");

            if (hasEquipped)
            {
                ImGui.PopStyleColor();
            }

            // Show slot description on second line (helpful for non-English slot names)
            if (!string.IsNullOrEmpty(slot.Description))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), slot.Description);
                ImGui.Unindent();
            }

            if (isOpen)
            {
                if (slotEquipment.Count == 0)
                {
                    ImGui.Indent();
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "(empty)");
                    ImGui.Unindent();
                }
                else
                {
                    foreach (var (equip, equipDef) in slotEquipment)
                    {
                        RenderEquipmentItem(viewModel, equip, equipDef, slot.RefName);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Renders a single equipment item within a slot section.
    /// </summary>
    private void RenderEquipmentItem(SagaMainViewModel viewModel, EquipmentEntry equip, Equipment? equipItem, string slotRef)
    {
        var name = equipItem?.DisplayName ?? equip.EquipmentRef;
        var isEquipped = viewModel.IsItemEquipped(equip.EquipmentRef);
        var isPending = _pendingEquipOperations.Contains(equip.EquipmentRef);

        ImGui.Indent();

        // Expandable header for each equipment item with equipped indicator
        var maxTextWidth = GetAvailableTextWidth();
        var statusSuffix = isEquipped ? " [EQUIPPED]" : "";
        var conditionText = $" ({equip.Condition:P0})";
        var fullHeaderText = $"{name}{conditionText}{statusSuffix}";
        var truncatedName = TruncateToFit(name, maxTextWidth - ImGui.CalcTextSize(conditionText + statusSuffix).X - 30f);
        var headerText = $"{truncatedName}{conditionText}{statusSuffix}";
        var treeNodeOpen = ImGui.TreeNode($"{headerText}##{equip.EquipmentRef}");

        // Show full name on hover if truncated
        if (truncatedName != name && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(fullHeaderText);
        }

        // Hotbar assign button and Equip/Unequip button on same line as header
        ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ButtonAreaWidth);
        RenderHotbarAssignButton(HotbarItemType.Equipment, equip.EquipmentRef, name);
        ImGui.SameLine();
        var buttonSize = new Vector2(ActionButtonWidth, ImGui.GetFrameHeight());
        if (isPending)
        {
            ImGui.BeginDisabled();
            ImGui.Button("...", buttonSize);
            ImGui.EndDisabled();
        }
        else if (isEquipped)
        {
            if (ImGui.Button($"Unequip##{equip.EquipmentRef}", buttonSize))
            {
                _pendingEquipOperations.Add(equip.EquipmentRef);
                _ = UnequipItemAsync(viewModel, equip.EquipmentRef, slotRef);
            }
        }
        else
        {
            if (ImGui.Button($"Equip##{equip.EquipmentRef}", buttonSize))
            {
                _pendingEquipOperations.Add(equip.EquipmentRef);
                _ = EquipItemAsync(viewModel, equip.EquipmentRef, slotRef);
            }
        }

        if (treeNodeOpen)
        {
            if (equipItem != null)
            {
                // Description
                if (!string.IsNullOrEmpty(equipItem.Description))
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), equipItem.Description);
                    ImGui.Spacing();
                }

                // Effects
                if (equipItem.Effects != null)
                {
                    ImGuiHelpers.RenderAttributes(equipItem.Effects);
                }
            }

            ImGui.TreePop();
        }

        ImGui.Unindent();
    }

    /// <summary>
    /// Renders an "Assign" button that opens the hotbar slot selection popup.
    /// </summary>
    private void RenderHotbarAssignButton(HotbarItemType itemType, string refName, string displayName)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.3f, 0.4f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.4f, 0.5f, 1f));
        if (ImGui.SmallButton($"[+]##{itemType}_{refName}"))
        {
            _showHotbarAssignPopup = true;
            _openHotbarPopupRequested = true;
            _assignItemType = itemType;
            _assignItemRef = refName;
            _assignItemName = displayName;
            _popupPosition = ImGui.GetMousePos();
        }
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Assign to hotbar slot (1-9)");
        }
    }

    /// <summary>
    /// Renders the hotbar assignment popup. Call this once per frame at root level.
    /// </summary>
    private void RenderHotbarAssignPopup(SagaMainViewModel viewModel)
    {
        if (!_showHotbarAssignPopup) return;

        var avatar = viewModel.PlayerAvatar;
        if (avatar == null) return;

        // Open popup if requested (deferred from button click)
        if (_openHotbarPopupRequested)
        {
            ImGui.OpenPopup("HotbarAssignPopup");
            _openHotbarPopupRequested = false;
        }

        // Position near where clicked
        ImGui.SetNextWindowPos(_popupPosition, ImGuiCond.Appearing);
        var popupSize = new Vector2(200, 0);
        ImGui.SetNextWindowSize(popupSize, ImGuiCond.Always);

        if (ImGui.BeginPopup("HotbarAssignPopup"))
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.5f, 1f), $"Assign: {_assignItemName}");
            ImGui.Separator();
            ImGui.Spacing();

            // Show 9 slots
            for (int i = 0; i < 9; i++)
            {
                var slot = avatar.Hotbar[i];
                var slotLabel = slot.IsEmpty
                    ? $"Slot {i + 1}: (empty)"
                    : $"Slot {i + 1}: {GetSlotItemName(viewModel, slot)}";

                if (ImGui.Selectable(slotLabel))
                {
                    // Assign the item to this slot
                    avatar.Hotbar[i].Set(_assignItemType, _assignItemRef!);
                    _showHotbarAssignPopup = false;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.Spacing();
            ImGui.Separator();

            if (ImGui.Button("Cancel", new Vector2(ImGuiSizes.Fill, 0)))
            {
                _showHotbarAssignPopup = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
        else
        {
            // Popup was closed (clicked outside)
            _showHotbarAssignPopup = false;
        }
    }

    private string GetSlotItemName(SagaMainViewModel viewModel, HotbarSlot slot)
    {
        if (slot.IsEmpty || string.IsNullOrEmpty(slot.RefName))
            return "(empty)";

        var world = viewModel.CurrentWorld;
        if (world == null)
            return slot.RefName;

        return slot.ItemType switch
        {
            HotbarItemType.Tool => world.TryGetToolByRefName(slot.RefName)?.DisplayName ?? slot.RefName,
            HotbarItemType.Block => world.BlockProvider?.GetBlockByRefName(slot.RefName)?.DisplayName ?? slot.RefName,
            HotbarItemType.BuildingMaterial => world.TryGetBuildingMaterialByRefName(slot.RefName)?.DisplayName ?? slot.RefName,
            HotbarItemType.Consumable => world.Gameplay?.Consumables?.FirstOrDefault(c => c.RefName == slot.RefName)?.DisplayName ?? slot.RefName,
            HotbarItemType.Equipment => world.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == slot.RefName)?.DisplayName ?? slot.RefName,
            _ => slot.RefName
        };
    }

    /// <summary>
    /// Gets the maximum width available for item text, accounting for indentation and button area.
    /// Uses GetContentRegionAvail() for accurate available space calculation.
    /// </summary>
    private float GetAvailableTextWidth()
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        return availableWidth - ButtonAreaWidth - 10f; // Small padding for safety
    }

    /// <summary>
    /// Truncates text to fit within maxWidth, adding ellipsis if needed.
    /// </summary>
    private string TruncateToFit(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var fullSize = ImGui.CalcTextSize(text);
        if (fullSize.X <= maxWidth) return text;

        var ellipsis = "...";
        var ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        var targetWidth = maxWidth - ellipsisWidth;

        if (targetWidth <= 0) return ellipsis;

        // Binary search would be more efficient, but this is simple and works
        for (int len = text.Length - 1; len > 0; len--)
        {
            var truncated = text[..len];
            if (ImGui.CalcTextSize(truncated).X <= targetWidth)
            {
                return truncated + ellipsis;
            }
        }

        return ellipsis;
    }
}
