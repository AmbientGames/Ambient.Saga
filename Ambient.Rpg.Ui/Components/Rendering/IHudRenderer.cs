using Ambient.Rpg.Presentation.UI.ViewModels;
using Ambient.Rpg.Ui.Components.Panels;
using System.Numerics;

namespace Ambient.Rpg.Ui.Components.Rendering;

/// <summary>
/// Interface for rendering the HUD (Heads-Up Display) in the gameplay overlay.
/// Allows customization of the always-visible UI elements.
/// </summary>
public interface IHudRenderer
{
    /// <summary>
    /// Render the HUD.
    /// </summary>
    void Render(RpgMainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize, bool hasActiveToastMessages = false);

    /// <summary>
    /// Sets the panel manager for rendering key hints.
    /// </summary>
    void SetPanelManager(PanelManager panelManager) { }
}
