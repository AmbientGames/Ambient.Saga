using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Components.Rendering.Sections;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering;

/// <summary>
/// A modular HUD renderer that composes multiple IHudSection instances into a bottom bar.
///
/// Layout:
/// ┌────────────────────────────────────────────────────────────────────┐
/// │ [Left Section(s)] │ [Center Section(s)] │ [Right Section(s)]      │
/// └────────────────────────────────────────────────────────────────────┘
///
/// Games can add custom sections for tools, blocks, health bars, etc.
/// Default configuration shows status messages with hotkey hints as fallback.
/// </summary>
public class SectionedHudRenderer : IHudRenderer
{
    private readonly List<IHudSection> _sections;

    /// <summary>
    /// Create a SectionedHudRenderer with default sections (StatusSection in center).
    /// </summary>
    public SectionedHudRenderer() : this(new StatusSection())
    {
    }

    /// <summary>
    /// Create a SectionedHudRenderer with specified sections.
    /// </summary>
    public SectionedHudRenderer(params IHudSection[] sections)
    {
        _sections = sections.ToList();
    }

    /// <summary>
    /// Create a SectionedHudRenderer with a collection of sections.
    /// </summary>
    public SectionedHudRenderer(IEnumerable<IHudSection> sections)
    {
        _sections = sections.ToList();
    }

    /// <summary>
    /// Add a section to the HUD.
    /// </summary>
    public void AddSection(IHudSection section)
    {
        _sections.Add(section);
    }

    /// <summary>
    /// Remove a section from the HUD.
    /// </summary>
    public bool RemoveSection(IHudSection section)
    {
        return _sections.Remove(section);
    }

    /// <summary>
    /// Remove all sections of a specific type.
    /// </summary>
    public void RemoveSectionsOfType<T>() where T : IHudSection
    {
        _sections.RemoveAll(s => s is T);
    }

    /// <summary>
    /// Get all current sections.
    /// </summary>
    public IReadOnlyList<IHudSection> Sections => _sections.AsReadOnly();

    public void Render(MainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize)
    {
        // Calculate HUD dimensions
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;

        // Position at bottom of screen
        ImGui.SetNextWindowPos(new Vector2(0, displaySize.Y - hudHeight));
        ImGui.SetNextWindowSize(new Vector2(displaySize.X, hudHeight));

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse |
                          ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.15f, 0.9f));

        if (ImGui.Begin("##HudBar", windowFlags))
        {
            // Calculate region widths (divide into thirds, adjustable based on content)
            var availableWidth = displaySize.X - style.WindowPadding.X * 2;
            var leftWidth = availableWidth * 0.3f;
            var centerWidth = availableWidth * 0.4f;
            var rightWidth = availableWidth * 0.3f;

            // Create context
            var context = new HudContext
            {
                ViewModel = viewModel,
                ActivePanel = activePanel,
                DisplaySize = displaySize,
                HudHeight = hudHeight,
                LeftRegionWidth = leftWidth,
                CenterRegionWidth = centerWidth,
                RightRegionWidth = rightWidth
            };

            // Group and sort sections by region and priority
            var leftSections = _sections.Where(s => s.Region == HudRegion.Left).OrderBy(s => s.Priority).ToList();
            var centerSections = _sections.Where(s => s.Region == HudRegion.Center).OrderBy(s => s.Priority).ToList();
            var rightSections = _sections.Where(s => s.Region == HudRegion.Right).OrderBy(s => s.Priority).ToList();

            // Render left sections
            foreach (var section in leftSections)
            {
                section.Render(context);
                ImGui.SameLine();
            }

            // Calculate center position
            if (centerSections.Count > 0)
            {
                // Position center sections in the middle
                var centerStartX = leftWidth + style.WindowPadding.X;
                ImGui.SameLine(centerStartX);

                foreach (var section in centerSections)
                {
                    section.Render(context);
                    ImGui.SameLine();
                }
            }

            // Calculate right position
            if (rightSections.Count > 0)
            {
                // Position right sections at the right side
                var rightStartX = leftWidth + centerWidth + style.WindowPadding.X;
                ImGui.SameLine(rightStartX);

                foreach (var section in rightSections)
                {
                    section.Render(context);
                    ImGui.SameLine();
                }
            }
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }
}
