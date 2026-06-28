# GameplayOverlay Extensibility Guide

The `GameplayOverlay` class supports extensibility through dependency injection. This allows you to customize input handling, HUD rendering, and add game-specific panels without modifying the core overlay code.

## Architecture Overview

Four extensibility points:

1. **IInputHandler** - Customize keyboard/mouse/gamepad input handling
2. **IHudRenderer** - Customize the always-visible HUD (Heads-Up Display)
3. **PanelManager** - Register a game-specific extended panel alongside the built-in M/C/I/J panels
4. **Panel System** - The built-in panel rendering system (Map, Character, Inventory, Journal)

## Basic Usage (Default Behavior)

```csharp
var gameplayOverlay = new GameplayOverlay(modalManager);

// In your render loop
gameplayOverlay.Render(viewModel, heightMapTexturePtr, heightMapWidth, heightMapHeight);
```

## Extended Panel (Game-Specific)

Games can register one extended panel that participates in the same toggle group as the built-in panels (M/C/I/J). The extended panel shares the same ESC handling, HUD key hints, and mutual exclusivity.

### Define the panel

```csharp
public class SocialPanelAdapter : IPanel
{
    public string Name => "Social";
    public ImGuiKey Key => ImGuiKey.F;
    public string KeyLabel => "F";

    public void OnOpening() { /* load data */ }
    public void Render(object? context, ref bool isOpen) { /* render content */ }
    public void OnClosed() { /* cleanup */ }
}
```

### Register via PanelManager (DI singleton)

```csharp
// In your DI setup
services.AddSingleton<PanelManager>();
services.AddSingleton<GameplayOverlay>();

// In your game startup
panelManager.RegisterPanel(new SocialPanelAdapter(gameApiClient));
```

GameplayOverlay reads the registered panel from PanelManager automatically. The panel's key hint appears inline with M/C/I/J in the HUD. Pressing the key toggles the panel, and pressing any other panel key (or ESC) closes it.

### How it works

- `IPanel.OnOpening()` is called when the panel activates
- `IPanel.Render()` is called each frame while active (inside the standard panel window frame)
- `IPanel.OnClosed()` is called when the panel deactivates (via ESC, key toggle, or another panel opening)
- `IPanel.IsAvailable` controls whether the key hint appears and the key is active (default: true)

## Pause Menu Support

The overlay provides built-in support for pause menu requests via the ESC key. Clients can be notified through events or polling.

### Event-Based (Recommended)

```csharp
var overlay = new GameplayOverlay(modalManager);
overlay.InputHandler.PauseMenuRequested += () =>
{
    modalManager.OpenPauseMenu();
};
```

### Polling-Based

```csharp
void GameLoop()
{
    overlay.Render(viewModel, ...);
    if (overlay.InputHandler.WasPauseMenuRequested)
    {
        modalManager.OpenPauseMenu();
    }
}
```

### ESC Key Behavior (Hierarchical)

1. **Panel is open** -> ESC closes the panel (built-in or extended)
2. **Nothing is open** -> ESC requests pause menu

## Custom Input Handler

```csharp
public class GamepadInputHandler : IInputHandler
{
    public event Action? PauseMenuRequested;
    public event Action<int>? HotbarSlotActivated;
    public bool WasPauseMenuRequested { get; }

    public void ProcessInput(InputContext context)
    {
        if (context.IsModalActive || context.IsTextInputActive)
            return;

        // Gamepad button toggles map
        if (IsGamepadButtonPressed(GamepadButton.Y))
            context.TogglePanelAction(ActivePanel.Map);

        // Extended panel toggle works through context.PanelManager
        if (IsGamepadButtonPressed(GamepadButton.RB))
            context.TogglePanelAction(ActivePanel.Extended);
    }
}

var overlay = new GameplayOverlay(modalManager, inputHandler: gamepadInput);
```

## Custom HUD Renderer

```csharp
public class MinimalHudRenderer : IHudRenderer
{
    public void Render(SagaMainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize, bool hasActiveToastMessages = false)
    {
        // Minimal HUD - just position
        if (viewModel.HasAvatarPosition)
        {
            ImGui.SetNextWindowPos(new Vector2(10, displaySize.Y - 30));
            if (ImGui.Begin("##Hud", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize))
                ImGui.Text($"({viewModel.AvatarLatitude:F2}, {viewModel.AvatarLongitude:F2})");
            ImGui.End();
        }
    }
}

var overlay = new GameplayOverlay(modalManager, hudRenderer: minimalHud);
```

## Constructor Parameters

```csharp
new GameplayOverlay(
    modalManager,                    // Required: modal dialog management
    panelManager: panelManager,      // Optional: game-specific extended panel
    inputHandler: myInputHandler,    // Optional: custom input (default: DefaultInputHandler)
    hudRenderer: myHudRenderer       // Optional: custom HUD (default: SectionedHudRenderer)
);
```

## See Also

- `IInputHandler.cs` - Input handler interface
- `DefaultInputHandler.cs` - Default M/C/I/J/ESC implementation
- `IHudRenderer.cs` - HUD renderer interface
- `SectionedHudRenderer.cs` - Default sectioned HUD
- `IPanel.cs` - Extended panel interface
- `PanelManager.cs` - Extended panel holder (DI singleton)
