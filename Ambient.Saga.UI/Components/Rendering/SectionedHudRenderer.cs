using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI;
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

    // Base layout constants at 96 DPI (1.0 scale)
    private const float CornerPaddingBase = 8f;
    private const float TopLeftCornerWidthBase = 150f;
    private const float TopLeftCornerHeightBase = 80f;
    private const float TopRightCornerWidthBase = 280f;
    private const float TopRightCornerHeightBase = 200f; // Taller to fit navigation + debug text

    // DPI-scaled corner dimensions
    private static float CornerPadding => CornerPaddingBase * UIConstants.DpiScale;
    private static float TopLeftCornerWidth => TopLeftCornerWidthBase * UIConstants.DpiScale;
    private static float TopLeftCornerHeight => TopLeftCornerHeightBase * UIConstants.DpiScale;
    private static float TopRightCornerWidth => TopRightCornerWidthBase * UIConstants.DpiScale;
    private static float TopRightCornerHeight => TopRightCornerHeightBase * UIConstants.DpiScale;

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

    // Base layout sizes at 96 DPI (1.0 scale) - scaled at runtime for DPI awareness
    private const float LeftRegionWidthBase = 160f;
    private const float RightRegionWidthBase = 160f;

    // DPI-scaled layout sizes
    private static float LeftRegionFixedWidth => LeftRegionWidthBase * UIConstants.DpiScale;
    private static float RightRegionFixedWidth => RightRegionWidthBase * UIConstants.DpiScale;
    private static float CenterRegionFixedWidth => HotbarSection.TotalWidth;
    private static float BarFixedWidth => LeftRegionFixedWidth + CenterRegionFixedWidth + RightRegionFixedWidth;
    private static float ContentHeight => HotbarSection.SlotHeight;

    public void Render(SagaMainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize, bool hasActiveToastMessages = false)
    {
        // All sizes are fixed, not based on display size
        var hudHeight = ContentHeight;
        var barWidth = BarFixedWidth;
        var leftWidth = LeftRegionFixedWidth;
        var centerWidth = CenterRegionFixedWidth;
        var rightWidth = RightRegionFixedWidth;

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

        // Calculate height to extend from top padding down to just above the bottom HUD bar
        var topRightHeight = context.DisplaySize.Y - CornerPadding - context.HudHeight - CornerPadding;

        ImGui.SetNextWindowPos(new Vector2(context.DisplaySize.X - TopRightCornerWidth - CornerPadding, CornerPadding));
        ImGui.SetNextWindowSize(new Vector2(TopRightCornerWidth, topRightHeight));

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

        // Remove window padding and border so content area matches window size exactly
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

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

        ImGui.PopStyleVar(2);
    }
}
