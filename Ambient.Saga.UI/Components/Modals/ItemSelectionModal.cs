using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for selecting a consumable item to use.
/// </summary>
public class ItemSelectionModal
{
    private readonly Combatant _avatar;
    private readonly IWorld _world;

    // Event fired when user selects an item
    public event Action<string>? ItemSelected;

    // Event fired when user cancels
    public event Action? Cancelled;

    public ItemSelectionModal(Combatant avatar, IWorld world)
    {
        _avatar = avatar;
        _world = world;
    }

    /// <summary>
    /// Render the modal UI content.
    /// </summary>
    public void Render()
    {
        ImGui.PushFont(UIConstants.FontTitle);
        ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.3f, 1.0f), "USE ITEM");
        ImGui.PopFont();
        ImGui.Separator();
        ImGui.Spacing();

        var buttonHeight = ImGui.GetFrameHeight() * 1.2f;

        // Check if player has any items
        if (_avatar.Capabilities?.Consumables == null || _avatar.Capabilities.Consumables.Length == 0)
        {
            ImGui.Text("No items available!");
            ImGui.Spacing();

            if (ImGui.Button("OK", new Vector2(ImGuiSizes.Fill, buttonHeight)))
            {
                Cancelled?.Invoke();
            }
            return;
        }

        // Item buttons - full width
        foreach (var itemEntry in _avatar.Capabilities.Consumables)
        {
            if (itemEntry.Quantity <= 0) continue; // Skip empty items

            var consumable = _world.GetConsumableByRefName(itemEntry.ConsumableRef);
            if (consumable == null) continue;

            var itemRef = itemEntry.ConsumableRef; // Capture for lambda
            if (ImGui.Button($"{consumable.DisplayName} x{itemEntry.Quantity}", new Vector2(ImGuiSizes.Fill, buttonHeight)))
            {
                Console.WriteLine($"Item selected: {consumable.DisplayName} ({itemRef})");
                ItemSelected?.Invoke(itemRef);
            }
        }

        ImGui.Spacing();

        // Cancel button - full width
        if (ImGui.Button("Cancel", new Vector2(ImGuiSizes.Fill, buttonHeight)))
        {
            Console.WriteLine("Item selection cancelled");
            Cancelled?.Invoke();
        }
    }
}
