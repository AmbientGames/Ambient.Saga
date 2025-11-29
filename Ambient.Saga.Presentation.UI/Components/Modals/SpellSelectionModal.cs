using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using ImGuiNET;
using System;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal for selecting a spell to cast.
/// </summary>
public class SpellSelectionModal
{
    private readonly Combatant _player;
    private readonly World _world;

    // Event fired when user selects a spell
    public event Action<string>? SpellSelected;

    // Event fired when user cancels
    public event Action? Cancelled;

    public SpellSelectionModal(Combatant player, World world)
    {
        _player = player;
        _world = world;
    }

    /// <summary>
    /// Render the modal UI content.
    /// </summary>
    public void Render()
    {
        ImGui.TextColored(new Vector4(0.5f, 0.3f, 0.8f, 1.0f), "✨ CAST SPELL");
        ImGui.Separator();
        ImGui.Spacing();

        // Check if player has any spells
        if (_player.Capabilities?.Spells == null || _player.Capabilities.Spells.Length == 0)
        {
            ImGui.Text("No spells available!");
            ImGui.Spacing();

            if (ImGui.Button("OK", new Vector2(200, 40)))
            {
                Cancelled?.Invoke();
            }
            return;
        }

        // Spell buttons
        foreach (var spellEntry in _player.Capabilities.Spells)
        {
            if (spellEntry.Condition <= 0) continue; // Skip broken spells

            var spell = _world.GetSpellByRefName(spellEntry.SpellRef);
            if (spell == null) continue;

            var spellRef = spellEntry.SpellRef; // Capture for lambda
            if (ImGui.Button($"{spell.DisplayName} ({spellEntry.Condition:P0})", new Vector2(400, 50)))
            {
                Console.WriteLine($"Spell selected: {spell.DisplayName} ({spellRef})");
                SpellSelected?.Invoke(spellRef);
            }
        }

        ImGui.Spacing();

        // Cancel button
        if (ImGui.Button("Cancel", new Vector2(400, 40)))
        {
            Console.WriteLine("Spell selection cancelled");
            Cancelled?.Invoke();
        }
    }
}
