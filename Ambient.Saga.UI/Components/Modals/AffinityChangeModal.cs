using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for quick affinity change during battle.
/// Provides a small health bonus as a defensive action.
/// </summary>
public class AffinityChangeModal
{
    private readonly Combatant _avatar;
    private readonly IWorld _world;
    private readonly List<string> _availableAffinities;

    public event Action<string>? AffinitySelected;
    public event Action? Cancelled;

    public AffinityChangeModal(Combatant avatar, IWorld world, List<string> availableAffinities)
    {
        _avatar = avatar;
        _world = world;
        _availableAffinities = availableAffinities ?? new List<string>();
    }

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the modal using helper
        ImGuiHelpers.SetupModalWindow(400, 350);

        // Style with cyan/teal border (evasion/elemental theme) — DPI-scaled
        var scale = UIConstants.DpiScale;
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.2f, 0.7f, 0.7f, 1.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 3f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20 * scale, 20 * scale));

        if (ImGui.Begin("Change Affinity###AffinityChangeModal", ref isOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
        {
            // Title
            ImGui.PushFont(UIConstants.FontTitle);
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.8f, 1.0f), "Change Affinity");
            ImGui.PopFont();
            ImGui.Spacing();

            // Info text
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Switching affinity grants +5% health");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "and defensive positioning.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Current affinity
            var currentAffinityName = GetAffinityDisplayName(_avatar.AffinityRef);
            ImGui.Text($"Current: ");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), currentAffinityName);
            ImGui.Spacing();
            ImGui.Spacing();

            // Affinity buttons
            foreach (var affinityRef in _availableAffinities)
            {
                var affinity = _world.TryGetCharacterAffinityByRefName(affinityRef);
                var displayName = affinity?.DisplayName ?? affinityRef;
                var isCurrent = affinityRef == _avatar.AffinityRef;

                // Disable current affinity
                if (isCurrent)
                {
                    ImGui.BeginDisabled();
                }

                // Style based on affinity (could be color-coded based on element)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.35f, 0.35f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.5f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.65f, 0.65f, 1.0f));

                var buttonHeight = ImGui.GetFrameHeight() * 1.2f;
                if (ImGui.Button(isCurrent ? $"{displayName} (Current)" : displayName, new Vector2(ImGuiSizes.Fill, buttonHeight)))
                {
                    AffinitySelected?.Invoke(affinityRef);
                    isOpen = false;
                }

                ImGui.PopStyleColor(3);

                if (isCurrent)
                {
                    ImGui.EndDisabled();
                }
            }

            if (_availableAffinities.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "No other affinities available");
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

    private string GetAffinityDisplayName(string? affinityRef)
    {
        if (string.IsNullOrEmpty(affinityRef)) return "None";
        var affinity = _world.TryGetCharacterAffinityByRefName(affinityRef);
        return affinity?.DisplayName ?? affinityRef;
    }
}
