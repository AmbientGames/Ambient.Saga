# GameplayOverlay Extensibility Guide

The `GameplayOverlay` class has been refactored to support extensibility through dependency injection. This allows you to customize input handling, HUD rendering, and other behaviors without modifying the core overlay code.

## Architecture Overview

The overlay now uses three main extensibility points:

1. **IInputHandler** - Customize keyboard/mouse/gamepad input handling
2. **IHudRenderer** - Customize the always-visible HUD (Heads-Up Display)
3. **Panel System** - The panel rendering system remains built-in but can be extended

## Basic Usage (Default Behavior)

```csharp
// Create with default keyboard controls (M/C/I/ESC) and standard HUD
var modalManager = new ModalManager(archetypeSelector, mediator, worldContentGenerator);
var gameplayOverlay = new GameplayOverlay(modalManager);

// In your render loop
gameplayOverlay.Render(viewModel, heightMapTexturePtr, heightMapWidth, heightMapHeight);
```

## Pause Menu Support

The overlay provides two ways to detect when the player presses ESC to open the pause menu:

### Option 1: Event-Based (Recommended)

```csharp
var overlay = new GameplayOverlay(modalManager);

// Subscribe to pause menu event
overlay.InputHandler.PauseMenuRequested += () =>
{
    // Show your game's pause menu
    modalManager.ShowPauseMenu = true;
    Console.WriteLine("Pause menu opened");
};
```

### Option 2: Polling-Based

```csharp
var overlay = new GameplayOverlay(modalManager);

// In your game loop, check for pause menu request
void GameLoop()
{
    overlay.Render(viewModel, ...);
    
    // Poll for pause menu request
    if (overlay.InputHandler.WasPauseMenuRequested)
    {
        modalManager.ShowPauseMenu = true;
    }
}
```

### ESC Key Behavior (Hierarchical)

The default input handler uses hierarchical "go back" behavior for ESC:

1. **Modal is open** ? Modal handles ESC (closes dialog)
2. **Panel is open** ? ESC closes the panel
3. **Nothing is open** ? ESC requests pause menu

This matches player expectations in modern games.

## Custom Input Handler

### Example 1: Arrow Key Controls

```csharp
// Use arrow keys instead of M/C/I
var customInput = new ArrowKeyInputHandler();
var gameplayOverlay = new GameplayOverlay(modalManager, customInput);
```

### Example 2: Custom Input Handler

```csharp
public class GamepadInputHandler : IInputHandler
{
    public void ProcessInput(InputContext context)
    {
        // Skip when modal active or typing
        if (context.IsModalActive || context.IsTextInputActive)
            return;

        // Example: Use gamepad buttons
        if (IsGamepadButtonPressed(GamepadButton.Y))
        {
            context.TogglePanelAction(ActivePanel.Map);
        }
        
        if (IsGamepadButtonPressed(GamepadButton.X))
        {
            context.TogglePanelAction(ActivePanel.Character);
        }
        
        if (IsGamepadButtonPressed(GamepadButton.B))
        {
            context.CloseAllPanelsAction();
        }
    }
    
    private bool IsGamepadButtonPressed(GamepadButton button)
    {
        // Your gamepad input detection logic here
        return false;
    }
}

// Usage
var gamepadInput = new GamepadInputHandler();
var gameplayOverlay = new GameplayOverlay(modalManager, gamepadInput);
```

### Example 3: Composite Input (Multiple Control Schemes)

```csharp
// Support both keyboard AND gamepad simultaneously
var compositeInput = new CompositeInputHandler();
compositeInput.AddHandler(new DefaultInputHandler());      // M/C/I/ESC
compositeInput.AddHandler(new GamepadInputHandler());      // Gamepad buttons
compositeInput.AddHandler(new ArrowKeyInputHandler());     // Arrow keys

var gameplayOverlay = new GameplayOverlay(modalManager, compositeInput);
```

## Custom HUD Renderer

### Example 1: Minimal HUD

```csharp
public class MinimalHudRenderer : IHudRenderer
{
    public void Render(MainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize)
    {
        // Only show avatar position, nothing else
        if (viewModel.HasAvatarPosition)
        {
            ImGui.SetNextWindowPos(new Vector2(10, displaySize.Y - 30));
            ImGui.SetNextWindowSize(new Vector2(200, 25));
            
            if (ImGui.Begin("##MinimalHud", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize))
            {
                ImGui.Text($"Pos: ({viewModel.AvatarLatitude:F2}, {viewModel.AvatarLongitude:F2})");
            }
            ImGui.End();
        }
    }
}

// Usage
var minimalHud = new MinimalHudRenderer();
var gameplayOverlay = new GameplayOverlay(modalManager, null, minimalHud);
```

### Example 2: Top Bar HUD

```csharp
public class TopBarHudRenderer : IHudRenderer
{
    public void Render(MainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize)
    {
        // Render HUD at top of screen instead of bottom
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(displaySize.X, 40));
        
        var windowFlags = ImGuiWindowFlags.NoTitleBar | 
                          ImGuiWindowFlags.NoResize | 
                          ImGuiWindowFlags.NoMove;
        
        if (ImGui.Begin("##TopHud", windowFlags))
        {
            ImGui.Text($"Active Panel: {activePanel}");
            
            if (!string.IsNullOrEmpty(viewModel.StatusMessage))
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), $" | {viewModel.StatusMessage}");
            }
        }
        ImGui.End();
    }
}
```

### Example 3: No HUD (Immersive Mode)

```csharp
public class NoHudRenderer : IHudRenderer
{
    public void Render(MainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize)
    {
        // Don't render anything - immersive mode
    }
}

// Usage - completely hide the HUD
var noHud = new NoHudRenderer();
var gameplayOverlay = new GameplayOverlay(modalManager, null, noHud);
```

## Advanced Scenarios

### Event-Driven Input Handling

```csharp
public class EventDrivenInputHandler : IInputHandler
{
    public event Action<ActivePanel>? PanelToggleRequested;
    public event Action? CloseAllRequested;
    
    private readonly Dictionary<ImGuiKey, ActivePanel> _keyMap = new()
    {
        { ImGuiKey.F1, ActivePanel.Map },
        { ImGuiKey.F2, ActivePanel.Character },
        { ImGuiKey.F3, ActivePanel.WorldInfo }
    };
    
    private Dictionary<ImGuiKey, bool> _keyStates = new();
    
    public void ProcessInput(InputContext context)
    {
        if (context.IsModalActive || context.IsTextInputActive)
            return;
        
        foreach (var (key, panel) in _keyMap)
        {
            bool isDown = ImGui.IsKeyDown(key);
            bool wasDown = _keyStates.GetValueOrDefault(key, false);
            
            if (isDown && !wasDown)
            {
                context.TogglePanelAction(panel);
                PanelToggleRequested?.Invoke(panel);
            }
            
            _keyStates[key] = isDown;
        }
    }
}

// Usage with event subscription
var eventInput = new EventDrivenInputHandler();
eventInput.PanelToggleRequested += (panel) =>
{
    Console.WriteLine($"User toggled panel: {panel}");
    // Log analytics, play sound effect, etc.
};

var gameplayOverlay = new GameplayOverlay(modalManager, eventInput);
```

### Configurable Input Bindings

```csharp
public class ConfigurableInputHandler : IInputHandler
{
    private readonly Dictionary<ImGuiKey, ActivePanel> _keyBindings = new();
    private readonly Dictionary<ImGuiKey, bool> _keyStates = new();
    
    public void BindKey(ImGuiKey key, ActivePanel panel)
    {
        _keyBindings[key] = panel;
    }
    
    public void ProcessInput(InputContext context)
    {
        if (context.IsModalActive || context.IsTextInputActive)
            return;
        
        foreach (var (key, panel) in _keyBindings)
        {
            bool isDown = ImGui.IsKeyDown(key);
            bool wasDown = _keyStates.GetValueOrDefault(key, false);
            
            if (isDown && !wasDown)
            {
                context.TogglePanelAction(panel);
            }
            
            _keyStates[key] = isDown;
        }
    }
}

// Usage - load from config file
var configurableInput = new ConfigurableInputHandler();
configurableInput.BindKey(ImGuiKey.F1, ActivePanel.Map);
configurableInput.BindKey(ImGuiKey.F2, ActivePanel.Character);
configurableInput.BindKey(ImGuiKey.Tab, ActivePanel.WorldInfo);

var gameplayOverlay = new GameplayOverlay(modalManager, configurableInput);
```

## Benefits of This Design

1. **Open/Closed Principle**: The overlay is open for extension but closed for modification
2. **Dependency Injection**: Easy to test and swap implementations
3. **Separation of Concerns**: Input handling and rendering are decoupled from overlay logic
4. **Backward Compatible**: Existing code continues to work with default implementations
5. **Composability**: Combine multiple handlers using `CompositeInputHandler`

## Migration from Old Code

Old code (hardcoded escape key):
```csharp
// ESC key handling was hardcoded in HandlePanelHotkeys()
private void HandlePanelHotkeys()
{
    // ... hardcoded key checks ...
    bool escKeyDown = ImGui.IsKeyDown(ImGuiKey.Escape);
    if (escKeyDown && !_escKeyWasPressed && _activePanel != ActivePanel.None)
    {
        _activePanel = ActivePanel.None;
    }
}
```

New code (extensible):
```csharp
// Now you can inject custom behavior
public class MyCustomInputHandler : IInputHandler
{
    public void ProcessInput(InputContext context)
    {
        // Custom logic - maybe escape opens a pause menu instead
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            OpenPauseMenu();
        }
        
        // Or just close panels
        if (ImGui.IsKeyPressed(ImGuiKey.Backspace))
        {
            context.CloseAllPanelsAction();
        }
    }
}
```

## Testing

The extensibility design makes testing much easier:

```csharp
[Test]
public void TestCustomInputHandler()
{
    // Arrange
    var mockInput = new MockInputHandler();
    var modalManager = new ModalManager(...);
    var overlay = new GameplayOverlay(modalManager, mockInput);
    
    // Act
    mockInput.SimulateKeyPress(ImGuiKey.M);
    overlay.Render(viewModel, ...);
    
    // Assert
    Assert.AreEqual(ActivePanel.Map, overlay.ActivePanel);
}
```

## See Also

- `IInputHandler.cs` - Input handler interface
- `DefaultInputHandler.cs` - Default M/C/I/ESC implementation
- `ArrowKeyInputHandler.cs` - Example arrow key implementation
- `CompositeInputHandler.cs` - Combine multiple handlers
- `IHudRenderer.cs` - HUD renderer interface
- `DefaultHudRenderer.cs` - Default bottom bar implementation
