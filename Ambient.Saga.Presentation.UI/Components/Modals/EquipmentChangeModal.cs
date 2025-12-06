using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal for changing complete loadout (equipment, affinity, stance).
/// </summary>
public class EquipmentChangeModal
{
    private readonly Combatant _player;
    private readonly World _world;
    private readonly List<string> _playerAffinityRefs;
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

    public EquipmentChangeModal(Combatant player, World world, List<string> playerAffinityRefs)
    {
        _player = player;
        _world = world;
        _playerAffinityRefs = playerAffinityRefs;

        InitializeDropdowns();
    }

    private void InitializeDropdowns()
    {
        // Initialize equipment slots
        var slots = new[] { "Head", "Chest", "Legs", "Feet", "LeftHand", "RightHand" };
        foreach (var slotName in slots)
        {
            var options = new List<string> { "-- None --" };
            var selectedIndex = 0;

            // Get current equipment for this slot
            string? currentEquipmentRef = null;
            if (_player.CombatProfile.TryGetValue(slotName, out var equipped))
            {
                currentEquipmentRef = equipped;
            }

            // Get equipment the player actually HAS
            if (_player.Capabilities?.Equipment != null)
            {
                foreach (var entry in _player.Capabilities.Equipment)
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
        foreach (var affinityRef in _playerAffinityRefs)
        {
            var affinity = _world.TryGetCharacterAffinityByRefName(affinityRef);
            if (affinity != null)
            {
                _affinityOptions.Add(affinity.RefName);

                if (affinity.RefName == _player.AffinityRef)
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
            _player.CombatProfile.TryGetValue("Stance", out currentStanceRef);

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
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1.0f), "⚙️ CHANGE LOADOUT");
        ImGui.Separator();
        ImGui.Spacing();

        // Affinity dropdown
        RenderAffinityDropdown();

        // Stance dropdown
        RenderStanceDropdown();

        ImGui.Separator();
        ImGui.Spacing();

        // Equipment slots
        var slots = new[] { "Head", "Chest", "Legs", "Feet", "LeftHand", "RightHand" };
        foreach (var slotName in slots)
        {
            RenderEquipmentSlotDropdown(slotName);
        }

        ImGui.Spacing();

        // Action buttons
        if (ImGui.Button("✓ Accept", new Vector2(190, 50)))
        {
            OnAcceptPressed();
        }
        ImGui.SameLine();
        if (ImGui.Button("✗ Cancel", new Vector2(190, 50)))
        {
            Console.WriteLine("Equipment change cancelled");
            Cancelled?.Invoke();
        }
    }

    private void RenderEquipmentSlotDropdown(string slotName)
    {
        ImGui.Text($"{slotName}:");
        ImGui.SameLine(100);

        if (_slotOptions.TryGetValue(slotName, out var options) &&
            _slotSelections.TryGetValue(slotName, out var selectedIndex))
        {
            ImGui.SetNextItemWidth(300);

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
        ImGui.Text("Affinity:");
        ImGui.SameLine(100);
        ImGui.SetNextItemWidth(300);

        var items = _affinityOptions.Select(a =>
        {
            var affinity = _world.TryGetCharacterAffinityByRefName(a);
            return affinity?.DisplayName ?? a;
        }).ToArray();

        ImGui.Combo("##Affinity", ref _affinitySelection, items, items.Length);
    }

    private void RenderStanceDropdown()
    {
        ImGui.Text("Stance:");
        ImGui.SameLine(100);
        ImGui.SetNextItemWidth(300);

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
            if (selectedAffinityRef != _player.AffinityRef)
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
            _player.CombatProfile.TryGetValue("Stance", out currentStanceRef);

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
            _player.CombatProfile.TryGetValue(slotName, out currentEquipmentRef);

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
