using System.Diagnostics;
using ImGuiNET;

namespace Ambient.Saga.UI.Components.Input;

/// <summary>
/// Default input handler for GameplayOverlay using M/C/I/Insert/ESC keys.
/// This is the standard keyboard mapping but can be replaced with custom implementations.
///
/// Key Bindings:
/// - M: Toggle Map panel
/// - C: Toggle Character panel
/// - I: Toggle World Info panel
/// - Insert: Toggle Dev Tools panel (only when debugger attached)
/// - ESC: Close panels OR open pause menu (hierarchical behavior)
/// 
/// ESC Key Behavior (Hierarchical "Go Back"):
/// 1. If a panel is open ? Close the panel
/// 2. If no panels are open ? Raise PauseMenuRequested event
/// 
/// Pause Menu Notification:
/// Two ways to detect pause menu requests:
/// 1. Event-based: Subscribe to PauseMenuRequested event
/// 2. Polling-based: Check WasPauseMenuRequested property each frame
/// </summary>
public class DefaultInputHandler : IInputHandler
{
    // Track key states to detect press (not hold)
    private bool _mKeyWasPressed = false;
    private bool _cKeyWasPressed = false;
    private bool _iKeyWasPressed = false;
    private bool _insertKeyWasPressed = false;
    private bool _escKeyWasPressed = false;
    private bool _pauseMenuRequestedThisFrame = false;

    /// <summary>
    /// Event raised when ESC is pressed with no panels open.
    /// Subscribe to this to show your game's pause menu.
    /// </summary>
    public event Action? PauseMenuRequested;

    /// <summary>
    /// Gets whether a pause menu was requested this frame.
    /// Alternative to subscribing to PauseMenuRequested event - allows polling-based checks.
    /// Automatically resets to false after being read.
    /// </summary>
    public bool WasPauseMenuRequested
    {
        get
        {
            var result = _pauseMenuRequestedThisFrame;
            _pauseMenuRequestedThisFrame = false; // Auto-reset after read
            return result;
        }
    }

    public void ProcessInput(InputContext context)
    {
        // Reset pause menu flag at start of frame
        _pauseMenuRequestedThisFrame = false;

        // Skip input processing when modal is active or text input is focused
        if (context.IsModalActive || context.IsTextInputActive)
            return;

        // M key - Map (only if world has a height map)
        bool mKeyDown = ImGui.IsKeyDown(ImGuiKey.M);
        if (mKeyDown && !_mKeyWasPressed && context.HasMap)
        {
            context.TogglePanelAction(ActivePanel.Map);
        }
        _mKeyWasPressed = mKeyDown;

        // C key - Character
        bool cKeyDown = ImGui.IsKeyDown(ImGuiKey.C);
        if (cKeyDown && !_cKeyWasPressed)
        {
            context.TogglePanelAction(ActivePanel.Character);
        }
        _cKeyWasPressed = cKeyDown;

        // I key - World Info
        bool iKeyDown = ImGui.IsKeyDown(ImGuiKey.I);
        if (iKeyDown && !_iKeyWasPressed)
        {
            context.TogglePanelAction(ActivePanel.WorldInfo);
        }
        _iKeyWasPressed = iKeyDown;

        // Insert key - Dev Tools (only when debugger attached)
        bool insertKeyDown = ImGui.IsKeyDown(ImGuiKey.Insert);
        if (insertKeyDown && !_insertKeyWasPressed && Debugger.IsAttached)
        {
            context.TogglePanelAction(ActivePanel.DevTools);
        }
        _insertKeyWasPressed = insertKeyDown;

        // ESC key - Hierarchical behavior
        // 1. If panel open ? Close panel
        // 2. If no panels open ? Request pause menu
        bool escKeyDown = ImGui.IsKeyDown(ImGuiKey.Escape);
        if (escKeyDown && !_escKeyWasPressed)
        {
            if (context.ActivePanel != ActivePanel.None)
            {
                // Close the currently open panel
                context.CloseAllPanelsAction();
            }
            else
            {
                // No panels open - request pause menu
                _pauseMenuRequestedThisFrame = true;
                PauseMenuRequested?.Invoke();
            }
        }
        _escKeyWasPressed = escKeyDown;
    }
}
