using ImGuiNET;
using System.Numerics;
using Ambient.Domain;

namespace Ambient.Saga.Presentation.UI.Components.Utilities;

/// <summary>
/// Helper methods for rendering common UI components in ImGui
/// </summary>
public static class ImGuiHelpers
{
    /// <summary>
    /// Renders a CharacterEffects display showing stat modifiers with +/- formatting
    /// Matches WPF CharacterEffectsDisplay.xaml (aka VitalEffectsDisplay) functionality
    /// </summary>
    public static void RenderCharacterEffects(CharacterEffects? effects, string title = "Effects:")
    {
        if (effects == null) return;

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 1, 1), title);
        ImGui.Indent(10);

        RenderEffectLine("Health:", effects.Health);
        RenderEffectLine("Stamina:", effects.Stamina);
        RenderEffectLine("Mana:", effects.Mana);
        RenderEffectLine("Temperature:", effects.Temperature);
        RenderEffectLine("Hunger:", effects.Hunger);
        RenderEffectLine("Thirst:", effects.Thirst);
        RenderEffectLine("Insulation:", effects.Insulation);
        RenderEffectLine("Strength:", effects.Strength);
        RenderEffectLine("Defense:", effects.Defense);
        RenderEffectLine("Speed:", effects.Speed);
        RenderEffectLine("Magic:", effects.Magic);

        ImGui.Unindent(10);
    }

    /// <summary>
    /// Renders a single effect line with +/- formatting (e.g., "+5.0", "-3.5", "0")
    /// </summary>
    private static void RenderEffectLine(string label, float value)
    {
        // Show all non-default values
        var isDefault = Math.Abs(value - 1.0f) < 0.001f || Math.Abs(value) < 0.001f;
        if (isDefault) return;

        ImGui.Text(label);
        ImGui.SameLine(120);

        var color = value > 0
            ? new Vector4(0.5f, 1, 0.5f, 1)  // Green for positive
            : new Vector4(1, 0.5f, 0.5f, 1); // Red for negative

        ImGui.TextColored(color, $"{value:+0.0;-0.0;0}");
    }

    /// <summary>
    /// Renders a two-column stat line (label on left, value on right)
    /// </summary>
    public static void RenderStatLine(string label, string value, int labelWidth = 120)
    {
        ImGui.Text(label);
        ImGui.SameLine(labelWidth);
        ImGui.Text(value);
    }

    /// <summary>
    /// Renders a colored header for a section
    /// </summary>
    public static void RenderSectionHeader(string text, Vector4 color)
    {
        ImGui.TextColored(color, text);
    }
}
