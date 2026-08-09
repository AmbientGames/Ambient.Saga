using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain.Battle;
using Ambient.Rpg.Rendering.DirectX;
using Ambient.Rpg.Ui.Components.Utilities;
using ImGuiNET;
using System;
using System.Numerics;

namespace Ambient.Rpg.Ui.Components.Modals;

/// <summary>
/// Modal for selecting a spell to cast.
/// </summary>
public class SpellSelectionModal
{
    private readonly Combatant _avatar;
    private readonly IWorld _world;

    // Event fired when user selects a spell
    public event Action<string>? SpellSelected;

    // Event fired when user cancels
    public event Action? Cancelled;

    public SpellSelectionModal(Combatant avatar, IWorld world)
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
        ImGui.TextColored(new Vector4(0.5f, 0.3f, 0.8f, 1.0f), "CAST SPELL");
        ImGui.PopFont();
        ImGui.Separator();
        ImGui.Spacing();

        var buttonHeight = ImGui.GetFrameHeight() * 1.2f;

        // Check if avatar has any spells
        if (_avatar.Capabilities?.Spells == null || _avatar.Capabilities.Spells.Length == 0)
        {
            ImGui.Text("No spells available!");
            ImGui.Spacing();

            if (ImGui.Button("OK", new Vector2(ImGuiSizes.Fill, buttonHeight)))
            {
                Cancelled?.Invoke();
            }
            return;
        }

        // Spell buttons - full width
        foreach (var spellEntry in _avatar.Capabilities.Spells)
        {
            if (spellEntry.Condition <= 0) continue; // Skip broken spells

            var spell = _world.GetSpellByRefName(spellEntry.SpellRef);
            if (spell == null) continue;

            var spellRef = spellEntry.SpellRef; // Capture for lambda
            ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonInfo);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonInfoHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonInfoActive);
            var spellClicked = ImGui.Button($"{spell.DisplayName} ({spellEntry.Condition:P0})", new Vector2(ImGuiSizes.Fill, buttonHeight));
            ImGui.PopStyleColor(3);
            if (spellClicked)
            {
                Console.WriteLine($"Spell selected: {spell.DisplayName} ({spellRef})");
                SpellSelected?.Invoke(spellRef);
            }
        }

        ImGui.Spacing();

        // Cancel button - full width
        if (ImGui.Button("Cancel", new Vector2(ImGuiSizes.Fill, buttonHeight)))
        {
            Console.WriteLine("Spell selection cancelled");
            Cancelled?.Invoke();
        }
    }
}
