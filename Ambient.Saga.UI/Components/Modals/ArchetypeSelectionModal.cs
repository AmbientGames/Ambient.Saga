using Ambient.Domain;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Services;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for selecting character archetype
/// </summary>
public class ArchetypeSelectionModal
{
    private AvatarArchetype? _selectedArchetype;
    private int _selectedIndex = -1;

    public void Render(MainViewModel viewModel, ImGuiArchetypeSelector? selector, ref bool isOpen)
    {
        if (!isOpen) return;

        var archetypes = selector?.CurrentArchetypes?.ToList();
        var currencyName = selector?.CurrentCurrencyName ?? "Credits";

        // Open popup if not already open (must be called BEFORE BeginPopupModal)
        if (!ImGui.IsPopupOpen("Choose Your Character"))
        {
            ImGui.OpenPopup("Choose Your Character");
        }

        ImGuiHelpers.SetupModalWindow(1200, 800);

        // NoClose flag prevents the X button - archetype selection is mandatory
        // Note: ImGui doesn't have NoClose flag, so we pass a dummy bool that we ignore
        bool dummyOpen = true;
        
        if (ImGui.BeginPopupModal("Choose Your Character", ref dummyOpen, ImGuiWindowFlags.NoResize))
        {
            // Header
            ImGui.PushFont(UIConstants.FontTitle);
            ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Choose Your Character Archetype");
            ImGui.PopFont();
            ImGui.Text("This choice determines your starting equipment and stats");
            ImGui.Separator();
            ImGui.Spacing();

            if (archetypes == null || archetypes.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "No archetypes available. Load a world first.");
            }
            else
            {
                // Two-column layout
                if (ImGui.BeginTable("ArchetypeLayout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
                {
                    ImGui.TableSetupColumn("List", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow();

                    // Left column: Archetype list
                    ImGui.TableNextColumn();
                    var footerHeight = ImGui.GetFrameHeightWithSpacing() * 1.5f;
                    ImGui.BeginChild("ArchetypeList", new Vector2(ImGuiSizes.Fill, -footerHeight), ImGuiChildFlags.Borders);

                    for (var i = 0; i < archetypes.Count; i++)
                    {
                        var archetype = archetypes[i];
                        var isSelected = i == _selectedIndex;

                        // Use a child region for each archetype card for better layout control
                        // Card shows: name, affinity, description - calculate height based on content
                        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
                        var cardHeight = lineHeight * 5; // Name + Affinity + ~3 lines for description

                        // Store positions before any rendering
                        var cursorScreenPos = ImGui.GetCursorScreenPos();
                        var availWidth = ImGui.GetContentRegionAvail().X;
                        var startPos = ImGui.GetCursorPos();

                        // Invisible button for click detection (render first to establish the interactive area)
                        if (ImGui.InvisibleButton($"archetype_{i}", new Vector2(availWidth, cardHeight)))
                        {
                            _selectedIndex = i;
                            _selectedArchetype = archetype;
                        }

                        // Check hover state
                        var isHovered = ImGui.IsItemHovered();

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }

                        // Draw background - selection (green) or hover (subtle)
                        if (isSelected || isHovered)
                        {
                            var drawList = ImGui.GetWindowDrawList();
                            var bgColor = isSelected
                                ? new Vector4(0.3f, 0.5f, 0.3f, 0.5f)   // Green for selected
                                : new Vector4(0.4f, 0.4f, 0.4f, 0.3f);  // Gray for hover
                            drawList.AddRectFilled(
                                cursorScreenPos,
                                new Vector2(cursorScreenPos.X + availWidth, cursorScreenPos.Y + cardHeight),
                                ImGui.GetColorU32(bgColor),
                                4.0f);
                        }

                        // Draw archetype info overlaid on the button area
                        ImGui.SetCursorPos(new Vector2(startPos.X + 8, startPos.Y + 4));

                        ImGui.TextColored(new Vector4(1, 1, 1, 1), archetype.DisplayName ?? archetype.RefName);

                        ImGui.SetCursorPosX(startPos.X + 8);
                        var affinity = viewModel.CurrentWorld?.TryGetCharacterAffinityByRefName(archetype.AffinityRef ?? "");
                        var affinityName = affinity?.DisplayName ?? archetype.AffinityRef ?? "None";
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), $"Affinity: {affinityName}");

                        ImGui.SetCursorPosX(startPos.X + 8);
                        ImGui.PushTextWrapPos(startPos.X + ImGui.GetContentRegionAvail().X - 8);
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), archetype.Description ?? "");
                        ImGui.PopTextWrapPos();

                        // Move cursor to end of card
                        ImGui.SetCursorPos(new Vector2(startPos.X, startPos.Y + cardHeight));

                        // Separator between items
                        if (i < archetypes.Count - 1)
                        {
                            ImGui.Separator();
                        }
                    }

                    ImGui.EndChild();

                    // Right column: Details
                    ImGui.TableNextColumn();
                    ImGui.BeginChild("ArchetypeDetails", new Vector2(ImGuiSizes.Fill, -footerHeight), ImGuiChildFlags.Borders);

                    if (_selectedArchetype != null)
                    {
                        RenderArchetypeDetails(_selectedArchetype, currencyName, viewModel);
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Select an archetype to view details");
                    }

                    ImGui.EndChild();

                    ImGui.EndTable();
                }
            }

            ImGui.Separator();

            // Buttons - auto-size based on text content
            var style = ImGui.GetStyle();
            var quitTextSize = ImGui.CalcTextSize("Quit Game");
            var enterTextSize = ImGui.CalcTextSize("Enter World");
            var buttonPadding = style.FramePadding * 2;
            var quitButtonWidth = quitTextSize.X + buttonPadding.X;
            var enterButtonWidth = enterTextSize.X + buttonPadding.X;
            var buttonHeight = Math.Max(quitTextSize.Y, enterTextSize.Y) + buttonPadding.Y;
            var spacing = style.ItemSpacing.X;
            var totalWidth = quitButtonWidth + enterButtonWidth + spacing;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - totalWidth - style.WindowPadding.X);

            // Quit Game button (red) - this is the only way out if you don't want to play
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.25f, 0.25f, 1));
            if (ImGui.Button("Quit Game", new Vector2(quitButtonWidth, buttonHeight)))
            {
                selector?.CancelSelection();
                ImGui.CloseCurrentPopup(); // Explicitly close ImGui popup
                isOpen = false;
                _selectedArchetype = null;
                _selectedIndex = -1;
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            // Enter World button (green) - only enabled when archetype selected
            var canEnter = _selectedArchetype != null;
            if (!canEnter) ImGui.BeginDisabled();

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
            if (ImGui.Button("Enter World", new Vector2(enterButtonWidth, buttonHeight)))
            {
                selector?.CompleteSelection(_selectedArchetype);
                ImGui.CloseCurrentPopup(); // Explicitly close ImGui popup to prevent lingering state
                isOpen = false;
                _selectedArchetype = null;
                _selectedIndex = -1;
            }
            ImGui.PopStyleColor(3);

            if (!canEnter) ImGui.EndDisabled();

            ImGui.EndPopup();
        }

        // Note: Modal cannot be closed without selecting an archetype (NoClose flag)
        // Clicking "Quit Game" will call CancelSelection and quit the application
    }

    private void RenderArchetypeDetails(AvatarArchetype archetype, string currencyName, MainViewModel viewModel)
    {
        // Archetype Bias (permanent stat modifiers)
        if (archetype.ArchetypeBias != null)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Archetype Bonuses");
            ImGui.Separator();
            ImGui.Spacing();

            var bias = archetype.ArchetypeBias;
            var hasAnyBias = false;

            // Combat stats (default is 0, so any non-zero is a bonus)
            if (bias.Strength != 0)
            {
                RenderModifierLine("Strength", bias.Strength);
                hasAnyBias = true;
            }
            if (bias.Defense != 0)
            {
                RenderModifierLine("Defense", bias.Defense);
                hasAnyBias = true;
            }
            if (bias.Speed != 0)
            {
                RenderModifierLine("Speed", bias.Speed);
                hasAnyBias = true;
            }
            if (bias.Magic != 0)
            {
                RenderModifierLine("Magic", bias.Magic);
                hasAnyBias = true;
            }

            // Vitals (default is 1 for Health/Stamina/Mana, so != 1 is a modifier)
            if (bias.Health != 1)
            {
                RenderModifierLine("Health", bias.Health - 1); // Show as modifier from baseline
                hasAnyBias = true;
            }
            if (bias.Stamina != 1)
            {
                RenderModifierLine("Stamina", bias.Stamina - 1);
                hasAnyBias = true;
            }
            if (bias.Mana != 1)
            {
                RenderModifierLine("Mana", bias.Mana - 1);
                hasAnyBias = true;
            }

            // Endurance (default is 0)
            if (bias.Endurance != 0)
            {
                RenderModifierLine("Endurance", bias.Endurance);
                hasAnyBias = true;
            }

            if (!hasAnyBias)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No stat bonuses");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Starting Stats");
        ImGui.Separator();
        ImGui.Spacing();

        if (archetype.SpawnStats != null)
        {
            var stats = archetype.SpawnStats;

            // Two-column layout for stats
            if (ImGui.BeginTable("StatsTable", 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Col1", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Col2", ImGuiTableColumnFlags.WidthFixed, 120);

                // Resources
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"Health: {stats.Health:P0}");
                ImGui.TableNextColumn();
                ImGui.Text($"Stamina: {stats.Stamina:P0}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"Mana: {stats.Mana:P0}");
                ImGui.TableNextColumn();
                ImGui.Text($"Temp: {stats.Temperature:F1}C");

                // Attributes
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(1, 0.8f, 0.6f, 1), "Attributes:");
                ImGui.TableNextColumn();
                ImGui.Text("");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"Strength: {stats.Strength:P0}");
                ImGui.TableNextColumn();
                ImGui.Text($"Defense: {stats.Defense:P0}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"Magic: {stats.Magic:P0}");
                ImGui.TableNextColumn();
                ImGui.Text($"Speed: {stats.Speed:P0}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"Endurance: {stats.Endurance:P0}");
                ImGui.TableNextColumn();
                ImGui.Text("");

                // Row 3: Progression
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"{currencyName}:");
                ImGui.TableNextColumn();
                ImGui.Text($"{stats.Credits:N0}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Experience:");
                ImGui.TableNextColumn();
                ImGui.Text($"{stats.Experience:N0}");

                ImGui.EndTable();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Default starting stats");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Capabilities
        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Starting Inventory");
        ImGui.Separator();
        ImGui.Spacing();

        if (archetype.SpawnCapabilities != null)
        {
            var caps = archetype.SpawnCapabilities;
            var hasAnyItems = false;

            if (caps.Equipment != null && caps.Equipment.Length > 0)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Equipment:");
                foreach (var item in caps.Equipment)
                {
                    var equipDef = viewModel.CurrentWorld?.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == item.EquipmentRef);
                    var equipName = equipDef?.DisplayName ?? item.EquipmentRef;
                    ImGui.BulletText($"{equipName} ({item.Condition:P0})");
                }
                ImGui.Spacing();
                hasAnyItems = true;
            }

            if (caps.Consumables != null && caps.Consumables.Length > 0)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Consumables:");
                foreach (var item in caps.Consumables)
                {
                    var consumableDef = viewModel.CurrentWorld?.Gameplay?.Consumables?.FirstOrDefault(c => c.RefName == item.ConsumableRef);
                    var consumableName = consumableDef?.DisplayName ?? item.ConsumableRef;
                    ImGui.BulletText($"{consumableName} x{item.Quantity}");
                }
                ImGui.Spacing();
                hasAnyItems = true;
            }

            if (caps.Spells != null && caps.Spells.Length > 0)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Spells:");
                foreach (var item in caps.Spells)
                {
                    var spellDef = viewModel.CurrentWorld?.Gameplay?.Spells?.FirstOrDefault(s => s.RefName == item.SpellRef);
                    var spellName = spellDef?.DisplayName ?? item.SpellRef;
                    ImGui.BulletText($"{spellName} ({item.Condition:P0})");
                }
                ImGui.Spacing();
                hasAnyItems = true;
            }

            if (caps.Tools != null && caps.Tools.Length > 0)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Tools:");
                foreach (var item in caps.Tools)
                {
                    var toolDef = viewModel.CurrentWorld?.Gameplay?.Tools?.FirstOrDefault(t => t.RefName == item.ToolRef);
                    var toolName = toolDef?.DisplayName ?? item.ToolRef;
                    ImGui.BulletText($"{toolName} ({item.Condition:P0})");
                }
                ImGui.Spacing();
                hasAnyItems = true;
            }

            if (caps.Blocks != null && caps.Blocks.Length > 0)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Blocks:");
                foreach (var item in caps.Blocks)
                {
                    var blockDef = viewModel.CurrentWorld?.BlockProvider?.GetBlockByRefName(item.BlockRef);
                    var blockName = blockDef?.DisplayName ?? item.BlockRef;
                    ImGui.BulletText($"{blockName} x{item.Quantity}");
                }
                ImGui.Spacing();
                hasAnyItems = true;
            }

            if (caps.BuildingMaterials != null && caps.BuildingMaterials.Length > 0)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Building Materials:");
                foreach (var item in caps.BuildingMaterials)
                {
                    var materialDef = viewModel.CurrentWorld?.TryGetBuildingMaterialByRefName(item.BuildingMaterialRef);
                    var materialName = materialDef?.DisplayName ?? item.BuildingMaterialRef;
                    ImGui.BulletText($"{materialName} x{item.Quantity}");
                }
                hasAnyItems = true;
            }

            if (!hasAnyItems)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No starting items");
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "No starting items");
        }
    }

    private void RenderModifierLine(string statName, float modifier)
    {
        var color = modifier > 0
            ? new Vector4(0.2f, 1, 0.2f, 1)   // Green for positive
            : new Vector4(1, 0.4f, 0.4f, 1);  // Red for negative
        var sign = modifier > 0 ? "+" : "";
        ImGui.TextColored(color, $"{statName}: {sign}{modifier:P0}");
    }
}
