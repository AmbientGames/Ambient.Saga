using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for changing complete loadout (equipment, affinity, stance).
/// </summary>
public class EquipmentChangeModal
{
    private readonly Combatant _avatar;
    private readonly IWorld _world;
    private readonly List<string> _avatarAffinityRefs;
    private readonly Dictionary<string, int> _slotSelections = new();
    private readonly Dictionary<string, List<string>> _slotOptions = new();
    private readonly List<string> _affinityOptions = new();
    private readonly List<string> _stanceOptions = new();
    private int _affinitySelection = 0;
    private int _stanceSelection = 0;

    // Event fired when user accepts changes (parameter is comma-separated changes)
    public event Action<string>? EquipmentChanged;

    // Event fired when user cancels
    public event Action? Cancelled;

    public EquipmentChangeModal(Combatant avatar, IWorld world, List<string> avatarAffinityRefs)
    {
        _avatar = avatar;
        _world = world;
        _avatarAffinityRefs = avatarAffinityRefs;

        InitializeDropdowns();
    }

    private void InitializeDropdowns()
    {
        // Initialize equipment slots from world definition
        var loadoutSlots = _world.Gameplay?.LoadoutSlots ?? Array.Empty<LoadoutSlot>();
        var slotNames = loadoutSlots.Select(s => s.RefName).ToList();

        // Fallback to common slots if world doesn't define any
        if (slotNames.Count == 0)
        {
            slotNames = new List<string> { "Head", "Chest", "Legs", "Feet", "OffHand", "MainHand", "BothHands", "Hands", "Back", "Ring", "Amulet", "Belt" };
        }

        foreach (var slotName in slotNames)
        {
            var options = new List<string> { "-- None --" };
            var selectedIndex = 0;

            // Get current equipment for this slot
            string? currentEquipmentRef = null;
            if (_avatar.CombatProfile.TryGetValue(slotName, out var equipped))
            {
                currentEquipmentRef = equipped;
            }

            // Get equipment the player actually HAS
            if (_avatar.Capabilities?.Equipment != null)
            {
                foreach (var entry in _avatar.Capabilities.Equipment)
                {
                    var equipment = _world.GetEquipmentByRefName(entry.EquipmentRef);

                    // Check if this equipment can go in this slot
                    if (equipment != null && equipment.SlotRef.ToString() == slotName)
                    {
                        options.Add(equipment.RefName);

                        // Check if this is currently equipped
                        if (equipment.RefName == currentEquipmentRef)
                        {
                            selectedIndex = options.Count - 1;
                        }
                    }
                }
            }

            _slotOptions[slotName] = options;
            _slotSelections[slotName] = selectedIndex;
        }

        // Initialize affinity dropdown
        foreach (var affinityRef in _avatarAffinityRefs)
        {
            var affinity = _world.TryGetCharacterAffinityByRefName(affinityRef);
            if (affinity != null)
            {
                _affinityOptions.Add(affinity.RefName);

                if (affinity.RefName == _avatar.AffinityRef)
                {
                    _affinitySelection = _affinityOptions.Count - 1;
                }
            }
        }

        // Initialize stance dropdown
        var allStances = _world?.Gameplay?.CombatStances;
        if (allStances != null)
        {
            string? currentStanceRef = null;
            _avatar.CombatProfile.TryGetValue("Stance", out currentStanceRef);

            foreach (var stance in allStances)
            {
                _stanceOptions.Add(stance.RefName);

                if (stance.RefName == currentStanceRef)
                {
                    _stanceSelection = _stanceOptions.Count - 1;
                }
            }
        }
    }

    /// <summary>
    /// Render the modal UI content.
    /// </summary>
    public void Render()
    {
        ImGui.PushFont(UIConstants.FontTitle);
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1.0f), "CHANGE LOADOUT");
        ImGui.PopFont();
        ImGui.Separator();
        ImGui.Spacing();

        // Affinity dropdown
        RenderAffinityDropdown();

        // Stance dropdown
        RenderStanceDropdown();

        ImGui.Separator();
        ImGui.Spacing();

        // Equipment slots (render all slots that have options)
        foreach (var slotName in _slotOptions.Keys)
        {
            RenderEquipmentSlotDropdown(slotName);
        }

        ImGui.Spacing();

        // Action buttons - use ButtonRow pattern for evenly spaced buttons
        var result = ImGuiHelpers.OkCancelButtons("Accept", "Cancel");
        if (result == 0)
        {
            OnAcceptPressed();
        }
        else if (result == 1)
        {
            Console.WriteLine("Equipment change cancelled");
            Cancelled?.Invoke();
        }
    }

    private void RenderEquipmentSlotDropdown(string slotName)
    {
        // Use table-like alignment with AlignTextToFramePadding
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{slotName}:");
        ImGui.SameLine(100 * UIConstants.DpiScale);

        if (_slotOptions.TryGetValue(slotName, out var options) &&
            _slotSelections.TryGetValue(slotName, out var selectedIndex))
        {
            // Fill remaining width
            ImGuiHelpers.FullWidth();

            var items = options.Select(o =>
            {
                if (o == "-- None --") return o;
                var eq = _world.GetEquipmentByRefName(o);
                return eq?.DisplayName ?? o;
            }).ToArray();

            if (ImGui.Combo($"##{slotName}", ref selectedIndex, items, items.Length))
            {
                _slotSelections[slotName] = selectedIndex;
            }
        }
    }

    private void RenderAffinityDropdown()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Affinity:");
        ImGui.SameLine(100 * UIConstants.DpiScale);
        ImGuiHelpers.FullWidth();

        var items = _affinityOptions.Select(a =>
        {
            var affinity = _world.TryGetCharacterAffinityByRefName(a);
            return affinity?.DisplayName ?? a;
        }).ToArray();

        ImGui.Combo("##Affinity", ref _affinitySelection, items, items.Length);
    }

    private void RenderStanceDropdown()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Stance:");
        ImGui.SameLine(100 * UIConstants.DpiScale);
        ImGuiHelpers.FullWidth();

        var items = _stanceOptions.Select(s =>
        {
            var stance = _world.Gameplay?.CombatStances?.FirstOrDefault(st => st.RefName == s);
            return stance?.DisplayName ?? s;
        }).ToArray();

        ImGui.Combo("##Stance", ref _stanceSelection, items, items.Length);
    }

    private void OnAcceptPressed()
    {
        var changes = new List<string>();

        // Check affinity change
        if (_affinitySelection >= 0 && _affinitySelection < _affinityOptions.Count)
        {
            var selectedAffinityRef = _affinityOptions[_affinitySelection];
            if (selectedAffinityRef != _avatar.AffinityRef)
            {
                changes.Add($"Affinity:{selectedAffinityRef}");
                Console.WriteLine($"Affinity change: {selectedAffinityRef}");
            }
        }

        // Check stance change
        if (_stanceSelection >= 0 && _stanceSelection < _stanceOptions.Count)
        {
            var selectedStanceRef = _stanceOptions[_stanceSelection];
            string? currentStanceRef = null;
            _avatar.CombatProfile.TryGetValue("Stance", out currentStanceRef);

            if (selectedStanceRef != currentStanceRef)
            {
                changes.Add($"Stance:{selectedStanceRef}");
                Console.WriteLine($"Stance change: {selectedStanceRef}");
            }
        }

        // Check equipment changes
        foreach (var kvp in _slotSelections)
        {
            var slotName = kvp.Key;
            var selectedIndex = kvp.Value;

            if (!_slotOptions.TryGetValue(slotName, out var options)) continue;
            if (selectedIndex < 0 || selectedIndex >= options.Count) continue;

            var selectedEquipmentRef = options[selectedIndex];

            // Get current equipment
            string? currentEquipmentRef = null;
            _avatar.CombatProfile.TryGetValue(slotName, out currentEquipmentRef);

            // Check if it changed
            if (selectedEquipmentRef != currentEquipmentRef)
            {
                if (selectedEquipmentRef == "-- None --" || string.IsNullOrEmpty(selectedEquipmentRef))
                {
                    // Removing equipment from slot
                    if (!string.IsNullOrEmpty(currentEquipmentRef))
                    {
                        changes.Add($"{slotName}:REMOVE");
                        Console.WriteLine($"Equipment removal: {slotName} (was {currentEquipmentRef})");
                    }
                    continue;
                }

                changes.Add($"{slotName}:{selectedEquipmentRef}");
                Console.WriteLine($"Equipment change: {slotName} -> {selectedEquipmentRef}");
            }
        }

        if (changes.Count == 0)
        {
            Console.WriteLine("No equipment changes");
            Cancelled?.Invoke();
            return;
        }

        // Join all changes with commas
        var parameter = string.Join(",", changes);
        Console.WriteLine($"Equipment changes accepted: {parameter}");
        EquipmentChanged?.Invoke(parameter);
    }
}
