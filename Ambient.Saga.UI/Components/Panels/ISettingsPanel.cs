namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Interface for custom settings panels.
/// Implement this to provide your own settings UI when the pause menu Settings button is clicked.
/// </summary>
public interface ISettingsPanel
{
    /// <summary>
    /// Render the settings panel UI.
    /// </summary>
    /// <param name="isOpen">Whether the settings panel is open. Set to false to close.</param>
    void Render(ref bool isOpen);
}
