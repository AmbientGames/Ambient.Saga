using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;
using Ambient.Saga.UI.Components.Modals;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Right panel showing avatar information: position, stats, inventory, affinities, party.
/// Quests and Achievements have moved to the Journal panel (J key).
/// </summary>
public class AvatarActionsPanel
{
    public void Render(MainViewModel viewModel, ModalManager modalManager)
    {
        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "AVATAR");
        ImGui.Separator();

        // Calculate available height for content AFTER the header is rendered
        // Reserve space for action buttons at the bottom
        var buttonHeight = ImGui.GetFrameHeight();
        var style = ImGui.GetStyle();
        // 2 buttons (1 with spacing + 1 without) + separator area
        var actionsHeight = ImGui.GetFrameHeightWithSpacing() + buttonHeight + style.ItemSpacing.Y * 4;
        var availableHeight = ImGui.GetContentRegionAvail().Y - actionsHeight;

        // Scrollable avatar info content
        ImGui.BeginChild("AvatarInfoScroll", new Vector2(ImGuiSizes.Fill, availableHeight), ImGuiChildFlags.None);

        // Position
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Position:");
        if (viewModel.HasAvatarPosition)
        {
            ImGui.Text($"Lat: {viewModel.AvatarLatitude:F6}°");
            ImGui.Text($"Long: {viewModel.AvatarLongitude:F6}°");
            ImGui.Text($"Elevation: {viewModel.AvatarElevation}m");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No position set");
            ImGui.TextWrapped("Click on map to move");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Vitals/Stats
        if (viewModel.PlayerAvatar != null && viewModel.PlayerAvatar.Stats != null)
        {
            var vitals = viewModel.PlayerAvatar.Stats;
            var currencyName = viewModel.CurrentWorld.WorldConfiguration.CurrencyName ?? "Credit";
            var pluralCurrency = vitals.Credits == 1 ? currencyName : currencyName + "s";

            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Stats:");

            RenderStatLine("Health:", vitals.Health.ToString());
            RenderStatLine("Strength:", vitals.Strength.ToString());
            RenderStatLine("Defense:", vitals.Defense.ToString());
            RenderStatLine("Speed:", vitals.Speed.ToString());
            RenderStatLine("Magic:", vitals.Magic.ToString());
            RenderStatLine("Temperature:", $"{vitals.Temperature:F1}°C");
            RenderStatLine("Hunger:", vitals.Hunger.ToString());
            RenderStatLine("Thirst:", vitals.Thirst.ToString());
            RenderStatLine($"{pluralCurrency}:", $"{vitals.Credits:N0}");

            // Archetype info with bias
            if (!string.IsNullOrEmpty(viewModel.PlayerAvatar.ArchetypeRef))
            {
                var archetype = viewModel.CurrentWorld?.Gameplay?.AvatarArchetypes?
                    .FirstOrDefault(a => a.RefName == viewModel.PlayerAvatar.ArchetypeRef);

                if (archetype != null)
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    // Archetype name and affinity
                    ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Archetype:");
                    ImGui.SameLine();
                    ImGui.Text(archetype.DisplayName ?? archetype.RefName);

                    if (!string.IsNullOrEmpty(archetype.AffinityRef))
                    {
                        var affinity = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?
                            .FirstOrDefault(a => a.RefName == archetype.AffinityRef);
                        var affinityName = affinity?.DisplayName ?? archetype.AffinityRef;
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), "Affinity:");
                        ImGui.SameLine();
                        ImGui.Text(affinityName);
                    }

                    // Archetype bias (permanent stat modifiers)
                    if (archetype.ArchetypeBias != null)
                    {
                        var bias = archetype.ArchetypeBias;
                        var hasBias = bias.Strength != 0 || bias.Defense != 0 || bias.Speed != 0 || bias.Magic != 0 ||
                                      bias.Health != 1 || bias.Stamina != 1 || bias.Mana != 1 || bias.Insulation != 0;

                        if (hasBias)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.9f, 1), "Archetype Bonuses:");

                            if (bias.Strength != 0) RenderBiasLine("Strength", bias.Strength);
                            if (bias.Defense != 0) RenderBiasLine("Defense", bias.Defense);
                            if (bias.Speed != 0) RenderBiasLine("Speed", bias.Speed);
                            if (bias.Magic != 0) RenderBiasLine("Magic", bias.Magic);
                            if (bias.Health != 1) RenderBiasLine("Health", bias.Health - 1);
                            if (bias.Stamina != 1) RenderBiasLine("Stamina", bias.Stamina - 1);
                            if (bias.Mana != 1) RenderBiasLine("Mana", bias.Mana - 1);
                            if (bias.Insulation != 0) RenderBiasLine("Insulation", bias.Insulation);
                        }
                    }
                }
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No avatar created");
            ImGui.TextWrapped("Enter a world to select archetype");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Inventory
        if (viewModel.PlayerAvatar?.Capabilities != null)
        {
            var caps = viewModel.PlayerAvatar.Capabilities;

            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Inventory:");

            // Gameplay Elements
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Gameplay Elements");

            // Blocks
            if (ImGui.CollapsingHeader($"Blocks ({caps.Blocks.Length})"))
            {
                if (caps.Blocks != null)
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
            }

            // Tools
            if (ImGui.CollapsingHeader($"Tools ({caps.Tools.Length})"))
            {
                if (caps.Tools != null)
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
            }

            // Materials
            if (ImGui.CollapsingHeader($"Materials ({caps.BuildingMaterials.Length})"))
            {
                if (caps.BuildingMaterials != null)
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
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.7f, 0.7f, 1), "RPG Elements");

            // Equipment
            if (ImGui.CollapsingHeader($"Equipment ({caps.Equipment.Length})"))
            {
                if (caps.Equipment != null)
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
                                    ImGuiHelpers.RenderCharacterEffects(equipItem.Effects);
                                }
                            }

                            ImGui.TreePop();
                        }

                        ImGui.Unindent();
                    }
                }
            }

            // Consumables
            if (ImGui.CollapsingHeader($"Consumables ({caps.Consumables.Length})"))
            {
                if (caps.Consumables != null)
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
                                    ImGuiHelpers.RenderCharacterEffects(consumableItem.Effects);
                                }
                            }

                            ImGui.TreePop();
                        }

                        ImGui.Unindent();
                    }
                }
            }

            // Spells
            if (ImGui.CollapsingHeader($"Spells ({caps.Spells.Length})"))
            {
                if (caps.Spells != null)
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
                                    ImGuiHelpers.RenderCharacterEffects(spellItem.Effects);
                                }
                            }

                            ImGui.TreePop();
                        }

                        ImGui.Unindent();
                    }
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
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Collected Affinities (captured from characters)
        if (viewModel.PlayerAvatar?.Affinities != null && viewModel.PlayerAvatar.Affinities.Length > 0)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Collected Affinities:");
            ImGui.Spacing();

            // Active affinity indicator
            var activeAffinity = viewModel.PlayerAvatar.ActiveAffinityRef;
            if (!string.IsNullOrEmpty(activeAffinity))
            {
                var activeAffinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == activeAffinity);
                var activeName = activeAffinityDef?.DisplayName ?? activeAffinity;
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Active: {activeName}");
                ImGui.Spacing();
            }

            foreach (var affinity in viewModel.PlayerAvatar.Affinities)
            {
                var affinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == affinity.AffinityRef);
                var name = affinityDef?.DisplayName ?? affinity.AffinityRef;
                var isActive = affinity.AffinityRef == activeAffinity;

                ImGui.Indent();

                var treeNodeOpen = ImGui.TreeNode($"{(isActive ? "* " : "")}{name}##aff_{affinity.AffinityRef}");

                if (treeNodeOpen)
                {
                    // Source character
                    if (!string.IsNullOrEmpty(affinity.CapturedFromCharacterRef))
                    {
                        var sourceChar = viewModel.CurrentWorld?.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == affinity.CapturedFromCharacterRef);
                        var sourceName = sourceChar?.DisplayName ?? affinity.CapturedFromCharacterRef;
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"From: {sourceName}");
                    }

                    // Acquired date
                    if (!string.IsNullOrEmpty(affinity.AcquiredDate))
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Acquired: {affinity.AcquiredDate}");
                    }

                    // Affinity description and matchups
                    if (affinityDef != null)
                    {
                        if (!string.IsNullOrEmpty(affinityDef.Description))
                        {
                            ImGui.TextWrapped(affinityDef.Description);
                        }

                        if (affinityDef.Matchup != null && affinityDef.Matchup.Length > 0)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Matchups:");
                            ImGui.Indent(10 * UIConstants.DpiScale);
                            foreach (var matchup in affinityDef.Matchup)
                            {
                                var targetAffinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == matchup.TargetAffinityRef);
                                var targetName = targetAffinityDef?.DisplayName ?? matchup.TargetAffinityRef;
                                var color = matchup.Multiplier > 1.0
                                    ? new Vector4(0.2f, 1, 0.2f, 1)  // Green for strong
                                    : new Vector4(1, 0.5f, 0.2f, 1); // Orange for weak
                                ImGui.TextColored(color, $"vs {targetName}: {matchup.Multiplier}x");
                            }
                            ImGui.Unindent(10 * UIConstants.DpiScale);
                        }
                    }

                    ImGui.TreePop();
                }

                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
        }

        // Party/Companions
        if (viewModel.PlayerAvatar?.Party?.Member != null && viewModel.PlayerAvatar.Party.Member.Length > 0)
        {
            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Party Members:");
            ImGui.Spacing();

            foreach (var member in viewModel.PlayerAvatar.Party.Member)
            {
                var memberChar = viewModel.CurrentWorld?.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == member.CharacterRef);
                var memberName = memberChar?.DisplayName ?? member.CharacterRef;

                ImGui.Indent();

                var treeNodeOpen = ImGui.TreeNode($"{memberName}##party_{member.CharacterRef}");

                if (treeNodeOpen)
                {
                    if (memberChar != null && !string.IsNullOrEmpty(memberChar.Description))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), memberChar.Description);
                    }

                    // Show member's affinity if available
                    if (memberChar?.AffinityRef != null)
                    {
                        var memberAffinity = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == memberChar.AffinityRef);
                        var affinityName = memberAffinity?.DisplayName ?? memberChar.AffinityRef;
                        ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), $"Affinity: {affinityName}");
                    }

                    ImGui.TreePop();
                }

                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
        }

        // Lifetime Statistics
        if (viewModel.PlayerAvatar != null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1, 1), "Lifetime Statistics:");
            ImGui.Spacing();

            var avatar = viewModel.PlayerAvatar;

            if (ImGui.BeginTable("LifetimeStatsTable", 2, ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                // Play time
                var playTimeHours = avatar.PlayTimeHours;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Play Time:");
                ImGui.TableNextColumn();
                ImGui.Text(playTimeHours >= 1 ? $"{playTimeHours:F1} hours" : $"{playTimeHours * 60:F0} minutes");

                // Distance traveled
                var distance = avatar.DistanceTraveled;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Distance:");
                ImGui.TableNextColumn();
                ImGui.Text(distance >= 1000 ? $"{distance / 1000:F2} km" : $"{distance:F0} m");

                // Blocks
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Blocks Placed:");
                ImGui.TableNextColumn();
                ImGui.Text($"{avatar.BlocksPlaced:N0}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Blocks Destroyed:");
                ImGui.TableNextColumn();
                ImGui.Text($"{avatar.BlocksDestroyed:N0}");

                ImGui.EndTable();
            }
        }

        ImGui.EndChild();

        // Action buttons at bottom (always visible, outside scroll area)
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("View World Catalog", new Vector2(ImGuiSizes.Fill, buttonHeight)))
        {
            modalManager.OpenWorldCatalog();
        }

        if (ImGui.Button("Faction Reputation", new Vector2(ImGuiSizes.Fill, buttonHeight)))
        {
            modalManager.OpenFactionReputation();
        }
    }

    private void RenderStatLine(string label, string value)
    {
        var scale = UIConstants.DpiScale;
        ImGui.Text(label);
        ImGui.SameLine(120 * scale);
        ImGui.Text(value);
    }

    private void RenderBiasLine(string statName, float modifier)
    {
        var color = modifier > 0
            ? new Vector4(0.2f, 1, 0.2f, 1)   // Green for positive
            : new Vector4(1, 0.4f, 0.4f, 1);  // Red for negative
        var sign = modifier > 0 ? "+" : "";
        ImGui.TextColored(color, $"  {statName}: {sign}{modifier:P0}");
    }
}
