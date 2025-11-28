using Ambient.Domain;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Presentation.UI.Services;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

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

        ImGui.SetNextWindowSize(new Vector2(900, 600), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Choose Your Character", ref isOpen, ImGuiWindowFlags.NoResize))
        {
            // Header
            ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Choose Your Character Archetype");
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
                    ImGui.TableSetupColumn("List", ImGuiTableColumnFlags.WidthFixed, 400);
                    ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow();

                    // Left column: Archetype list
                    ImGui.TableNextColumn();
                    ImGui.BeginChild("ArchetypeList", new Vector2(0, -40), ImGuiChildFlags.Borders);

                    for (var i = 0; i < archetypes.Count; i++)
                    {
                        var archetype = archetypes[i];
                        var isSelected = i == _selectedIndex;

                        if (ImGui.Selectable($"##{i}", isSelected, ImGuiSelectableFlags.None, new Vector2(0, 80)))
                        {
                            _selectedIndex = i;
                            _selectedArchetype = archetype;
                        }

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }

                        // Draw archetype info on same line as selectable
                        var cursorPos = ImGui.GetCursorPos();
                        ImGui.SetCursorPos(new Vector2(cursorPos.X, cursorPos.Y - 75));

                        ImGui.TextColored(new Vector4(1, 1, 1, 1), archetype.DisplayName);
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Affinity: {archetype.AffinityRef}");
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 360);
                        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), archetype.Description ?? "");
                        ImGui.PopTextWrapPos();

                        ImGui.SetCursorPos(cursorPos);
                        ImGui.Spacing();
                    }

                    ImGui.EndChild();

                    // Right column: Details
                    ImGui.TableNextColumn();
                    ImGui.BeginChild("ArchetypeDetails", new Vector2(0, -40), ImGuiChildFlags.Borders);

                    if (_selectedArchetype != null)
                    {
                        RenderArchetypeDetails(_selectedArchetype, currencyName);
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

            // Buttons
            float buttonWidth = 120;
            float spacing = 10;
            var totalWidth = buttonWidth * 2 + spacing;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - totalWidth - 20);

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 30)))
            {
                selector?.CancelSelection();
                isOpen = false;
                _selectedArchetype = null;
                _selectedIndex = -1;
            }

            ImGui.SameLine();

            var canEnter = _selectedArchetype != null;
            if (!canEnter) ImGui.BeginDisabled();

            if (ImGui.Button("Enter World", new Vector2(buttonWidth, 30)))
            {
                selector?.CompleteSelection(_selectedArchetype);
                isOpen = false;
                _selectedArchetype = null;
                _selectedIndex = -1;
            }

            if (!canEnter) ImGui.EndDisabled();

            ImGui.EndPopup();
        }

        // If modal was closed without selection (e.g., ESC key), treat as cancel
        if (!isOpen && _selectedArchetype != null)
        {
            selector?.CancelSelection();
            _selectedArchetype = null;
            _selectedIndex = -1;
        }
    }

    private void RenderArchetypeDetails(AvatarArchetype archetype, string currencyName)
    {
        ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Character Stats");
        ImGui.Separator();
        ImGui.Spacing();

        if (archetype.SpawnStats != null)
        {
            var stats = archetype.SpawnStats;

            // Vitals
            ImGui.Text($"Health:     {stats.Health:P0}");
            ImGui.Text($"Stamina:    {stats.Stamina:P0}");
            ImGui.Text($"Mana:       {stats.Mana:P0}");
            ImGui.Text($"Hunger:     {stats.Hunger:P0}");
            ImGui.Text($"Thirst:     {stats.Thirst:P0}");
            ImGui.Spacing();

            // Combat Stats
            ImGui.Text($"Strength:   {stats.Strength:P0}");
            ImGui.Text($"Defense:    {stats.Defense:P0}");
            ImGui.Text($"Speed:      {stats.Speed:P0}");
            ImGui.Text($"Magic:      {stats.Magic:P0}");
            ImGui.Spacing();

            // Currency & XP
            ImGui.Text($"{currencyName}: {stats.Credits}");
            ImGui.Text($"Experience: {stats.Experience}");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Capabilities
        if (archetype.SpawnCapabilities != null)
        {
            ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Starting Equipment");
            ImGui.Separator();
            ImGui.Spacing();

            var caps = archetype.SpawnCapabilities;

            if (caps.Equipment != null && caps.Equipment.Length > 0)
            {
                ImGui.Text("Equipment:");
                foreach (var item in caps.Equipment)
                {
                    ImGui.BulletText($"{item.EquipmentRef} (condition: {item.Condition:P0})");
                }
                ImGui.Spacing();
            }

            if (caps.Consumables != null && caps.Consumables.Length > 0)
            {
                ImGui.Text("Consumables:");
                foreach (var item in caps.Consumables)
                {
                    ImGui.BulletText($"{item.ConsumableRef} (x{item.Quantity})");
                }
                ImGui.Spacing();
            }

            if (caps.Spells != null && caps.Spells.Length > 0)
            {
                ImGui.Text("Spells:");
                foreach (var item in caps.Spells)
                {
                    ImGui.BulletText($"{item.SpellRef} (condition: {item.Condition:P0})");
                }
                ImGui.Spacing();
            }

            if (caps.Tools != null && caps.Tools.Length > 0)
            {
                ImGui.Text("Tools:");
                foreach (var item in caps.Tools)
                {
                    ImGui.BulletText($"{item.ToolRef} (condition: {item.Condition:P0})");
                }
            }
        }
    }
}
