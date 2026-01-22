using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Center HUD section that displays status messages.
/// When no status message is active, shows hotkey hints as a fallback.
/// </summary>
public class StatusSection : IHudSection
{
    public HudRegion Region => HudRegion.Center;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        // Determine what to show
        var hasStatusMessage = !string.IsNullOrEmpty(context.StatusMessage) && context.StatusMessage != "Ready";

        if (context.IsLoading)
        {
            // Loading indicator
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Loading...");
        }
        else if (hasStatusMessage)
        {
            // Active status message
            ImGui.Text(context.StatusMessage);
        }
        else
        {
            // Fallback: show hotkey hints
            RenderHotkeyHints(context);
        }
    }

    private void RenderHotkeyHints(HudContext context)
    {
        var dimColor = new Vector4(0.5f, 0.5f, 0.5f, 1f);
        var separatorColor = new Vector4(0.3f, 0.3f, 0.3f, 1f);

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
    }

    private void RenderHotkeyHint(string key, string label, bool isActive)
    {
        Vector4 keyColor = isActive
            ? new Vector4(0.3f, 0.7f, 0.3f, 1f)  // Green when active
            : new Vector4(0.3f, 0.3f, 0.3f, 1f); // Gray when inactive
        Vector4 textColor = isActive
            ? new Vector4(1f, 1f, 1f, 1f)        // White when active
            : new Vector4(0.6f, 0.6f, 0.6f, 1f); // Dim when inactive

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
