using ImGuiNET;

namespace Ambient.Saga.UI.Components.Input;

/// <summary>
/// Example custom input handler that uses arrow keys instead of M/C/I.
/// Demonstrates how to create custom input mappings for GameplayOverlay.
/// 
/// Key mappings:
/// - Arrow Left: Toggle Map panel
/// - Arrow Right: Toggle Character panel  
/// - Arrow Up: Toggle World Info panel
/// - Arrow Down or ESC: Close all panels / Pause menu
/// </summary>
public class ArrowKeyInputHandler : IInputHandler
{
    private bool _leftArrowWasPressed = false;
    private bool _rightArrowWasPressed = false;
    private bool _upArrowWasPressed = false;
    private bool _downArrowWasPressed = false;
    private bool _escKeyWasPressed = false;
    private bool _pauseMenuRequestedThisFrame = false;

    public event Action? PauseMenuRequested;

    public bool WasPauseMenuRequested
    {
        get
        {
            var result = _pauseMenuRequestedThisFrame;
            _pauseMenuRequestedThisFrame = false;
            return result;
        }
    }

    public void ProcessInput(InputContext context)
    {
        _pauseMenuRequestedThisFrame = false;

        // Skip input processing when modal is active or text input is focused
        if (context.IsModalActive || context.IsTextInputActive)
            return;

        // Left Arrow - Map
        bool leftArrowDown = ImGui.IsKeyDown(ImGuiKey.LeftArrow);
        if (leftArrowDown && !_leftArrowWasPressed)
        {
            context.TogglePanelAction(ActivePanel.Map);
        }
        _leftArrowWasPressed = leftArrowDown;

        // Right Arrow - Character
        bool rightArrowDown = ImGui.IsKeyDown(ImGuiKey.RightArrow);
        if (rightArrowDown && !_rightArrowWasPressed)
        {
            context.TogglePanelAction(ActivePanel.Character);
        }
        _rightArrowWasPressed = rightArrowDown;

        // Up Arrow - World Info
        bool upArrowDown = ImGui.IsKeyDown(ImGuiKey.UpArrow);
        if (upArrowDown && !_upArrowWasPressed)
        {
            context.TogglePanelAction(ActivePanel.WorldInfo);
        }
        _upArrowWasPressed = upArrowDown;

        // Down Arrow - Close all panels / Pause menu
        bool downArrowDown = ImGui.IsKeyDown(ImGuiKey.DownArrow);
        if (downArrowDown && !_downArrowWasPressed)
        {
            if (context.ActivePanel != ActivePanel.None)
            {
                context.CloseAllPanelsAction();
            }
            else
            {
                _pauseMenuRequestedThisFrame = true;
                PauseMenuRequested?.Invoke();
            }
        }
        _downArrowWasPressed = downArrowDown;

        // ESC - Close all panels / Pause menu
        bool escKeyDown = ImGui.IsKeyDown(ImGuiKey.Escape);
        if (escKeyDown && !_escKeyWasPressed)
        {
            if (context.ActivePanel != ActivePanel.None)
            {
                context.CloseAllPanelsAction();
            }
            else
            {
                _pauseMenuRequestedThisFrame = true;
                PauseMenuRequested?.Invoke();
            }
        }
        _escKeyWasPressed = escKeyDown;
    }
}
