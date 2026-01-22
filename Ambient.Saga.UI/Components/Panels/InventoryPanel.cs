using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Inventory panel showing what is available to the character.
/// Includes equipment, consumables, spells, tools, blocks, materials, and quest tokens.
/// Accessible via I key.
/// </summary>
public class InventoryPanel
{
    public void Render(MainViewModel viewModel)
    {
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1), "INVENTORY");
        ImGui.Separator();

        // Scrollable inventory content
        ImGui.BeginChild("InventoryScroll", new Vector2(ImGuiSizes.Fill, ImGuiSizes.Fill), ImGuiChildFlags.None);

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

                    ImGui.Indent();

                    // Expandable header for each equipment item
                    var treeNodeOpen = ImGui.TreeNode($"{name} ({equip.Condition:P0})##{equip.EquipmentRef}");

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
            }
            else
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No equipment");
                ImGui.Unindent();
            }
        }

        // Consumables
        if (ImGui.CollapsingHeader($"Consumables ({caps.Consumables?.Length ?? 0})"))
        {
            if (caps.Consumables != null && caps.Consumables.Length > 0)
            {
                foreach (var consumable in caps.Consumables)
                {
                    var consumableItem = viewModel.CurrentWorld?.Gameplay?.Consumables?.FirstOrDefault(c => c.RefName == consumable.ConsumableRef);
                    var name = consumableItem?.DisplayName ?? consumable.ConsumableRef;

                    ImGui.Indent();

                    // Expandable header for each consumable item
                    var treeNodeOpen = ImGui.TreeNode($"{name} x{consumable.Quantity}##{consumable.ConsumableRef}");

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
                    var treeNodeOpen = ImGui.TreeNode($"{name} ({spell.Condition:P0})##{spell.SpellRef}");

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
                    ImGui.BulletText($"{toolName} ({tool.Condition:P0})");
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
                    ImGui.BulletText($"{blockName} x{block.Quantity}");
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
                    var treeNodeOpen = ImGui.TreeNode($"{name} x{material.Quantity}##{material.BuildingMaterialRef}");

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

        ImGui.EndChild();
    }
}
