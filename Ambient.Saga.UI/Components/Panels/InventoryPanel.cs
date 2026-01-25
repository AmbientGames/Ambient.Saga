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
    private HotbarItemType _assignItemType;
    private string? _assignItemRef;
    private string? _assignItemName;

    // Layout constants
    private const float AssignButtonWidth = 25f;  // "[+]" button
    private const float ActionButtonWidth = 55f;  // "Equip"/"Unequip"/"Use" buttons (fixed width)
    private const float ButtonSpacing = 5f;
    private const float ButtonAreaWidth = AssignButtonWidth + ActionButtonWidth + ButtonSpacing * 2 + 10f; // Total reserved space

    public void Render(SagaMainViewModel viewModel)
    {
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1), "INVENTORY");
        ImGui.Separator();

        // Scrollable inventory content
        // AlwaysVerticalScrollbar reserves space for scrollbar so layout doesn't shift when content grows
        ImGui.BeginChild("InventoryScroll", new Vector2(ImGuiSizes.Fill, ImGuiSizes.Fill), ImGuiChildFlags.None, ImGuiWindowFlags.AlwaysVerticalScrollbar);

        if (viewModel.PlayerAvatar?.Capabilities == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No avatar created");
            ImGui.TextWrapped("Enter a world to select archetype");
            ImGui.EndChild();
            return;
        }

        var caps = viewModel.PlayerAvatar.Capabilities;

        // RPG Elements
        ImGui.TextColored(new Vector4(1, 0.7f, 0.7f, 1), "RPG Elements");

        // Equipment
        if (ImGui.CollapsingHeader($"Equipment ({caps.Equipment?.Length ?? 0})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (caps.Equipment != null && caps.Equipment.Length > 0)
            {
                foreach (var equip in caps.Equipment)
                {
                    var equipItem = viewModel.CurrentWorld?.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == equip.EquipmentRef);
                    var name = equipItem?.DisplayName ?? equip.EquipmentRef;
                    var slotRef = equipItem?.SlotRef.ToString() ?? "Unknown";
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
                            // Slot info
                            ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1), $"Slot: {slotRef}");

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
            }
            else
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No equipment");
                ImGui.Unindent();
            }
        }

        // Consumables
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

                    // Expandable header for each consumable item
                    var maxTextWidth = GetAvailableTextWidth();
                    var quantityText = $" x{consumable.Quantity}";
                    var truncatedName = TruncateToFit(name, maxTextWidth - ImGui.CalcTextSize(quantityText).X - 30f);
                    var treeNodeOpen = ImGui.TreeNode($"{truncatedName}{quantityText}##{consumable.ConsumableRef}");

                    // Show full name on hover if truncated
                    if (truncatedName != name && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{name}{quantityText}");
                    }

                    // Hotbar assign and Use button on same line as header
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
                        // Empty placeholder to maintain layout when quantity is 0
                        ImGui.Dummy(buttonSize);
                    }

                    if (treeNodeOpen)
                    {
                        if (consumableItem != null)
                        {
                            // Description
                            if (!string.IsNullOrEmpty(consumableItem.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), consumableItem.Description);
                                ImGui.Spacing();
                            }

                            // Effects
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

        // Spells
        if (ImGui.CollapsingHeader($"Spells ({caps.Spells?.Length ?? 0})"))
        {
            if (caps.Spells != null && caps.Spells.Length > 0)
            {
                foreach (var spell in caps.Spells)
                {
                    var spellItem = viewModel.CurrentWorld?.Gameplay?.Spells?.FirstOrDefault(s => s.RefName == spell.SpellRef);
                    var name = spellItem?.DisplayName ?? spell.SpellRef;

                    ImGui.Indent();

                    // Expandable header for each spell
                    var maxTextWidth = GetAvailableTextWidth();
                    var conditionText = $" ({spell.Condition:P0})";
                    var truncatedName = TruncateToFit(name, maxTextWidth - ImGui.CalcTextSize(conditionText).X - 30f);
                    var treeNodeOpen = ImGui.TreeNode($"{truncatedName}{conditionText}##{spell.SpellRef}");

                    // Show full name on hover if truncated
                    if (truncatedName != name && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{name}{conditionText}");
                    }

                    // Hotbar assign button
                    ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ButtonAreaWidth);
                    RenderHotbarAssignButton(HotbarItemType.Spell, spell.SpellRef, name);

                    if (treeNodeOpen)
                    {
                        if (spellItem != null)
                        {
                            // Description
                            if (!string.IsNullOrEmpty(spellItem.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), spellItem.Description);
                                ImGui.Spacing();
                            }

                            // Effects
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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Gameplay Elements
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Gameplay Elements");

        // Tools
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

        // Blocks
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

        // Materials
        if (ImGui.CollapsingHeader($"Materials ({caps.BuildingMaterials?.Length ?? 0})"))
        {
            if (caps.BuildingMaterials != null && caps.BuildingMaterials.Length > 0)
            {
                foreach (var material in caps.BuildingMaterials)
                {
                    var materialItem = viewModel.CurrentWorld?.TryGetBuildingMaterialByRefName(material.BuildingMaterialRef);
                    var name = materialItem?.DisplayName ?? material.BuildingMaterialRef;

                    ImGui.Indent();

                    // Expandable header for each material
                    var maxTextWidth = GetAvailableTextWidth();
                    var quantityText = $" x{material.Quantity}";
                    var truncatedName = TruncateToFit(name, maxTextWidth - ImGui.CalcTextSize(quantityText).X - 30f);
                    var treeNodeOpen = ImGui.TreeNode($"{truncatedName}{quantityText}##{material.BuildingMaterialRef}");

                    // Show full name on hover if truncated
                    if (truncatedName != name && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{name}{quantityText}");
                    }

                    // Hotbar assign button
                    ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ButtonAreaWidth);
                    RenderHotbarAssignButton(HotbarItemType.BuildingMaterial, material.BuildingMaterialRef, name);

                    if (treeNodeOpen)
                    {
                        if (materialItem != null)
                        {
                            // Description
                            if (!string.IsNullOrEmpty(materialItem.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), materialItem.Description);
                                ImGui.Spacing();
                            }

                            // Pricing information
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

        // Quest Tokens
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

        // Render hotbar assignment popup inside child window (same ID scope as OpenPopup calls)
        RenderHotbarAssignPopup(viewModel);

        ImGui.EndChild();
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
    /// Renders an "Assign" button that opens the hotbar slot selection popup.
    /// </summary>
    private void RenderHotbarAssignButton(HotbarItemType itemType, string refName, string displayName)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.3f, 0.4f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.4f, 0.5f, 1f));
        if (ImGui.SmallButton($"[+]##{itemType}_{refName}"))
        {
            _showHotbarAssignPopup = true;
            _assignItemType = itemType;
            _assignItemRef = refName;
            _assignItemName = displayName;
            ImGui.OpenPopup("HotbarAssignPopup");
        }
        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Assign to hotbar slot (1-9)");
        }
    }

    /// <summary>
    /// Renders the hotbar assignment popup. Call this once per frame.
    /// </summary>
    private void RenderHotbarAssignPopup(SagaMainViewModel viewModel)
    {
        if (!_showHotbarAssignPopup) return;

        var avatar = viewModel.PlayerAvatar;
        if (avatar == null) return;

        // Center the popup
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
            HotbarItemType.Spell => world.Gameplay?.Spells?.FirstOrDefault(s => s.RefName == slot.RefName)?.DisplayName ?? slot.RefName,
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
