using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Domain.Rpg.Battle;
using ImGuiNET;
using System;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal for selecting a consumable item to use.
/// </summary>
public class ItemSelectionModal
{
    private readonly Combatant _player;
    private readonly World _world;

    // Event fired when user selects an item
    public event Action<string>? ItemSelected;

    // Event fired when user cancels
    public event Action? Cancelled;

    public ItemSelectionModal(Combatant player, World world)
    {
        _player = player;
        _world = world;
    }

    /// <summary>
    /// Render the modal UI content.
    /// </summary>
    public void Render()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 1.0f), "💊 USE ITEM");
        ImGui.Separator();
        ImGui.Spacing();

        // Check if player has any items
        if (_player.Capabilities?.Consumables == null || _player.Capabilities.Consumables.Length == 0)
        {
            ImGui.Text("No items available!");
            ImGui.Spacing();

            if (ImGui.Button("OK", new Vector2(200, 40)))
            {
                Cancelled?.Invoke();
            }
            return;
        }

        // Item buttons
        foreach (var itemEntry in _player.Capabilities.Consumables)
        {
            if (itemEntry.Quantity <= 0) continue; // Skip empty items

            var consumable = _world.GetConsumableByRefName(itemEntry.ConsumableRef);
            if (consumable == null) continue;

            var itemRef = itemEntry.ConsumableRef; // Capture for lambda
            if (ImGui.Button($"{consumable.DisplayName} x{itemEntry.Quantity}", new Vector2(400, 50)))
            {
                Console.WriteLine($"Item selected: {consumable.DisplayName} ({itemRef})");
                ItemSelected?.Invoke(itemRef);
            }
        }

        ImGui.Spacing();

        // Cancel button
        if (ImGui.Button("Cancel", new Vector2(400, 40)))
        {
            Console.WriteLine("Item selection cancelled");
            Cancelled?.Invoke();
        }
    }
}
