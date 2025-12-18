using Ambient.Saga.UI.Components;
using Ambient.Saga.UI.Components.Input;
using Ambient.Saga.UI.Components.Rendering;
using Ambient.Saga.UI.Components.Modals;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Examples;

/// <summary>
/// Example demonstrating how to use the extensible GameplayOverlay
/// with custom input handling and HUD rendering.
/// </summary>
public static class GameplayOverlayExamples
{
    /// <summary>
    /// Example 1: Basic usage with default controls
    /// </summary>
    public static GameplayOverlay CreateBasicOverlay(ModalManager modalManager)
    {
        // Uses default M/C/I/ESC keyboard controls and standard bottom HUD
        return new GameplayOverlay(modalManager);
    }

    /// <summary>
    /// Example 2: Basic usage with pause menu event subscription
    /// </summary>
    public static GameplayOverlay CreateOverlayWithPauseMenu(ModalManager modalManager)
    {
        var overlay = new GameplayOverlay(modalManager);
        
        // Subscribe to pause menu event
        overlay.InputHandler.PauseMenuRequested += () =>
        {
            // Show your game's pause menu
            // Example: modalManager.ShowPauseMenu = true;
            // Or: ShowYourPauseMenuDialog();
            System.Diagnostics.Debug.WriteLine("Pause menu requested via event");
        };
        
        return overlay;
    }

    /// <summary>
    /// Example 3: Polling-based pause menu detection (in game loop)
    /// </summary>
    public static void RenderLoopWithPauseMenuPolling(GameplayOverlay overlay, ModalManager modalManager, MainViewModel viewModel)
    {
        // In your render loop:
        overlay.Render(viewModel, IntPtr.Zero, 1920, 1080);
        
        // Poll for pause menu request
        if (overlay.InputHandler.WasPauseMenuRequested)
        {
            // Show your game's pause menu
            // Example: modalManager.ShowPauseMenu = true;
            // Or: ShowYourPauseMenuDialog();
            System.Diagnostics.Debug.WriteLine("Pause menu requested via polling");
        }
    }

    /// <summary>
    /// Example 4: Custom keyboard controls (function keys)
    /// </summary>
    public static GameplayOverlay CreateFunctionKeyOverlay(ModalManager modalManager)
    {
        var functionKeyInput = new FunctionKeyInputHandler();
        return new GameplayOverlay(modalManager, functionKeyInput);
    }

    /// <summary>
    /// Example 5: Multiple control schemes (keyboard + gamepad)
    /// </summary>
    public static GameplayOverlay CreateMultiInputOverlay(ModalManager modalManager)
    {
        var compositeInput = new CompositeInputHandler();
        compositeInput.AddHandler(new DefaultInputHandler());     // M/C/I
        compositeInput.AddHandler(new ArrowKeyInputHandler());    // Arrow keys
        // compositeInput.AddHandler(new GamepadInputHandler());  // Add gamepad if available
        
        return new GameplayOverlay(modalManager, compositeInput);
    }

    /// <summary>
    /// Example 6: Immersive mode (no HUD, custom input)
    /// </summary>
    public static GameplayOverlay CreateImmersiveOverlay(ModalManager modalManager)
    {
        var noHud = new NoHudRenderer();
        var customInput = new ImmersiveInputHandler();
        
        return new GameplayOverlay(modalManager, customInput, noHud);
    }

    /// <summary>
    /// Example 7: Streamer mode (compact HUD with custom layout)
    /// </summary>
    public static GameplayOverlay CreateStreamerOverlay(ModalManager modalManager)
    {
        var compactHud = new CompactHudRenderer();
        
        return new GameplayOverlay(modalManager, null, compactHud);
    }
}

// ============================================================================
// EXAMPLE IMPLEMENTATIONS
// ============================================================================

/// <summary>
/// Example: Function key input handler (F1/F2/F3 instead of M/C/I)
/// </summary>
public class FunctionKeyInputHandler : IInputHandler
{
    private bool _f1WasPressed = false;
    private bool _f2WasPressed = false;
    private bool _f3WasPressed = false;
    private bool _escWasPressed = false;
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

        if (context.IsModalActive || context.IsTextInputActive)
            return;

        // F1 = Map
        bool f1Down = ImGui.IsKeyDown(ImGuiKey.F1);
        if (f1Down && !_f1WasPressed)
            context.TogglePanelAction(ActivePanel.Map);
        _f1WasPressed = f1Down;

        // F2 = Character
        bool f2Down = ImGui.IsKeyDown(ImGuiKey.F2);
        if (f2Down && !_f2WasPressed)
            context.TogglePanelAction(ActivePanel.Character);
        _f2WasPressed = f2Down;

        // F3 = World Info
        bool f3Down = ImGui.IsKeyDown(ImGuiKey.F3);
        if (f3Down && !_f3WasPressed)
            context.TogglePanelAction(ActivePanel.WorldInfo);
        _f3WasPressed = f3Down;

        // ESC = Close or Pause
        bool escDown = ImGui.IsKeyDown(ImGuiKey.Escape);
        if (escDown && !_escWasPressed)
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
        _escWasPressed = escDown;
    }
}

/// <summary>
/// Example: Immersive input handler - Tab to cycle panels, ESC to close
/// </summary>
public class ImmersiveInputHandler : IInputHandler
{
    private bool _tabWasPressed = false;
    private bool _escWasPressed = false;
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

        if (context.IsModalActive || context.IsTextInputActive)
            return;

        // Tab cycles through panels
        bool tabDown = ImGui.IsKeyDown(ImGuiKey.Tab);
        if (tabDown && !_tabWasPressed)
        {
            var nextPanel = context.ActivePanel switch
            {
                ActivePanel.None => ActivePanel.Map,
                ActivePanel.Map => ActivePanel.Character,
                ActivePanel.Character => ActivePanel.WorldInfo,
                ActivePanel.WorldInfo => ActivePanel.None,
                _ => ActivePanel.None
            };
            context.TogglePanelAction(nextPanel);
        }
        _tabWasPressed = tabDown;

        // ESC closes all or requests pause
        bool escDown = ImGui.IsKeyDown(ImGuiKey.Escape);
        if (escDown && !_escWasPressed)
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
        _escWasPressed = escDown;
    }
}

/// <summary>
/// Example: No HUD renderer for immersive mode
/// </summary>
public class NoHudRenderer : IHudRenderer
{
    public void Render(MainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize)
    {
        // Render nothing - completely immersive
    }
}

/// <summary>
/// Example: Compact HUD for streaming (minimal overlay)
/// </summary>
public class CompactHudRenderer : IHudRenderer
{
    public void Render(MainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize)
    {
        // Small corner HUD with just essentials
        ImGui.SetNextWindowPos(new Vector2(10, 10));
        ImGui.SetNextWindowSize(new Vector2(200, 80));
        
        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.1f, 0.8f));

        if (ImGui.Begin("##CompactHud", windowFlags))
        {
            // Active panel indicator
            if (activePanel != ActivePanel.None)
            {
                ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1), $"Panel: {activePanel}");
            }

            // Position
            if (viewModel.HasAvatarPosition)
            {
                ImGui.Text($"Pos: ({viewModel.AvatarLatitude:F1}, {viewModel.AvatarLongitude:F1})");
            }

            // Status
            if (!string.IsNullOrEmpty(viewModel.StatusMessage))
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), viewModel.StatusMessage);
            }
        }
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }
}
