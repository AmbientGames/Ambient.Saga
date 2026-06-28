# GameplayOverlay Architecture

## Component Diagram

```
GameplayOverlay
  Constructor: (ModalManager, PanelManager?, IInputHandler?, IHudRenderer?)

  +------------------+  +------------------+  +------------------+  +------------------+
  |  IInputHandler   |  |   IHudRenderer   |  |  ModalManager    |  |  PanelManager    |
  |   (injected)     |  |   (injected)     |  |   (injected)     |  |   (injected)     |
  +------------------+  +------------------+  +------------------+  +------------------+
          |                      |                     |                     |
          | ProcessInput()       | Render()            | Render()           | Panel
          |                      |                     |                     |
  +----------------------------------------------------------------------+
  |                        Render() Method                                |
  |  1. Process input via handler                                        |
  |  2. Render HUD via renderer                                          |
  |  3. Render active panel (Map/Character/Inventory/Journal/Extended)   |
  |  4. Render modals via modal manager                                  |
  +----------------------------------------------------------------------+
```

## ActivePanel Enum

```
ActivePanel
  None        - No panel open (3D world view)
  Map         - Press M (requires height map)
  Character   - Press C
  Inventory   - Press I
  Journal     - Press J
  WorldInfo   - Press F1 (debugger only)
  DevTools    - Press F12 (debugger only)
  Extended    - Game-registered panel via PanelManager (e.g. F for Social)
```

All panels are mutually exclusive. Opening one closes the current. ESC closes any.

## Data Flow

```
User Input (Keyboard/Mouse/Gamepad)
    |
IInputHandler.ProcessInput(InputContext)
    |-- Checks context.IsModalActive (suppress when modals open)
    |-- Checks context.IsTextInputActive (suppress when typing)
    |
    |-- Built-in keys: M/C/I/J/F1/F12
    |-- Extended panel key: from context.PanelManager.Panel.Key
    |-- ESC: close panel or request pause menu
    |
    +-- context.TogglePanelAction(panel) --> GameplayOverlay._activePanel
            |
        Render Loop
            |-- IHudRenderer.Render() (key hints, status)
            |-- Panel rendering (switch on _activePanel)
            |   |-- Built-in panels: MapViewPanel, CharacterPanel, etc.
            |   +-- Extended: PanelManager.Panel.Render()
            +-- ModalManager.Render() (always on top)
```

## InputContext

```
InputContext
  IsModalActive: bool             (from ModalManager.HasActiveModal)
  IsTextInputActive: bool         (from ImGui.IO.WantTextInput)
  ActivePanel: ActivePanel        (current panel state)
  HasMap: bool                    (whether height map exists)
  TogglePanelAction: Action       (toggle panel on/off)
  CloseAllPanelsAction: Action    (close all panels)
  PanelManager: PanelManager?     (extended panel for key handling)
```

## Extended Panel (IPanel)

Game-specific panels implement `IPanel` and register via `PanelManager`:

```csharp
public interface IPanel
{
    string Name { get; }
    ImGuiKey Key { get; }
    string KeyLabel { get; }
    bool IsAvailable => true;
    void OnOpening() { }
    void Render(object? context, ref bool isOpen);
    void OnClosed() { }
}
```

GameplayOverlay renders the extended panel in the same full-screen frame as built-in panels (consistent margins, background color, window flags). The panel's `Render` method only provides content — the window frame is handled by GameplayOverlay.

## Dependency Graph

```
GameplayOverlay
    +-- requires: ModalManager (mandatory)
    +-- optional: PanelManager (holds game-specific extended panel)
    +-- optional: IInputHandler (defaults to DefaultInputHandler)
    +-- optional: IHudRenderer (defaults to SectionedHudRenderer)

PanelManager
    +-- holds: IPanel? (one extended panel, or null)

ModalManager
    +-- manages: all modal dialogs
    +-- provides: HasActiveModal() for input suppression
```

## Lifecycle

```
DI Registration
    +-- Register ModalManager (singleton)
    +-- Register PanelManager (singleton)
    +-- Register GameplayOverlay (singleton)

Game Startup
    +-- Register extended panel on PanelManager (optional)
    +-- Register modals on ModalManager

Game Loop (Each Frame)
    +-- overlay.Render(viewModel, heightMap, width, height)
        +-- 1. Input: inputHandler.ProcessInput(context)
        +-- 2. HUD: hudRenderer.Render(...)
        +-- 3. Panels: switch(_activePanel) with lifecycle hooks
        +-- 4. Modals: modalManager.Render(viewModel)
```

## Usage Patterns

```csharp
// Default (no customization)
new GameplayOverlay(modalManager)

// With extended panel
new GameplayOverlay(modalManager, panelManager: panelManager)

// With custom input
new GameplayOverlay(modalManager, inputHandler: myInputHandler)

// With custom HUD
new GameplayOverlay(modalManager, hudRenderer: myHudRenderer)

// Full customization
new GameplayOverlay(modalManager, panelManager, myInputHandler, myHudRenderer)
```
