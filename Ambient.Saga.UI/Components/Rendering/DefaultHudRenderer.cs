using System.Diagnostics;
using Ambient.Domain.Enums;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering;

/// <summary>
/// Minimal survival-focused HUD renderer.
/// Shows resource bars (HP/Stamina/Mana), temperature warnings, and compact hotkey hints.
/// Philosophy: The 3D world is the focus - HUD shows only survival essentials.
/// </summary>
public class DefaultHudRenderer : IHudRenderer
{
    // Resource bar styling - dynamic sizing
    private const float MinBarWidth = 80f;
    private const float MaxBarWidth = 150f;
    private const float BarHeight = 14f;
    private const float BarSpacing = 8f;

    public void Render(SagaMainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize, bool hasActiveToastMessages = false)
    {
        // Calculate HUD height - two rows: resource bars + hotkeys
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var rowHeight = Math.Max(BarHeight, textHeight) + style.FramePadding.Y;
        var hudHeight = rowHeight * 2 + style.WindowPadding.Y * 2 + style.ItemSpacing.Y;

        // Position at bottom of screen
        ImGui.SetNextWindowPos(new Vector2(0, displaySize.Y - hudHeight));
        ImGui.SetNextWindowSize(new Vector2(displaySize.X, hudHeight));

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse |
                          ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.85f));

        if (ImGui.Begin("##HudBar", windowFlags))
        {
            // ROW 1: Resource bars (left) + Temperature warning (if abnormal)
            RenderResourceBars(viewModel);

            // ROW 2: Hotkey hints (left: gameplay, right: dev tools)
            RenderHotkeyRow(viewModel, activePanel, displaySize);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    // Cached bar width for current frame
    private float _currentBarWidth = MaxBarWidth;

    private void RenderResourceBars(SagaMainViewModel viewModel)
    {
        var stats = viewModel.Avatar?.Stats;
        if (stats == null)
        {
            ImGui.TextColored(UIColors.TextDisabled, "No avatar");
            return;
        }

        // Calculate bar width based on available space (3 bars + spacing + temperature)
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var tempWarningWidth = 60f; // Reserve space for temperature warning
        var totalSpacing = BarSpacing * 4; // Between bars and before temp
        var barsWidth = availableWidth - tempWarningWidth - totalSpacing;
        _currentBarWidth = Math.Clamp(barsWidth / 3f, MinBarWidth, MaxBarWidth);

        // Health bar (red)
        RenderResourceBar("HP", stats.Health, 1.0f,
            UIColors.BarHealth, UIColors.BarHealthBg);
        ImGui.SameLine(0, BarSpacing);

        // Stamina bar (green)
        RenderResourceBar("ST", stats.Stamina, 1.0f,
            UIColors.BarStamina, UIColors.BarStaminaBg);
        ImGui.SameLine(0, BarSpacing);

        // Mana bar (blue)
        RenderResourceBar("MP", stats.Mana, 1.0f,
            UIColors.BarMana, UIColors.BarManaBg);

        // Temperature warning (only if abnormal)
        var tempStatus = GetTemperatureStatus(stats.Temperature);
        if (tempStatus != TemperatureStatus.Normal)
        {
            ImGui.SameLine(0, BarSpacing * 2);
            RenderTemperatureWarning(tempStatus, stats.Temperature);
        }
    }

    private void RenderResourceBar(string label, float current, float max, Vector4 fillColor, Vector4 bgColor)
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var barWidth = _currentBarWidth;

        // Clamp value to 0-max range
        var fraction = Math.Clamp(current / max, 0f, 1f);

        // Background
        drawList.AddRectFilled(pos, new Vector2(pos.X + barWidth, pos.Y + BarHeight),
            ImGui.ColorConvertFloat4ToU32(bgColor), 3f);

        // Fill
        if (fraction > 0)
        {
            drawList.AddRectFilled(pos, new Vector2(pos.X + barWidth * fraction, pos.Y + BarHeight),
                ImGui.ColorConvertFloat4ToU32(fillColor), 3f);
        }

        // Border
        drawList.AddRect(pos, new Vector2(pos.X + barWidth, pos.Y + BarHeight),
            ImGui.ColorConvertFloat4ToU32(UIColors.BarBorder), 3f);

        // Label centered in bar (only if bar is wide enough)
        var labelText = $"{label} {(int)(fraction * 100)}%";
        var textSize = ImGui.CalcTextSize(labelText);
        if (barWidth >= textSize.X + 8 && BarHeight >= textSize.Y)
        {
            var textPos = new Vector2(
                pos.X + (barWidth - textSize.X) / 2,
                pos.Y + (BarHeight - textSize.Y) / 2);
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(UIColors.BarText), labelText);
        }

        // Advance cursor
        ImGui.Dummy(new Vector2(barWidth, BarHeight));
    }

    private enum TemperatureStatus { Normal, Cold, Hot }

    private TemperatureStatus GetTemperatureStatus(float temperature)
    {
        if (temperature < ThermalConstants.ColdWarning) return TemperatureStatus.Cold;
        if (temperature > ThermalConstants.HotWarning) return TemperatureStatus.Hot;
        return TemperatureStatus.Normal;
    }

    private void RenderTemperatureWarning(TemperatureStatus status, float temperature)
    {
        var (text, color) = status switch
        {
            TemperatureStatus.Cold => ("COLD", new Vector4(0.4f, 0.7f, 1f, 1f)),
            TemperatureStatus.Hot => ("HOT", new Vector4(1f, 0.5f, 0.2f, 1f)),
            _ => ("", new Vector4(1f, 1f, 1f, 1f))
        };

        // Flashing effect for urgent warnings
        var flash = (float)Math.Sin(ImGui.GetTime() * 4) * 0.3f + 0.7f;
        var flashedColor = color with { W = flash };

        ImGui.TextColored(flashedColor, text);
    }

    private void RenderHotkeyRow(SagaMainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize)
    {
        // Left side: Gameplay hotkey hints (compact)
        if (viewModel.HeightMapImage != null)
        {
            RenderCompactHotkey("M", activePanel == ActivePanel.Map);
            ImGui.SameLine(0, 4);
        }
        RenderCompactHotkey("C", activePanel == ActivePanel.Character);
        ImGui.SameLine(0, 4);
        RenderCompactHotkey("I", activePanel == ActivePanel.Inventory);
        ImGui.SameLine(0, 4);
        RenderCompactHotkey("J", activePanel == ActivePanel.Journal);

        // Right side: Developer tools (only when debugger attached)
        if (Debugger.IsAttached)
        {
            var devWidth = CalcCompactHotkeyWidth("F1") + CalcCompactHotkeyWidth("F12") + 12;
            var devStartX = displaySize.X - devWidth - ImGui.GetStyle().WindowPadding.X;

            ImGui.SameLine(devStartX);
            RenderCompactHotkey("F1", activePanel == ActivePanel.WorldInfo, isDev: true);
            ImGui.SameLine(0, 4);
            RenderCompactHotkey("F12", activePanel == ActivePanel.DevTools, isDev: true);
        }
    }

    private float CalcCompactHotkeyWidth(string key)
    {
        var textSize = ImGui.CalcTextSize(key);
        return textSize.X + ImGui.GetStyle().FramePadding.X * 2 + 4;
    }

    private void RenderCompactHotkey(string key, bool isActive, bool isDev = false)
    {
        Vector4 bgColor, textColor;

        if (isDev)
        {
            bgColor = isActive
                ? UIColors.HotkeyDevActiveBg
                : UIColors.HotkeyDevInactiveBg;
            textColor = isActive
                ? UIColors.HotkeyDevActiveText
                : UIColors.HotkeyDevInactiveText;
        }
        else
        {
            bgColor = isActive
                ? UIColors.HotkeyActiveBg
                : UIColors.HotkeyInactiveBg;
            textColor = isActive
                ? UIColors.HotkeyActiveText
                : UIColors.TextDisabled;
        }

        ImGui.PushStyleColor(ImGuiCol.Button, bgColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bgColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bgColor);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);

        var textSize = ImGui.CalcTextSize(key);
        ImGui.Button(key, new Vector2(textSize.X + ImGui.GetStyle().FramePadding.X * 2, 0));

        ImGui.PopStyleColor(4);
    }
}
