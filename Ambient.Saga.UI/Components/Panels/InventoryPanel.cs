using Ambient.Domain;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
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
    // Track pending sharpen operations for async feedback
    private HashSet<string> _pendingSharpenOperations = new();

    // Hotbar assignment state
    private bool _showHotbarAssignPopup = false;
    private bool _openHotbarPopupRequested = false;
    private HotbarItemType _assignItemType;
    private string? _assignItemRef;
    private string? _assignItemName;
    private Vector2 _popupPosition;

    // Layout base values (will be scaled by DpiScale)
    private const float BaseAssignButtonWidth = 30f;  // "[+]" button
    private const float BaseActionButtonWidth = 55f;  // "Equip"/"Unequip"/"Use" buttons (fixed width)
    private const float BaseButtonSpacing = 4f;

    // Scaled layout values
    private static float AssignButtonWidth => BaseAssignButtonWidth * UIConstants.DpiScale;
    private static float ActionButtonWidth => BaseActionButtonWidth * UIConstants.DpiScale;
    private static float ButtonSpacing => BaseButtonSpacing * UIConstants.DpiScale;
    private static float ButtonAreaWidth => AssignButtonWidth + ActionButtonWidth + ButtonSpacing + (8f * UIConstants.DpiScale);

    public void Render(SagaMainViewModel viewModel)
    {
        if (viewModel.Avatar?.Capabilities == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No avatar created");
            ImGui.TextWrapped("Enter a world to select archetype");
            return;
        }

        var caps = viewModel.Avatar.Capabilities;

        // Carry weight bar
        var worldConfig = viewModel.CurrentWorld?.WorldConfiguration;
        var archetype = viewModel.CurrentWorld?.Gameplay?.AvatarArchetypes?
            .FirstOrDefault(a => a.RefName == viewModel.Avatar.ArchetypeRef);
        if (worldConfig != null && archetype != null)
        {
            var weightUnit = worldConfig.WeightUnitName ?? "kg";
            var maxCarry = CarryWeightCalculator.GetMaxCarryWeight(archetype);
            var currentCarry = CarryWeightCalculator.CalculateTotalWeight(caps, worldConfig);
            var fraction = maxCarry > 0 ? currentCarry / maxCarry : 0f;

            var carryColor = fraction > 0.9f
                ? new Vector4(1, 0.3f, 0.3f, 1)
                : fraction > 0.7f
                    ? new Vector4(1, 0.8f, 0.3f, 1)
                    : new Vector4(0.5f, 0.8f, 1, 1);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, carryColor);
            ImGui.ProgressBar(fraction, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight()),
                $"Carrying: {currentCarry:N1} / {maxCarry:N1} {weightUnit}");
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

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

        // Column 2: RPG Elements (Consumables, Spells, Affinity, Stance, Quest Tokens)
        ImGui.BeginChild("RpgColumn", new Vector2(columnWidth, contentHeight), ImGuiChildFlags.None, ImGuiWindowFlags.None);
        ImGui.TextColored(new Vector4(0.7f, 1f, 0.7f, 1), "ITEMS & MAGIC");
        ImGui.Separator();
        RenderConsumables(viewModel, caps);
        RenderSpells(viewModel, caps);
        RenderAffinity(viewModel, caps);
        RenderStance(viewModel, caps);
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

                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ButtonAreaWidth - ImGui.GetStyle().WindowPadding.X);
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

                        // Drop button
                        ImGui.Spacing();
                        if (RenderDropButton($"drop_con_{consumable.ConsumableRef}"))
                        {
                            DiscardConsumable(viewModel, consumable.ConsumableRef, name);
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

                        // Drop button
                        ImGui.Spacing();
                        if (RenderDropButton($"drop_sp_{spell.SpellRef}"))
                        {
                            DiscardSpell(viewModel, spell.SpellRef, spellItem?.DisplayName ?? spell.SpellRef);
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

    // Quest tokens now live in saga state (AwardedQuestTokens), not avatar inventory.
    // TODO: Render from saga state read model when UI plumbing is available.
    private void RenderQuestTokens(SagaMainViewModel viewModel, ItemCollection caps)
    {
    }

    /// <summary>
    /// Renders the Affinity section showing collected affinities from defeated enemies.
    /// Allows setting the active affinity for combat.
    /// </summary>
    private void RenderAffinity(SagaMainViewModel viewModel, ItemCollection caps)
    {
        var world = viewModel.CurrentWorld;
        var avatar = viewModel.Avatar;
        var collectedAffinities = avatar?.Affinities ?? Array.Empty<Ambient.Domain.Affinity>();
        var activeAffinityRef = avatar?.ActiveAffinityRef;

        // Get display name for active affinity
        var activeAffinityName = "(None)";
        if (!string.IsNullOrEmpty(activeAffinityRef))
        {
            var activeDef = world?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == activeAffinityRef);
            activeAffinityName = activeDef?.DisplayName ?? activeAffinityRef;
        }

        var hasActive = !string.IsNullOrEmpty(activeAffinityRef);
        var headerText = hasActive
            ? $"Spirit: {activeAffinityName}"
            : "Spirit: (None Set)";

        // Highlight header if affinity is active
        if (hasActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.5f, 1f, 1f)); // Purple for spirit/affinity
        }

        var isOpen = ImGui.CollapsingHeader($"{headerText}##Affinity", ImGuiTreeNodeFlags.DefaultOpen);

        if (hasActive)
        {
            ImGui.PopStyleColor();
        }

        ImGui.Indent();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Elemental affinity for combat advantage");
        ImGui.Unindent();

        if (isOpen)
        {
            if (collectedAffinities.Length == 0)
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "(Defeat enemies to capture their affinities)");
                ImGui.Unindent();
            }
            else
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Collected: {collectedAffinities.Length}");
                ImGui.Spacing();

                foreach (var affinity in collectedAffinities)
                {
                    var affinityDef = world?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == affinity.AffinityRef);
                    var name = affinityDef?.DisplayName ?? affinity.AffinityRef;
                    var isActive = affinity.AffinityRef == activeAffinityRef;

                    // TreeNode for each affinity
                    var nodeFlags = isActive ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None;
                    var nodeLabel = isActive ? $"* {name} [ACTIVE]" : name;

                    if (isActive)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 1f, 0.5f, 1f));
                    }

                    var treeOpen = ImGui.TreeNodeEx($"{nodeLabel}##{affinity.AffinityRef}", nodeFlags);

                    if (isActive)
                    {
                        ImGui.PopStyleColor();
                    }

                    // Set Active button on same line
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ActionButtonWidth - ImGui.GetStyle().WindowPadding.X - ImGui.GetStyle().IndentSpacing);

                    if (isActive)
                    {
                        ImGui.BeginDisabled();
                        ImGui.Button("Active", new Vector2(ActionButtonWidth, ImGui.GetFrameHeight()));
                        ImGui.EndDisabled();
                    }
                    else
                    {
                        if (ImGui.Button($"Set##{affinity.AffinityRef}", new Vector2(ActionButtonWidth, ImGui.GetFrameHeight())))
                        {
                            // Set as active affinity
                            if (avatar != null)
                            {
                                avatar.ActiveAffinityRef = affinity.AffinityRef;
                                viewModel.AddToastMessage($"{name} is now your active affinity");
                            }
                        }
                    }

                    if (treeOpen)
                    {
                        // Source
                        if (!string.IsNullOrEmpty(affinity.CapturedFromCharacterRef))
                        {
                            var sourceChar = world?.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == affinity.CapturedFromCharacterRef);
                            var sourceName = sourceChar?.DisplayName ?? affinity.CapturedFromCharacterRef;
                            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Captured from: {sourceName}");
                        }

                        // Description
                        if (affinityDef != null && !string.IsNullOrEmpty(affinityDef.Description))
                        {
                            ImGui.TextWrapped(affinityDef.Description);
                        }

                        // Matchups (strengths/weaknesses)
                        if (affinityDef?.Matchup != null && affinityDef.Matchup.Length > 0)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Combat Matchups:");
                            foreach (var matchup in affinityDef.Matchup)
                            {
                                var targetDef = world?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == matchup.TargetAffinityRef);
                                var targetName = targetDef?.DisplayName ?? matchup.TargetAffinityRef;
                                var color = matchup.Multiplier > 1.0f
                                    ? new Vector4(0.3f, 1f, 0.3f, 1)   // Green for strong
                                    : matchup.Multiplier < 1.0f
                                        ? new Vector4(1f, 0.4f, 0.4f, 1) // Red for weak
                                        : new Vector4(0.7f, 0.7f, 0.7f, 1); // Gray for neutral
                                var label = matchup.Multiplier > 1.0f ? "Strong" : matchup.Multiplier < 1.0f ? "Weak" : "Neutral";
                                ImGui.TextColored(color, $"  vs {targetName}: {matchup.Multiplier:F1}x ({label})");
                            }
                        }

                        ImGui.TreePop();
                    }
                }

                ImGui.Unindent();
            }
        }
    }

    /// <summary>
    /// Renders the Stance section (Battle Stance).
    /// </summary>
    private void RenderStance(SagaMainViewModel viewModel, ItemCollection caps)
    {
        var world = viewModel.CurrentWorld;
        var slot = world?.LoadoutSlotsLookup?.GetValueOrDefault("Stance");
        if (slot == null) return;

        // Get equipment items for this slot
        var stanceEquipment = new List<(EquipmentEntry entry, Equipment? def)>();
        if (caps.Equipment != null)
        {
            foreach (var equip in caps.Equipment)
            {
                var equipDef = world?.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == equip.EquipmentRef);
                if (equipDef?.SlotRef == "Stance")
                {
                    stanceEquipment.Add((equip, equipDef));
                }
            }
        }

        var equippedItem = stanceEquipment.FirstOrDefault(e => viewModel.IsItemEquipped(e.entry.EquipmentRef));
        var hasEquipped = equippedItem.entry != null;

        var slotDisplayName = slot.DisplayName ?? "Battle Stance";
        var headerText = hasEquipped
            ? $"{slotDisplayName}: {equippedItem.def?.DisplayName ?? equippedItem.entry!.EquipmentRef}"
            : $"{slotDisplayName}";

        if (hasEquipped)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 1f, 0.5f, 1f));
        }

        var isOpen = ImGui.CollapsingHeader($"{headerText}##Stance");

        if (hasEquipped)
        {
            ImGui.PopStyleColor();
        }

        if (!string.IsNullOrEmpty(slot.Description))
        {
            ImGui.Indent();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), slot.Description);
            ImGui.Unindent();
        }

        if (isOpen)
        {
            if (stanceEquipment.Count == 0)
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "(none available)");
                ImGui.Unindent();
            }
            else
            {
                foreach (var (equip, equipDef) in stanceEquipment)
                {
                    RenderEquipmentItem(viewModel, equip, equipDef, "Stance");
                }
            }
        }
    }

    // Hardcoded sharpen cost (can be made configurable later)
    private const int SharpenCost = 50;

    /// <summary>
    /// Renders the Tools section with expandable details.
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
                    var treeNodeOpen = ImGui.TreeNode($"{truncatedName}{conditionText}##{tool.ToolRef}");

                    if (truncatedName != toolName && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{toolName}{conditionText}");
                    }

                    // Buttons on same line as header
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ButtonAreaWidth - ImGui.GetStyle().WindowPadding.X);
                    RenderHotbarAssignButton(HotbarItemType.Tool, tool.ToolRef, toolName);
                    ImGui.SameLine();

                    // Equip button
                    var buttonSize = new Vector2(ActionButtonWidth, ImGui.GetFrameHeight());
                    var isEquipped = viewModel.Avatar?.CurrentToolRef == tool.ToolRef;
                    if (isEquipped)
                    {
                        ImGui.BeginDisabled();
                        ImGui.Button("Equipped", buttonSize);
                        ImGui.EndDisabled();
                    }
                    else
                    {
                        if (ImGui.Button($"Equip##{tool.ToolRef}", buttonSize))
                        {
                            if (viewModel.Avatar != null)
                            {
                                viewModel.Avatar.CurrentToolRef = tool.ToolRef;
                                viewModel.AddToastMessage($"{toolName} equipped");
                            }
                        }
                    }

                    if (treeNodeOpen)
                    {
                        // Tool details
                        if (toolDef != null)
                        {
                            if (!string.IsNullOrEmpty(toolDef.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), toolDef.Description);
                                ImGui.Spacing();
                            }

                            // Effective substances
                            if (toolDef.EffectiveSubstances != null && toolDef.EffectiveSubstances.Length > 0)
                            {
                                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1), "Effective on:");
                                foreach (var substance in toolDef.EffectiveSubstances)
                                {
                                    var multiplierText = substance.EffectivenessMultiplier != 1f
                                        ? $" (×{substance.EffectivenessMultiplier:F1})"
                                        : "";
                                    ImGui.BulletText($"{substance.SubstanceRef}{multiplierText}");
                                }
                                ImGui.Spacing();
                            }

                            // Durability info
                            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), $"Wear rate: {toolDef.DurabilityLoss:P1}/use");
                            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Value: {toolDef.WholesalePrice}");
                        }

                        // Condition bar
                        ImGui.Spacing();
                        var conditionColor = tool.Condition > 0.5f
                            ? new Vector4(0.3f, 0.8f, 0.3f, 1)
                            : tool.Condition > 0.25f
                                ? new Vector4(0.9f, 0.7f, 0.2f, 1)
                                : new Vector4(0.9f, 0.3f, 0.2f, 1);
                        ImGui.TextColored(conditionColor, $"Condition: {tool.Condition:P0}");

                        // Sharpen button (only if damaged)
                        if (tool.Condition < 1f)
                        {
                            ImGui.Spacing();
                            var avatarCredits = viewModel.Avatar?.Stats?.Credits ?? 0;
                            var canAfford = avatarCredits >= SharpenCost;
                            var currencyName = viewModel.CurrentWorld?.WorldConfiguration?.CurrencyName ?? "Credits";
                            var isPendingSharpen = _pendingSharpenOperations.Contains(tool.ToolRef);

                            if (isPendingSharpen)
                            {
                                ImGui.BeginDisabled();
                                ImGui.Button("...", new Vector2(0, 0));
                                ImGui.EndDisabled();
                            }
                            else
                            {
                                ImGui.BeginDisabled(!canAfford);
                                if (ImGui.Button($"Sharpen ({SharpenCost} {currencyName})##{tool.ToolRef}"))
                                {
                                    _pendingSharpenOperations.Add(tool.ToolRef);
                                    _ = SharpenToolAsync(viewModel, tool.ToolRef);
                                }
                                ImGui.EndDisabled();

                                if (!canAfford && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                                {
                                    ImGui.SetTooltip($"Not enough {currencyName} (need {SharpenCost}, have {avatarCredits:F0})");
                                }
                            }
                        }

                        // Drop button (disabled if currently equipped)
                        ImGui.Spacing();
                        var isCurrentTool = viewModel.Avatar?.CurrentToolRef == tool.ToolRef;
                        if (isCurrentTool)
                        {
                            ImGui.BeginDisabled();
                            RenderDropButton($"drop_tl_{tool.ToolRef}");
                            ImGui.EndDisabled();
                            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                                ImGui.SetTooltip("Unequip before dropping");
                        }
                        else if (RenderDropButton($"drop_tl_{tool.ToolRef}"))
                        {
                            DiscardTool(viewModel, tool.ToolRef, toolName);
                        }

                        ImGui.TreePop();
                    }

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
    /// Renders the Blocks section with expandable details.
    /// Uses runtime BlockOwnership dictionary (actual inventory) instead of archetype Capabilities.
    /// </summary>
    private void RenderBlocks(SagaMainViewModel viewModel, ItemCollection caps)
    {
        // Get actual block inventory from BlockOwnership (backed by Capabilities.Blocks).
        // One row per stack — a stack's identity is its opaque ref.
        var stacks = viewModel.Avatar?.BlockOwnership?.Entries.Where(e => e.Quantity >= 1).ToList() ?? new List<BlockEntry>();
        var blockCount = stacks.Count;

        if (ImGui.CollapsingHeader($"Blocks ({blockCount})"))
        {
            if (stacks.Count > 0)
            {
                // Sort by display name (then by ref) for consistent ordering
                var sortedBlocks = stacks
                    .OrderBy(e => viewModel.CurrentWorld?.GetItemDisplayName(e.BlockRef) ?? e.BlockRef)
                    .ThenBy(e => e.BlockRef)
                    .ToList();

                foreach (var stack in sortedBlocks)
                {
                    var blockRef = stack.BlockRef;       // opaque stack identity
                    var quantity = (int)stack.Quantity;  // Truncate to whole blocks for display

                    var blockDef = viewModel.CurrentWorld?.BlockProvider?.GetBlockByRefName(blockRef);
                    var blockName = viewModel.CurrentWorld?.GetItemDisplayName(blockRef) ?? blockRef;
                    ImGui.Indent();

                    var maxTextWidth = GetAvailableTextWidth();
                    var quantityText = $" x{quantity}";
                    var truncatedName = TruncateToFit(blockName, maxTextWidth - ImGui.CalcTextSize(quantityText).X - 30f);
                    var treeNodeOpen = ImGui.TreeNode($"{truncatedName}{quantityText}##{blockRef}");

                    if (truncatedName != blockName && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"{blockName}{quantityText}");
                    }

                    // Buttons on same line as header
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ButtonAreaWidth - ImGui.GetStyle().WindowPadding.X);
                    RenderHotbarAssignButton(HotbarItemType.Block, blockRef, blockName);
                    ImGui.SameLine();

                    // Select button
                    var buttonSize = new Vector2(ActionButtonWidth, ImGui.GetFrameHeight());
                    var isSelected = viewModel.Avatar?.CurrentBlockRef == blockRef;
                    if (isSelected)
                    {
                        ImGui.BeginDisabled();
                        ImGui.Button("Selected", buttonSize);
                        ImGui.EndDisabled();
                    }
                    else
                    {
                        if (ImGui.Button($"Select##{blockRef}", buttonSize))
                        {
                            if (viewModel.Avatar != null)
                            {
                                viewModel.Avatar.CurrentBlockRef = blockRef;
                                viewModel.AddToastMessage($"{blockName} selected");
                            }
                        }
                    }

                    if (treeNodeOpen)
                    {
                        // Block details
                        if (blockDef != null)
                        {
                            if (!string.IsNullOrEmpty(blockDef.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), blockDef.Description);
                                ImGui.Spacing();
                            }

                            // Substance
                            if (!string.IsNullOrEmpty(blockDef.SubstanceRef))
                            {
                                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1), $"Substance: {blockDef.SubstanceRef}");
                            }

                            // Value
                            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Value: {blockDef.WholesalePrice}");
                            if (blockDef.MerchantMarkupMultiplier != 1f)
                            {
                                ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"Markup: {blockDef.MerchantMarkupMultiplier:F1}x");
                            }
                        }

                        // Drop button
                        ImGui.Spacing();
                        if (RenderDropButton($"drop_blk_{blockRef}"))
                        {
                            DiscardBlock(viewModel, blockRef, blockName);
                        }

                        ImGui.TreePop();
                    }

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
    /// Renders the Materials section with expandable details.
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

                    if (treeNodeOpen)
                    {
                        if (materialItem != null)
                        {
                            if (!string.IsNullOrEmpty(materialItem.Description))
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), materialItem.Description);
                                ImGui.Spacing();
                            }

                            // Compatible substances
                            if (materialItem.CompatibleSubstances != null && materialItem.CompatibleSubstances.Length > 0)
                            {
                                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1), "Works on:");
                                foreach (var substance in materialItem.CompatibleSubstances)
                                {
                                    ImGui.BulletText(substance.SubstanceRef);
                                }
                                ImGui.Spacing();
                            }

                            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Value: {materialItem.WholesalePrice}");
                            if (materialItem.MerchantMarkupMultiplier != 1f)
                            {
                                ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"Markup: {materialItem.MerchantMarkupMultiplier:F1}x");
                            }
                        }

                        // Drop button
                        ImGui.Spacing();
                        if (RenderDropButton($"drop_mat_{material.BuildingMaterialRef}"))
                        {
                            DiscardMaterial(viewModel, material.BuildingMaterialRef,
                                materialItem?.DisplayName ?? material.BuildingMaterialRef);
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

    private async Task SharpenToolAsync(SagaMainViewModel viewModel, string toolRef)
    {
        try
        {
            await viewModel.SharpenToolAsync(toolRef, SharpenCost);
        }
        finally
        {
            _pendingSharpenOperations.Remove(toolRef);
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

        // Group avatar's equipment by slot
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

        // Render each slot (skip Affinity and Stance - they're rendered separately)
        foreach (var slot in loadoutSlots)
        {
            // Skip Affinity and Stance slots - they're rendered in the RPG column
            if (slot.RefName == "Affinity" || slot.RefName == "Stance")
                continue;

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
        ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ButtonAreaWidth - ImGui.GetStyle().WindowPadding.X);
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
                    ImGuiHelpers.RenderAttributes(equipItem.Effects, warmingLabel: "Insulation:", coolingLabel: "Insulation:");
                }
            }

            // Condition display with color coding
            ImGui.Spacing();
            var conditionColor = equip.Condition > 0.5f
                ? new Vector4(0.3f, 0.8f, 0.3f, 1)
                : equip.Condition > 0.25f
                    ? new Vector4(0.9f, 0.7f, 0.2f, 1)
                    : new Vector4(0.9f, 0.3f, 0.2f, 1);
            ImGui.TextColored(conditionColor, $"Condition: {equip.Condition:P0}");

            // Drop button (disabled if equipped)
            ImGui.Spacing();
            if (isEquipped)
            {
                ImGui.BeginDisabled();
                RenderDropButton($"drop_eq_{equip.EquipmentRef}");
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Unequip before dropping");
            }
            else if (RenderDropButton($"drop_eq_{equip.EquipmentRef}"))
            {
                DiscardEquipment(viewModel, equip.EquipmentRef, name);
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
        var buttonSize = new Vector2(AssignButtonWidth, ImGui.GetFrameHeight());
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.3f, 0.4f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.4f, 0.5f, 1f));
        if (ImGui.Button($"[+]##{itemType}_{refName}", buttonSize))
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

        var avatar = viewModel.Avatar;
        if (avatar == null) return;

        // Open popup if requested (deferred from button click)
        if (_openHotbarPopupRequested)
        {
            ImGui.OpenPopup("HotbarAssignPopup");
            _openHotbarPopupRequested = false;
        }

        // Position near where clicked (DPI-scaled popup width)
        ImGui.SetNextWindowPos(_popupPosition, ImGuiCond.Appearing);
        var popupSize = new Vector2(200 * UIConstants.DpiScale, 0);
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

        // Every item type resolves the same way — no per-type branch, no block special case.
        return world.GetItemDisplayName(slot.RefName) ?? slot.RefName;
    }

    private bool RenderDropButton(string id)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonDanger);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonDangerHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonDangerActive);
        var clicked = ImGui.Button($"Drop##{id}");
        ImGui.PopStyleColor(3);
        return clicked;
    }

    private void DiscardEquipment(SagaMainViewModel viewModel, string equipmentRef, string displayName)
    {
        var caps = viewModel.Avatar?.Capabilities;
        if (caps?.Equipment == null) return;
        caps.Equipment = caps.Equipment.Where(e => e.EquipmentRef != equipmentRef).ToArray();
        viewModel.AddToastMessage($"Dropped {displayName}");
    }

    private void DiscardConsumable(SagaMainViewModel viewModel, string consumableRef, string displayName)
    {
        var caps = viewModel.Avatar?.Capabilities;
        if (caps?.Consumables == null) return;
        caps.Consumables = caps.Consumables.Where(c => c.ConsumableRef != consumableRef).ToArray();
        viewModel.AddToastMessage($"Dropped {displayName}");
    }

    private void DiscardSpell(SagaMainViewModel viewModel, string spellRef, string displayName)
    {
        var caps = viewModel.Avatar?.Capabilities;
        if (caps?.Spells == null) return;
        caps.Spells = caps.Spells.Where(s => s.SpellRef != spellRef).ToArray();
        viewModel.AddToastMessage($"Dropped {displayName}");
    }

    private void DiscardTool(SagaMainViewModel viewModel, string toolRef, string displayName)
    {
        var caps = viewModel.Avatar?.Capabilities;
        if (caps?.Tools == null) return;
        caps.Tools = caps.Tools.Where(t => t.ToolRef != toolRef).ToArray();
        viewModel.AddToastMessage($"Dropped {displayName}");
    }

    private void DiscardBlock(SagaMainViewModel viewModel, string blockRef, string displayName)
    {
        // BlockOwnership is backed by Capabilities.Blocks — drops only the specific stack
        viewModel.Avatar?.BlockOwnership?.Remove(blockRef);
        viewModel.AddToastMessage($"Dropped {displayName}");
    }

    private void DiscardMaterial(SagaMainViewModel viewModel, string materialRef, string displayName)
    {
        var caps = viewModel.Avatar?.Capabilities;
        if (caps?.BuildingMaterials == null) return;
        caps.BuildingMaterials = caps.BuildingMaterials.Where(m => m.BuildingMaterialRef != materialRef).ToArray();
        viewModel.AddToastMessage($"Dropped {displayName}");
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
