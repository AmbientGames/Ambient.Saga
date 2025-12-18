using Ambient.Saga.Presentation.UI.ViewModels;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering;

/// <summary>
/// Interface for rendering the HUD (Heads-Up Display) in the gameplay overlay.
/// Allows customization of the always-visible UI elements.
/// </summary>
public interface IHudRenderer
{
    /// <summary>
    /// Render the HUD.
    /// </summary>
    /// <param name="viewModel">Main view model with world/avatar state</param>
    /// <param name="activePanel">Currently active panel</param>
    /// <param name="displaySize">Display size in pixels</param>
    void Render(MainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize);
}
