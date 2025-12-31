using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Tile-based world selection screen for end users.
/// Displays worlds as clickable tiles in a 2-column scrollable grid.
/// </summary>
public class WorldSelectionTiles
{
    private const float TileWidth = 380f;
    private const float TileHeight = 140f;
    private const float TileSpacing = 15f;
    private const int TilesPerRow = 2;

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the selection window
        var viewport = ImGui.GetMainViewport();
        var windowWidth = (TileWidth * TilesPerRow) + (TileSpacing * (TilesPerRow + 1)) + 35f; // Extra for scrollbar and padding
        var windowHeight = viewport.Size.Y * 0.8f;

        ImGui.SetNextWindowPos(new Vector2(viewport.Size.X * 0.5f, viewport.Size.Y * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(windowWidth, windowHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar;

        if (!ImGui.Begin("World Selection##Tiles", windowFlags))
        {
            ImGui.End();
            return;
        }

        // Header
        ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]); // Use default font but we could use a larger one
        ImGui.TextColored(new Vector4(1, 1, 0.8f, 1), "Select Your World");
        ImGui.PopFont();
        ImGui.Separator();
        ImGui.Spacing();

        // Scrollable region for tiles
        var availableHeight = ImGui.GetContentRegionAvail().Y - 60f; // Leave space for quit button
        ImGui.BeginChild("WorldTiles", new Vector2(0, availableHeight), ImGuiChildFlags.None, ImGuiWindowFlags.AlwaysVerticalScrollbar);

        var configurations = viewModel.AvailableConfigurations;
        var configCount = configurations.Count;

        for (int i = 0; i < configCount; i++)
        {
            var config = configurations[i];
            var isSelected = viewModel.SelectedConfiguration?.RefName == config.RefName;

            // Calculate position in grid
            int col = i % TilesPerRow;

            // Start new row positioning
            if (col == 0)
            {
                ImGui.SetCursorPosX(TileSpacing);
            }
            else
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(TileSpacing + (col * (TileWidth + TileSpacing)));
            }

            // Render tile
            if (RenderWorldTile(config.RefName, config.DisplayName ?? config.RefName, config.Description, isSelected, TileWidth, TileHeight))
            {
                // Tile was clicked - select and load
                viewModel.SelectedConfiguration = config;
                if (viewModel.LoadSelectedConfigurationCommand?.CanExecute(null) == true)
                {
                    viewModel.LoadSelectedConfigurationCommand.Execute(null);
                }
            }

            // Add vertical spacing after each row
            if (col == TilesPerRow - 1 || i == configCount - 1)
            {
                ImGui.Spacing();
            }
        }

        // Handle empty state
        if (configCount == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "No worlds available.");
            ImGui.TextWrapped("Please ensure world configurations are properly installed.");
        }

        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Quit button at bottom
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.25f, 0.25f, 1));
        if (ImGui.Button("Quit Game", new Vector2(-1, 35)))
        {
            isOpen = false;
            viewModel.RaiseRequestQuit();
        }
        ImGui.PopStyleColor(3);

        ImGui.End();
    }

    private bool RenderWorldTile(string refName, string displayName, string? description, bool isSelected, float width, float height)
    {
        bool clicked = false;

        // Push unique ID for this tile
        ImGui.PushID(refName);

        // Get cursor position for drawing
        var cursorPos = ImGui.GetCursorScreenPos();

        // Invisible button for click detection
        clicked = ImGui.InvisibleButton("##tile", new Vector2(width, height));
        var isHovered = ImGui.IsItemHovered();

        // Draw tile background
        var drawList = ImGui.GetWindowDrawList();
        var bgColor = isSelected
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.4f, 0.6f, 1f))
            : isHovered
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.3f, 1f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.2f, 1f));

        var borderColor = isSelected
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.7f, 1f, 1f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.4f, 1f));

        var rectMin = cursorPos;
        var rectMax = new Vector2(cursorPos.X + width, cursorPos.Y + height);

        // Background
        drawList.AddRectFilled(rectMin, rectMax, bgColor, 8f);
        // Border
        drawList.AddRect(rectMin, rectMax, borderColor, 8f, ImDrawFlags.None, isSelected ? 3f : 1f);

        // Draw text content
        var textPadding = 12f;
        var titlePos = new Vector2(cursorPos.X + textPadding, cursorPos.Y + textPadding);

        // Title
        var titleColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0.9f, 1f));
        drawList.AddText(titlePos, titleColor, displayName);

        // Description (if available, truncated)
        if (!string.IsNullOrEmpty(description))
        {
            var descPos = new Vector2(cursorPos.X + textPadding, cursorPos.Y + textPadding + 24f);
            var descColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.8f, 1f));

            // Truncate description to fit
            var maxDescLength = 120;
            var truncatedDesc = description.Length > maxDescLength
                ? description.Substring(0, maxDescLength) + "..."
                : description;

            // Word wrap manually
            var lines = WrapText(truncatedDesc, (int)(width - textPadding * 2) / 7); // Rough char estimate
            var lineY = descPos.Y;
            var maxLines = 4;
            for (int i = 0; i < Math.Min(lines.Count, maxLines); i++)
            {
                drawList.AddText(new Vector2(descPos.X, lineY), descColor, lines[i]);
                lineY += 18f;
            }
        }

        // "Click to Play" hint at bottom
        var hintPos = new Vector2(cursorPos.X + textPadding, cursorPos.Y + height - 24f);
        var hintColor = isHovered
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.8f, 0.5f, 1f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.5f, 1f));
        drawList.AddText(hintPos, hintColor, isHovered ? "Click to Play" : "");

        ImGui.PopID();

        return clicked;
    }

    private List<string> WrapText(string text, int maxCharsPerLine)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        var words = text.Split(' ');
        var currentLine = "";

        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(currentLine))
            {
                currentLine = word;
            }
            else if (currentLine.Length + word.Length + 1 <= maxCharsPerLine)
            {
                currentLine += " " + word;
            }
            else
            {
                lines.Add(currentLine);
                currentLine = word;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return lines;
    }
}
