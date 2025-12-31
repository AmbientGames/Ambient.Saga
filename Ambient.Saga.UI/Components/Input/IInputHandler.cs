using ImGuiNET;

namespace Ambient.Saga.UI.Components.Input;

/// <summary>
/// Interface for handling input events in the GameplayOverlay.
/// Allows customization of keyboard/mouse handling without modifying core overlay logic.
/// </summary>
public interface IInputHandler
{
    /// <summary>
    /// Called each frame to process input.
    /// </summary>
    /// <param name="context">Input context with current state and actions</param>
    void ProcessInput(InputContext context);

    /// <summary>
    /// Event raised when the user requests to open the pause menu (typically ESC key).
    /// Subscribe to this to show your game's pause menu.
    /// </summary>
    event Action? PauseMenuRequested;

    /// <summary>
    /// Gets whether a pause menu was requested this frame.
    /// Alternative to subscribing to PauseMenuRequested event - allows polling-based checks.
    /// Automatically resets to false after being read.
    /// </summary>
    bool WasPauseMenuRequested { get; }
}

/// <summary>
/// Context passed to input handlers containing current state and available actions.
/// </summary>
public class InputContext
{
    /// <summary>
    /// Gets whether any modal dialog is currently active.
    /// Input handlers should typically ignore input when modals are open.
    /// </summary>
    public bool IsModalActive { get; init; }

    /// <summary>
    /// Gets whether text input is currently active.
    /// Input handlers should typically ignore hotkeys when typing.
    /// </summary>
    public bool IsTextInputActive { get; init; }

    /// <summary>
    /// Gets the current active panel state.
    /// </summary>
    public ActivePanel ActivePanel { get; init; }

    /// <summary>
    /// Gets whether the world has a map (height map) available.
    /// Procedural/generated worlds don't have maps.
    /// </summary>
    public bool HasMap { get; init; }

    /// <summary>
    /// Action to request a panel toggle.
    /// </summary>
    public Action<ActivePanel> TogglePanelAction { get; init; } = null!;

    /// <summary>
    /// Action to close all panels.
    /// </summary>
    public Action CloseAllPanelsAction { get; init; } = null!;

    /// <summary>
    /// Gets the ImGui IO for checking key states.
    /// </summary>
    public ImGuiIOPtr IO => ImGui.GetIO();
}
