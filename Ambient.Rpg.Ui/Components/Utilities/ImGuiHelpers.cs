using ImGuiNET;
using System.Numerics;
using Ambient.Domain;
using Ambient.Rpg.Rendering.DirectX;

namespace Ambient.Rpg.Ui.Components.Utilities;

public static class ImGuiSizes
{
    /// <summary>
    /// Use with ImGui item widths/sizes to mean "fill available width".
    /// ImGui.NET convention: -1f.
    /// </summary>
    public const float Fill = -1f;
}

/// <summary>
/// Helper methods for rendering common UI components in ImGui.
/// </summary>
public static class ImGuiHelpers
{
    #region Layout Helpers (Pixel-Perfect Sizing)

    /// <summary>
    /// Makes the next widget fill the remaining horizontal width.
    /// This is the idiomatic way to create full-width inputs, buttons, etc.
    /// Use this instead of manual width calculations.
    /// </summary>
    public static void FullWidth()
    {
        ImGui.SetNextItemWidth(ImGuiSizes.Fill);
    }

    /// <summary>
    /// Gets the remaining height available, minus space for footer rows.
    /// Use for main content areas that need to leave room for buttons at bottom.
    /// </summary>
    /// <param name="footerRows">Number of button/widget rows at bottom (default 1)</param>
    /// <param name="includeSeparator">Whether to account for a separator line</param>
    /// <returns>Height for the main content area</returns>
    public static float RemainingHeight(int footerRows = 1, bool includeSeparator = true)
    {
        var avail = ImGui.GetContentRegionAvail();
        var footerHeight = ImGui.GetFrameHeightWithSpacing() * footerRows;
        if (includeSeparator)
            footerHeight += ImGui.GetStyle().ItemSpacing.Y;
        return avail.Y - footerHeight;
    }

    /// <summary>
    /// Gets the standard height for N rows of framed widgets (buttons, inputs, etc).
    /// Includes spacing between rows.
    /// </summary>
    public static float RowsHeight(int rows)
    {
        return ImGui.GetFrameHeightWithSpacing() * rows;
    }

    /// <summary>
    /// Gets the remaining content region as a Vector2.
    /// Shorthand for ImGui.GetContentRegionAvail().
    /// </summary>
    public static Vector2 AvailableSize() => ImGui.GetContentRegionAvail();

    /// <summary>
    /// Creates a full-width button. Returns true if clicked.
    /// </summary>
    public static bool FullWidthButton(string label, float height = 0)
    {
        var size = new Vector2(ImGuiSizes.Fill, height > 0 ? height : ImGui.GetFrameHeight());
        return ImGui.Button(label, size);
    }

    /// <summary>
    /// Creates a full-width selectable. Returns true if clicked.
    /// </summary>
    public static bool FullWidthSelectable(string label, bool selected = false)
    {
        FullWidth();
        return ImGui.Selectable(label, selected);
    }

    #endregion

    #region Label + Value Rendering
    /// <summary>
    /// Renders an Attributes display showing stat modifiers with +/- formatting
    /// Matches WPF AttributesDisplay.xaml functionality
    /// </summary>
    public static void RenderAttributes(IStatAttributes? effects, string title = "Effects:", string warmingLabel = "Warming:", string coolingLabel = "Cooling:")
    {
        if (effects == null) return;

        ImGui.TextColored(UIColors.TextHighlight, title);
        ImGui.Indent(10 * UIConstants.DpiScale);

        // Use a table for proper auto-sizing column alignment
        if (ImGui.BeginTable("EffectsTable", 2, ImGuiTableFlags.SizingFixedFit))
        {
            // Label column auto-fits to content width, value column takes remainder
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            // Resources
            RenderEffectLine("Health:", effects.Health);
            RenderEffectLine("Stamina:", effects.Stamina);
            RenderEffectLine("Mana:", effects.Mana);
            // State — pick label by sign of temperature delta
            RenderEffectLine(effects.Temperature >= 0 ? warmingLabel : coolingLabel, effects.Temperature);
            // Attributes
            RenderEffectLine("Strength:", effects.Strength);
            RenderEffectLine("Defense:", effects.Defense);
            RenderEffectLine("Magic:", effects.Magic);
            RenderEffectLine("Speed:", effects.Speed);
            RenderEffectLine("Endurance:", effects.Endurance);

            ImGui.EndTable();
        }

        ImGui.Unindent(10 * UIConstants.DpiScale);
    }

    /// <summary>
    /// Renders a single effect line with +/- formatting (e.g., "+5.0", "-3.5", "0")
    /// Uses table row when called within a BeginTable context.
    /// </summary>
    private static void RenderEffectLine(string label, float value)
    {
        // Show all non-default values
        var isDefault = Math.Abs(value - 1.0f) < 0.001f || Math.Abs(value) < 0.001f;
        if (isDefault) return;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(label);

        ImGui.TableNextColumn();
        var color = value > 0
            ? UIColors.TextSuccess  // Green for positive
            : UIColors.TextDanger;  // Red for negative

        ImGui.TextColored(color, $"{value:+0.0;-0.0;0}");
    }

    /// <summary>
    /// Renders a two-column stat line (label on left, value on right).
    /// Uses AlignTextToFramePadding for proper vertical alignment.
    /// </summary>
    public static void RenderStatLine(string label, string value, int labelWidth = 120)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(labelWidth * UIConstants.DpiScale);
        ImGui.Text(value);
    }

    /// <summary>
    /// Renders a label and value using a table for proper alignment.
    /// Preferred over RenderStatLine when you have multiple rows.
    /// </summary>
    public static void LabeledValue(string label, string value, Vector4? valueColor = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine();
        if (valueColor.HasValue)
            ImGui.TextColored(valueColor.Value, value);
        else
            ImGui.Text(value);
    }

    #endregion

    #region Section Headers

    /// <summary>
    /// Renders a colored header for a section.
    /// Uses FontTitle if available.
    /// </summary>
    public static void RenderSectionHeader(string text, Vector4 color)
    {
        ImGui.PushFont(UIConstants.FontTitle);
        ImGui.TextColored(color, text);
        ImGui.PopFont();
    }

    /// <summary>
    /// Renders a section header with separator.
    /// </summary>
    public static void SectionHeader(string text, Vector4? color = null)
    {
        ImGui.Spacing();
        if (color.HasValue)
            ImGui.TextColored(color.Value, text);
        else
            ImGui.Text(text);
        ImGui.Separator();
        ImGui.Spacing();
    }

    #endregion

    #region Modal/Window Helpers

    /// <summary>
    /// Standard modal window size (centered, 50% of display).
    /// </summary>
    public static Vector2 ModalSize(float widthRatio = 0.5f, float heightRatio = 0.6f)
    {
        var viewport = ImGui.GetMainViewport();
        return new Vector2(viewport.Size.X * widthRatio, viewport.Size.Y * heightRatio);
    }

    /// <summary>
    /// Centers the next window on screen.
    /// </summary>
    public static void CenterNextWindow()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(
            new Vector2(viewport.Size.X * 0.5f, viewport.Size.Y * 0.5f),
            ImGuiCond.Appearing,
            new Vector2(0.5f, 0.5f));
    }

    /// <summary>
    /// Standard setup for a modal window: centered with fixed pixel size, scaled by DPI.
    /// The width/height values are base sizes at 100% DPI scaling.
    /// </summary>
    public static void SetupModalWindow(float width, float height)
    {
        var scale = UIConstants.DpiScale;
        CenterNextWindow();
        ImGui.SetNextWindowSize(new Vector2(width * scale, height * scale));
    }

    /// <summary>
    /// Standard setup for a modal window using display ratios (0.0-1.0).
    /// Preferred for DPI-aware layouts.
    /// </summary>
    /// <param name="widthRatio">Width as fraction of display (e.g., 0.5 = 50%)</param>
    /// <param name="heightRatio">Height as fraction of display (e.g., 0.6 = 60%)</param>
    public static void SetupModalWindowRatio(float widthRatio = 0.5f, float heightRatio = 0.6f)
    {
        CenterNextWindow();
        ImGui.SetNextWindowSize(ModalSize(widthRatio, heightRatio), ImGuiCond.FirstUseEver);
    }

    #endregion

    #region Button Layouts

    /// <summary>
    /// Renders a row of buttons at the bottom of a modal.
    /// Buttons are evenly spaced and fill the width.
    /// </summary>
    public static int ButtonRow(params string[] labels)
    {
        int clicked = -1;
        var avail = ImGui.GetContentRegionAvail();
        var style = ImGui.GetStyle();
        var buttonWidth = (avail.X - style.ItemSpacing.X * (labels.Length - 1)) / labels.Length;

        for (int i = 0; i < labels.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (ImGui.Button(labels[i], new Vector2(buttonWidth, 0)))
                clicked = i;
        }

        return clicked;
    }

    /// <summary>
    /// Renders OK and Cancel buttons. Returns 0 for OK, 1 for Cancel, -1 for neither.
    /// </summary>
    public static int OkCancelButtons(string okLabel = "OK", string cancelLabel = "Cancel")
    {
        return ButtonRow(okLabel, cancelLabel);
    }

    #endregion
}
