# GameplayOverlay Architecture

## Component Diagram

```
???????????????????????????????????????????????????????????????????
?                         GameplayOverlay                          ?
?                                                                  ?
?  Constructor: (ModalManager, IInputHandler?, IHudRenderer?)     ?
?                                                                  ?
?  ???????????????????  ????????????????????  ?????????????????? ?
?  ?  IInputHandler  ?  ?   IHudRenderer   ?  ?  ModalManager  ? ?
?  ?   (injected)    ?  ?   (injected)     ?  ?   (injected)   ? ?
?  ???????????????????  ????????????????????  ?????????????????? ?
?           ?                    ?                     ?          ?
?           ? ProcessInput()     ? Render()            ? Render() ?
?           ?                    ?                     ?          ?
?  ?????????????????????????????????????????????????????????????? ?
?  ?                      Render() Method                        ? ?
?  ?  1. Process input via handler                               ? ?
?  ?  2. Render HUD via renderer                                 ? ?
?  ?  3. Render active panel (Map/Character/WorldInfo)           ? ?
?  ?  4. Render modals via modal manager                         ? ?
?  ??????????????????????????????????????????????????????????????? ?
???????????????????????????????????????????????????????????????????
```

## Class Hierarchy

```
IInputHandler (interface)
??? DefaultInputHandler (M/C/I/ESC keys)
??? ArrowKeyInputHandler (Arrow keys)
??? CompositeInputHandler (Combines multiple handlers)
??? [Your custom implementations]
    ??? GamepadInputHandler
    ??? FunctionKeyInputHandler
    ??? ImmersiveInputHandler

IHudRenderer (interface)
??? DefaultHudRenderer (Bottom bar with hotkeys)
??? [Your custom implementations]
    ??? CompactHudRenderer (Minimal corner HUD)
    ??? TopBarHudRenderer (Top of screen)
    ??? NoHudRenderer (Immersive mode)
```

## Data Flow

```
User Input (Keyboard/Mouse/Gamepad)
    ?
    ?
IInputHandler.ProcessInput(InputContext)
    ?
    ??? Checks context.IsModalActive
    ??? Checks context.IsTextInputActive
    ?
    ??? Calls action methods:
        ??? context.TogglePanelAction(panel)
        ??? context.CloseAllPanelsAction()
            ?
            ?
        GameplayOverlay
            ??? Updates _activePanel state
            ?
            ?
        Render Loop
            ??? IHudRenderer.Render()
            ??? Panel rendering (switch on _activePanel)
            ??? ModalManager.Render()
                ?
                ?
            ImGui Output
```

## InputContext Structure

```
InputContext
??? IsModalActive: bool          (from ModalManager)
??? IsTextInputActive: bool      (from ImGui.IO)
??? ActivePanel: ActivePanel     (current panel state)
??? TogglePanelAction: Action    (toggle panel on/off)
??? CloseAllPanelsAction: Action (close all panels)
```

## Extension Points

### 1. Custom Input Handler

```csharp
public class MyInputHandler : IInputHandler
{
    public void ProcessInput(InputContext context)
    {
        // Your custom input logic
        if (MyCondition())
        {
            context.TogglePanelAction(ActivePanel.Map);
        }
    }
}
```

### 2. Custom HUD Renderer

```csharp
public class MyHudRenderer : IHudRenderer
{
    public void Render(MainViewModel viewModel, 
                       ActivePanel activePanel, 
                       Vector2 displaySize)
    {
        // Your custom HUD rendering
        ImGui.Begin("MyHUD");
        ImGui.Text($"Active: {activePanel}");
        ImGui.End();
    }
}
```

### 3. Composite Input (Multiple Handlers)

```csharp
var composite = new CompositeInputHandler();
composite.AddHandler(new DefaultInputHandler());
composite.AddHandler(new GamepadInputHandler());
composite.AddHandler(new MyCustomHandler());

// All handlers process input in sequence
```

## Usage Patterns

### Pattern 1: Default (No Customization)
```
new GameplayOverlay(modalManager)
    ??? Uses DefaultInputHandler + DefaultHudRenderer
```

### Pattern 2: Custom Input Only
```
new GameplayOverlay(modalManager, myInputHandler)
    ??? Uses myInputHandler + DefaultHudRenderer
```

### Pattern 3: Custom HUD Only
```
new GameplayOverlay(modalManager, null, myHudRenderer)
    ??? Uses DefaultInputHandler + myHudRenderer
```

### Pattern 4: Full Customization
```
new GameplayOverlay(modalManager, myInputHandler, myHudRenderer)
    ??? Uses myInputHandler + myHudRenderer
```

## Dependency Graph

```
GameplayOverlay
    ?
    ??? requires: ModalManager (mandatory)
    ??? optional: IInputHandler (defaults to DefaultInputHandler)
    ??? optional: IHudRenderer (defaults to DefaultHudRenderer)

ModalManager
    ??? manages: All modal dialogs
    ??? provides: HasActiveModal() for input suppression

IInputHandler
    ??? receives: InputContext
    ??? invokes: TogglePanelAction, CloseAllPanelsAction

IHudRenderer
    ??? receives: MainViewModel, ActivePanel, DisplaySize
    ??? renders: Always-visible UI elements
```

## Lifecycle

```
Application Startup
    ?
    ??? Create ModalManager
    ??? Create IInputHandler (optional)
    ??? Create IHudRenderer (optional)
    ?
    ??? Create GameplayOverlay(modalManager, inputHandler, hudRenderer)
        ?
        ??? Overlay ready for use

Game Loop (Each Frame)
    ?
    ??? Call overlay.Render(viewModel, heightMap, width, height)
        ?
        ??? 1. Input Processing Phase
        ?   ??? inputHandler.ProcessInput(context)
        ?
        ??? 2. HUD Rendering Phase
        ?   ??? hudRenderer.Render(viewModel, activePanel, displaySize)
        ?
        ??? 3. Panel Rendering Phase
        ?   ??? switch(activePanel) { ... }
        ?
        ??? 4. Modal Rendering Phase
            ??? modalManager.Render(viewModel)
```

## Key Principles

1. **Single Responsibility**
   - `GameplayOverlay` orchestrates the UI flow
   - `IInputHandler` handles input logic
   - `IHudRenderer` handles HUD presentation
   - `ModalManager` handles modal dialogs

2. **Open/Closed Principle**
   - Open for extension (new handlers/renderers)
   - Closed for modification (core overlay unchanged)

3. **Dependency Inversion**
   - Depends on abstractions (IInputHandler, IHudRenderer)
   - Not on concrete implementations

4. **Interface Segregation**
   - Small, focused interfaces
   - Easy to implement

5. **Liskov Substitution**
   - Any IInputHandler implementation works
   - Any IHudRenderer implementation works
