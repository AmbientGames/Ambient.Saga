namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Holds an optional game-specific extended panel.
/// Registered as a DI singleton — games register their panel before GameplayOverlay exists.
/// GameplayOverlay reads it and integrates it into the built-in panel toggle group (M/C/I/J).
/// </summary>
public class PanelManager
{
    /// <summary>
    /// The extended panel, or null if none registered.
    /// </summary>
    public IPanel? Panel { get; private set; }

    /// <summary>
    /// Register the extended panel. Only one is supported.
    /// </summary>
    public void RegisterPanel(IPanel panel)
    {
        Panel = panel;
    }
}
