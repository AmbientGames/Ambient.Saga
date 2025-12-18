# Pause Menu Support

## Overview

The GameplayOverlay now provides built-in support for pause menu requests via the ESC key (or any custom key binding). Clients can be notified through **two methods**: events or polling.

## ESC Key Behavior (Hierarchical)

The default input handler implements "go back one level" behavior that matches player expectations:

```
1. Modal dialog is open     ? Modal handles ESC (closes dialog)
2. Panel is open (no modal)  ? ESC closes the panel
3. Nothing is open           ? ESC requests pause menu
```

This hierarchical approach feels natural to players and matches AAA game UX patterns.

## Usage

### Method 1: Event-Based (Recommended)

Subscribe to the `PauseMenuRequested` event on the input handler:

```csharp
var overlay = new GameplayOverlay(modalManager);

// Subscribe to pause menu event
overlay.InputHandler.PauseMenuRequested += () =>
{
    // Show your game's pause menu
    ShowPauseMenu();
    
    // Or if using modal manager:
    // modalManager.ShowPauseMenu = true;
};
```

**Benefits:**
- ? Clean, event-driven architecture
- ? No polling overhead
- ? Immediate response
- ? Follows observer pattern

### Method 2: Polling-Based

Check the `WasPauseMenuRequested` property each frame:

```csharp
var overlay = new GameplayOverlay(modalManager);

// In your game loop
void Update(float deltaTime)
{
    overlay.Render(viewModel, heightMapTexturePtr, width, height);
    
    // Poll for pause menu request
    if (overlay.InputHandler.WasPauseMenuRequested)
    {
        ShowPauseMenu();
    }
}
```

**Benefits:**
- ? Simple to understand
- ? Fits well with game loop patterns
- ? Auto-resets after being read (no manual flag clearing)

**Note:** `WasPauseMenuRequested` automatically resets to `false` after being read, so you don't need to manually clear it.

## Custom Input Handlers

If you create a custom input handler, implement both the event and polling support:

```csharp
public class MyCustomInputHandler : IInputHandler
{
    private bool _pauseMenuRequestedThisFrame = false;
    
    // Event for subscribers
    public event Action? PauseMenuRequested;
    
    // Polling property (auto-reset after read)
    public bool WasPauseMenuRequested
    {
        get
        {
            var result = _pauseMenuRequestedThisFrame;
            _pauseMenuRequestedThisFrame = false; // Auto-reset
            return result;
        }
    }
    
    public void ProcessInput(InputContext context)
    {
        // Reset at start of frame
        _pauseMenuRequestedThisFrame = false;
        
        if (context.IsModalActive || context.IsTextInputActive)
            return;
        
        // Your custom input logic...
        
        // When you want to request pause menu:
        if (YourCondition())
        {
            _pauseMenuRequestedThisFrame = true;
            PauseMenuRequested?.Invoke();
        }
    }
}
```

## Examples

### Example 1: Simple Event Subscription

```csharp
var overlay = new GameplayOverlay(modalManager);
overlay.InputHandler.PauseMenuRequested += OnPauseMenuRequested;

void OnPauseMenuRequested()
{
    Console.WriteLine("Player pressed ESC - showing pause menu");
    // Your pause menu logic here
}
```

### Example 2: Polling in Game Loop

```csharp
var overlay = new GameplayOverlay(modalManager);

void GameLoop()
{
    while (isRunning)
    {
        UpdateGame(deltaTime);
        
        overlay.Render(viewModel, ...);
        
        if (overlay.InputHandler.WasPauseMenuRequested)
        {
            isPaused = true;
            ShowPauseMenuModal();
        }
    }
}
```

### Example 3: Both Methods Combined

```csharp
var overlay = new GameplayOverlay(modalManager);

// Event for immediate audio feedback
overlay.InputHandler.PauseMenuRequested += () =>
{
    PlaySound("pause_menu_open.wav");
};

// Polling for game state changes
void Update()
{
    overlay.Render(...);
    
    if (overlay.InputHandler.WasPauseMenuRequested)
    {
        gamePaused = true;
        ShowPauseMenu();
    }
}
```

## Composite Input Handlers

When using `CompositeInputHandler`, pause menu requests from **any** child handler will trigger the event:

```csharp
var composite = new CompositeInputHandler();
composite.AddHandler(new DefaultInputHandler());    // ESC key
composite.AddHandler(new GamepadInputHandler());    // Start button

// This event fires if EITHER handler requests pause menu
composite.PauseMenuRequested += ShowPauseMenu;

var overlay = new GameplayOverlay(modalManager, composite);
```

## Customizing ESC Behavior

You can customize what keys trigger the pause menu by creating a custom input handler:

```csharp
public class CustomPauseInputHandler : IInputHandler
{
    public void ProcessInput(InputContext context)
    {
        // Use P key for pause instead of ESC
        if (ImGui.IsKeyPressed(ImGuiKey.P))
        {
            _pauseMenuRequestedThisFrame = true;
            PauseMenuRequested?.Invoke();
        }
        
        // ESC still closes panels
        if (ImGui.IsKeyPressed(ImGuiKey.Escape) && context.ActivePanel != ActivePanel.None)
        {
            context.CloseAllPanelsAction();
        }
    }
}
```

## Architecture Benefits

### Before (Hardcoded)
```csharp
// ESC behavior was hardcoded - could only close panels
if (escKeyDown && _activePanel != ActivePanel.None)
{
    _activePanel = ActivePanel.None;  // ? Can't customize
}
```

**Problems:**
- ? No way to open pause menu
- ? Can't customize ESC behavior
- ? Difficult to test
- ? Doesn't match game UX patterns

### After (Extensible)
```csharp
// ESC behavior is injectable and customizable
overlay.InputHandler.PauseMenuRequested += ShowPauseMenu;
```

**Benefits:**
- ? Matches AAA game UX (ESC opens pause menu)
- ? Two notification methods (events + polling)
- ? Easy to test (mock input handler)
- ? Fully customizable
- ? Backward compatible (existing code works unchanged)

## Testing

The pause menu feature is easy to test with mock input handlers:

```csharp
[Test]
public void TestPauseMenuRequest()
{
    // Arrange
    var mockInput = new MockInputHandler();
    var overlay = new GameplayOverlay(modalManager, mockInput);
    var pauseMenuOpened = false;
    
    overlay.InputHandler.PauseMenuRequested += () => pauseMenuOpened = true;
    
    // Act
    mockInput.SimulatePauseMenuRequest();
    
    // Assert
    Assert.IsTrue(pauseMenuOpened);
}
```

## Backward Compatibility

? **Fully backward compatible**

Existing code continues to work without any changes. The pause menu feature is opt-in:

```csharp
// OLD CODE - Still works (no pause menu)
var overlay = new GameplayOverlay(modalManager);
overlay.Render(...);

// NEW CODE - Opt-in to pause menu support
var overlay = new GameplayOverlay(modalManager);
overlay.InputHandler.PauseMenuRequested += ShowPauseMenu;
overlay.Render(...);
```

## Best Practices

1. **Use events for immediate feedback** (sound effects, animations)
2. **Use polling for game state changes** (pause game, show menu)
3. **Combine both if needed** (common pattern in game loops)
4. **Always handle hierarchical behavior** (modals ? panels ? pause menu)
5. **Test with mock input handlers** (don't rely on actual keyboard input in tests)

## See Also

- `EXTENSIBILITY.md` - Complete extensibility guide
- `DefaultInputHandler.cs` - Reference implementation
- `GameplayOverlayExamples.cs` - Working code examples
- `ARCHITECTURE.md` - System architecture diagrams
