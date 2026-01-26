using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Components.Rendering.Sections;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering;

/// <summary>
/// A modular HUD renderer that composes multiple IHudSection instances into a 5-region layout.
///
/// Layout:
/// ┌─────────────────────────────────────────────────────────────┐
/// │ [TopLeft]                                      [TopRight]   │
/// │  Status effects,                               World info,  │
/// │  body temp icon                                time/weather │
/// │                                                             │
/// │                        3D WORLD                             │
/// │                                                             │
/// ├─────────────────────────────────────────────────────────────┤
/// │ [BottomLeft] │    [BottomCenter]      │ [BottomRight]       │
/// │  HP/Stamina  │   Blocks hotbar (ext)  │  Interaction hints  │
/// └─────────────────────────────────────────────────────────────┘
///
/// Key principles:
/// - Corners = info (passive)
/// - Bottom-center = actions (Schema extension slot for blocks/tools)
/// - Bars for spendables, icons for states, numbers live in menus
/// </summary>
public class SectionedHudRenderer : IHudRenderer
{
    private readonly List<IHudSection> _sections;

    // Layout constants
    private const float CornerPadding = 8f;
    private const float TopLeftCornerWidth = 150f;
    private const float TopLeftCornerHeight = 80f;
    private const float TopRightCornerWidth = 280f;
    private const float TopRightCornerHeight = 200f; // Taller to fit navigation + debug text

    /// <summary>
    /// Create a SectionedHudRenderer with default sections.
    /// </summary>
    public SectionedHudRenderer() : this(
        new ResourceBarsSection(),      // BottomLeft: HP/Stamina/Mana
        new StatusEffectsSection(),     // TopLeft: debuffs, body temp
        new WorldInfoSection(),         // TopRight: time, weather
        new InteractionHintsSection(),  // BottomRight: context hints
        new HotbarSection())            // BottomCenter: hotbar (1-9)
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

    // Fixed content height matching hotbar slot height
    private const float ContentHeight = 40f;

    public void Render(SagaMainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize, bool hasActiveToastMessages = false)
    {
        // HUD height is fixed to match hotbar slot height (no extra padding)
        var style = ImGui.GetStyle();
        var hudHeight = ContentHeight;

        // Calculate bottom bar region widths (bar is half screen width, centered)
        var barWidth = displaySize.X * 0.5f;
        var availableWidth = barWidth - style.WindowPadding.X * 2;
        var leftWidth = availableWidth * 0.25f;
        var centerWidth = availableWidth * 0.50f;
        var rightWidth = availableWidth * 0.25f;

        // Create context
        var context = new HudContext
        {
            ViewModel = viewModel,
            ActivePanel = activePanel,
            DisplaySize = displaySize,
            HudHeight = hudHeight,
            BarWidth = barWidth,
            LeftRegionWidth = leftWidth,
            CenterRegionWidth = centerWidth,
            RightRegionWidth = rightWidth,
            HasActiveToastMessages = hasActiveToastMessages
        };

        // Group sections by region
        var topLeftSections = _sections.Where(s => s.Region == HudRegion.TopLeft).OrderBy(s => s.Priority).ToList();
        var topRightSections = _sections.Where(s => s.Region == HudRegion.TopRight).OrderBy(s => s.Priority).ToList();
        var bottomLeftSections = _sections.Where(s => s.Region == HudRegion.BottomLeft).OrderBy(s => s.Priority).ToList();
        var bottomCenterSections = _sections.Where(s => s.Region == HudRegion.BottomCenter).OrderBy(s => s.Priority).ToList();
        var bottomRightSections = _sections.Where(s => s.Region == HudRegion.BottomRight).OrderBy(s => s.Priority).ToList();

        // Render corner overlays
        RenderTopLeftOverlay(context, topLeftSections);
        RenderTopRightOverlay(context, topRightSections);

        // Render bottom bar
        RenderBottomBar(context, bottomLeftSections, bottomCenterSections, bottomRightSections, hudHeight);
    }

    private void RenderTopLeftOverlay(HudContext context, List<IHudSection> sections)
    {
        if (sections.Count == 0) return;

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse |
                          ImGuiWindowFlags.NoBackground |
                          ImGuiWindowFlags.NoInputs;

        ImGui.SetNextWindowPos(new Vector2(CornerPadding, CornerPadding));
        ImGui.SetNextWindowSize(new Vector2(TopLeftCornerWidth, TopLeftCornerHeight));

        if (ImGui.Begin("##TopLeftHud", windowFlags))
        {
            foreach (var section in sections)
            {
                section.Render(context);
            }
        }
        ImGui.End();
    }

    private void RenderTopRightOverlay(HudContext context, List<IHudSection> sections)
    {
        if (sections.Count == 0) return;

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse |
                          ImGuiWindowFlags.NoBackground |
                          ImGuiWindowFlags.NoInputs;

        ImGui.SetNextWindowPos(new Vector2(context.DisplaySize.X - TopRightCornerWidth - CornerPadding, CornerPadding));
        ImGui.SetNextWindowSize(new Vector2(TopRightCornerWidth, TopRightCornerHeight));

        if (ImGui.Begin("##TopRightHud", windowFlags))
        {
            foreach (var section in sections)
            {
                section.Render(context);
            }
        }
        ImGui.End();
    }

    private void RenderBottomBar(HudContext context, List<IHudSection> leftSections,
        List<IHudSection> centerSections, List<IHudSection> rightSections, float hudHeight)
    {
        // Position at bottom of screen, centered horizontally
        var barX = (context.DisplaySize.X - context.BarWidth) / 2f;
        ImGui.SetNextWindowPos(new Vector2(barX, context.DisplaySize.Y - hudHeight));
        ImGui.SetNextWindowSize(new Vector2(context.BarWidth, hudHeight));

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse |
                          ImGuiWindowFlags.NoBringToFrontOnFocus |
                          ImGuiWindowFlags.NoBackground;

        // Remove window padding so content sits at exact bottom
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        if (ImGui.Begin("##HudBar", windowFlags))
        {
            // Render left sections (resource bars)
            ImGui.BeginGroup();
            foreach (var section in leftSections)
            {
                section.Render(context);
            }
            ImGui.EndGroup();

            // Render center sections (hotbar) - no gap
            if (centerSections.Count > 0)
            {
                ImGui.SameLine(context.LeftRegionWidth);
                ImGui.BeginGroup();
                foreach (var section in centerSections)
                {
                    section.Render(context);
                }
                ImGui.EndGroup();
            }

            // Render right sections (interaction hints) - no gap
            if (rightSections.Count > 0)
            {
                ImGui.SameLine(context.LeftRegionWidth + context.CenterRegionWidth);
                ImGui.BeginGroup();
                foreach (var section in rightSections)
                {
                    section.Render(context);
                }
                ImGui.EndGroup();
            }
        }
        ImGui.End();

        ImGui.PopStyleVar();
    }
}
