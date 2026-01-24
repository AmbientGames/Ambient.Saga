using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Bottom-right HUD section displaying interaction hints and panel hotkeys.
/// Shows context-sensitive actions (E: Use, RMB: Place) and panel shortcuts.
/// </summary>
public class InteractionHintsSection : IHudSection
{
    public HudRegion Region => HudRegion.BottomRight;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        var separatorColor = new Vector4(0.3f, 0.3f, 0.3f, 1f);

        // Panel hotkey hints (always visible)
        ImGui.BeginGroup();

        // Only show Map hint if world has a height map
        if (context.HasMap)
        {
            RenderHotkeyHint("M", "Map", context.ActivePanel == ActivePanel.Map);
            ImGui.SameLine();
            ImGui.TextColored(separatorColor, "|");
            ImGui.SameLine();
        }

        RenderHotkeyHint("C", "Character", context.ActivePanel == ActivePanel.Character);
        ImGui.SameLine();
        ImGui.TextColored(separatorColor, "|");
        ImGui.SameLine();

        RenderHotkeyHint("I", "Inventory", context.ActivePanel == ActivePanel.Inventory);
        ImGui.SameLine();
        ImGui.TextColored(separatorColor, "|");
        ImGui.SameLine();

        RenderHotkeyHint("J", "Journal", context.ActivePanel == ActivePanel.Journal);

        ImGui.EndGroup();

        // Context-sensitive interaction hints could be added here
        // Example: When looking at an interactable object, show "E: Interact"
        // This would require context from the game layer about what's targeted
    }

    private void RenderHotkeyHint(string key, string label, bool isActive)
    {
        Vector4 keyColor = isActive
            ? new Vector4(0.3f, 0.7f, 0.3f, 1f)  // Green when active
            : new Vector4(0.3f, 0.3f, 0.3f, 1f); // Dark when inactive
        Vector4 textColor = isActive
            ? new Vector4(1f, 1f, 1f, 1f)        // White when active
            : new Vector4(0.6f, 0.6f, 0.6f, 1f); // Gray when inactive

        ImGui.PushStyleColor(ImGuiCol.Button, keyColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, keyColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, keyColor);

        var textSize = ImGui.CalcTextSize(key);
        var style = ImGui.GetStyle();
        var buttonWidth = textSize.X + style.FramePadding.X * 2;
        var buttonHeight = textSize.Y + style.FramePadding.Y * 2;
        ImGui.Button(key, new Vector2(buttonWidth, buttonHeight));

        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        ImGui.TextColored(textColor, label);
    }
}
