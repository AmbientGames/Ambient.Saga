using Ambient.Saga.Presentation.UI.ViewModels;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering;

/// <summary>
/// Context provided to HUD sections during rendering.
/// Contains all the data sections need to render themselves.
/// </summary>
public class HudContext
{
    /// <summary>
    /// The main view model with world/avatar state.
    /// </summary>
    public required MainViewModel ViewModel { get; init; }

    /// <summary>
    /// Currently active panel (None, Map, Character, etc.)
    /// </summary>
    public required ActivePanel ActivePanel { get; init; }

    /// <summary>
    /// Total display size in pixels.
    /// </summary>
    public required Vector2 DisplaySize { get; init; }

    /// <summary>
    /// Height of the HUD bar in pixels.
    /// </summary>
    public required float HudHeight { get; init; }

    /// <summary>
    /// Width available for the left region.
    /// </summary>
    public required float LeftRegionWidth { get; init; }

    /// <summary>
    /// Width available for the center region.
    /// </summary>
    public required float CenterRegionWidth { get; init; }

    /// <summary>
    /// Width available for the right region.
    /// </summary>
    public required float RightRegionWidth { get; init; }

    /// <summary>
    /// Whether the map is available (some worlds don't have height maps).
    /// </summary>
    public bool HasMap => ViewModel.HeightMapImage != null;

    /// <summary>
    /// Whether the game is currently loading.
    /// </summary>
    public bool IsLoading => ViewModel.IsLoading;
}
