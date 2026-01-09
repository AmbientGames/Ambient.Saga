using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for quick stance change during battle.
/// Provides a small health bonus as a defensive action.
/// </summary>
public class StanceChangeModal
{
    private readonly Combatant _player;
    private readonly IWorld _world;

    public event Action<string>? StanceSelected;
    public event Action? Cancelled;

    public StanceChangeModal(Combatant player, IWorld world)
    {
        _player = player;
        _world = world;
    }

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the modal using helper
        ImGuiHelpers.SetupModalWindow(400, 400);

        // Style with orange border (combat/martial theme)
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.8f, 0.5f, 0.2f, 1.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));

        if (ImGui.Begin("Change Stance###StanceChangeModal", ref isOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
        {
            // Title
            ImGui.PushFont(UIConstants.FontTitle);
            ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.2f, 1.0f), "Change Stance");
            ImGui.PopFont();
            ImGui.Spacing();

            // Info text
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Switching stance grants +5% health");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "and defensive positioning.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Current stance
            var currentStanceRef = GetCurrentStance();
            var currentStanceName = GetStanceDisplayName(currentStanceRef);
            ImGui.Text($"Current: ");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), currentStanceName);
            ImGui.Spacing();
            ImGui.Spacing();

            // Get all available stances from world
            var stances = _world.Gameplay?.CombatStances ?? Array.Empty<CombatStance>();

            // Stance buttons
            foreach (var stance in stances)
            {
                var isCurrent = stance.RefName == currentStanceRef;

                // Disable current stance
                if (isCurrent)
                {
                    ImGui.BeginDisabled();
                }

                // Style based on stance type
                var buttonColor = GetStanceColor(stance);
                ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor * 1.3f);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor * 1.5f);

                var buttonLabel = isCurrent ? $"{stance.DisplayName} (Current)" : stance.DisplayName;
                var buttonHeight = ImGui.GetFrameHeight() * 1.2f;
                if (ImGui.Button(buttonLabel, new Vector2(ImGuiSizes.Fill, buttonHeight)))
                {
                    StanceSelected?.Invoke(stance.RefName);
                    isOpen = false;
                }

                ImGui.PopStyleColor(3);

                // Show stance effect hints
                if (stance.Effects != null)
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 150);
                    var hints = GetStanceHints(stance);
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), hints);
                }

                if (isCurrent)
                {
                    ImGui.EndDisabled();
                }
            }

            if (stances.Length == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "No stances available");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Cancel button - full width
            if (ImGui.Button("Cancel", new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight())))
            {
                Cancelled?.Invoke();
                isOpen = false;
            }
        }
        ImGui.End();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    private string? GetCurrentStance()
    {
        if (_player.CombatProfile == null) return null;
        _player.CombatProfile.TryGetValue("Stance", out var stanceRef);
        return stanceRef;
    }

    private string GetStanceDisplayName(string? stanceRef)
    {
        if (string.IsNullOrEmpty(stanceRef)) return "None";
        var stance = _world.TryGetCombatStanceByRefName(stanceRef);
        return stance?.DisplayName ?? stanceRef;
    }

    private Vector4 GetStanceColor(CombatStance stance)
    {
        // Color-code based on stance effects
        if (stance.Effects == null) return new Vector4(0.3f, 0.3f, 0.3f, 1.0f);

        // Aggressive (high strength) = red
        if (stance.Effects.Strength > 1.0f)
            return new Vector4(0.4f, 0.2f, 0.2f, 1.0f);

        // Defensive (high defense) = blue
        if (stance.Effects.Defense > 1.0f)
            return new Vector4(0.2f, 0.2f, 0.4f, 1.0f);

        // Fast (high speed) = green
        if (stance.Effects.Speed > 1.0f)
            return new Vector4(0.2f, 0.35f, 0.2f, 1.0f);

        // Magic-focused = purple
        if (stance.Effects.Magic > 1.0f)
            return new Vector4(0.35f, 0.2f, 0.4f, 1.0f);

        // Balanced = neutral
        return new Vector4(0.3f, 0.3f, 0.3f, 1.0f);
    }

    private string GetStanceHints(CombatStance stance)
    {
        if (stance.Effects == null) return "";

        var hints = new List<string>();

        if (stance.Effects.Strength > 1.0f) hints.Add($"+{(stance.Effects.Strength - 1) * 100:F0}% Str");
        else if (stance.Effects.Strength < 1.0f) hints.Add($"-{(1 - stance.Effects.Strength) * 100:F0}% Str");

        if (stance.Effects.Defense > 1.0f) hints.Add($"+{(stance.Effects.Defense - 1) * 100:F0}% Def");
        else if (stance.Effects.Defense < 1.0f) hints.Add($"-{(1 - stance.Effects.Defense) * 100:F0}% Def");

        if (stance.Effects.Speed > 1.0f) hints.Add($"+{(stance.Effects.Speed - 1) * 100:F0}% Spd");
        else if (stance.Effects.Speed < 1.0f) hints.Add($"-{(1 - stance.Effects.Speed) * 100:F0}% Spd");

        return string.Join(", ", hints.Take(2)); // Show max 2 hints
    }
}
