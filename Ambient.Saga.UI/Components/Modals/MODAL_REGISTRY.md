# Modal Registry Pattern

The Modal Registry Pattern provides an extensible, lifecycle-aware system for managing modal dialogs in the Ambient.Saga UI.

## Overview

The registry pattern eliminates boilerplate code and provides:
- **Automatic Lifecycle Management**: `OnOpening`, `OnClosed`, `OnObscured`, `OnRevealed` hooks
- **Context Management**: Per-modal context instead of global state
- **Extensibility**: Add new modals without modifying `ModalManager`
- **Type Safety**: Validate context before opening modals
- **Maintainability**: Reduced code in `ModalManager` (from ~390 lines to ~200 lines planned)

## Architecture

### Core Components

```
IModal (interface)
├── Defines contract for all modals
├── Lifecycle hooks (OnOpening, OnClosed, etc.)
└── Render(context, ref isOpen)

ModalRegistry (class)
├── Stores modals in Dictionary<string, IModal>
├── Manages per-modal context
├── Triggers lifecycle hooks
└── Renders registered modals

ModalManager (class)
├── Owns ModalRegistry and ModalStack
├── Provides RegisterModal() and OpenRegisteredModal()
└── Coordinates between old and new systems
```

### Integration with ModalStack

The registry leverages the existing `ModalStack` infrastructure:
- Stack events (`ModalPushed`, `ModalPopped`) trigger lifecycle hooks
- `Contains()` checks prevent duplicate registrations
- Stack order determines render order (top-to-bottom)

## Usage

### 1. Create a New Modal

Implement `IModal` interface:

```csharp
public class MyCustomModal : IModal
{
    public string Name => "MyCustom";

    public bool CanOpen(object? context)
    {
        // Optional validation
        return context is string;
    }

    public void OnOpening(object? context)
    {
        // Initialize from context
        var message = context as string;
        Console.WriteLine($"Opening with: {message}");
    }

    public void Render(object? context, ref bool isOpen)
    {
        ImGui.Begin("My Modal", ref isOpen);
        ImGui.Text(context?.ToString() ?? "No context");
        ImGui.End();
    }

    public void OnClosed()
    {
        // Cleanup
    }

    // OnObscured() and OnRevealed() are optional
}
```

### 2. Register the Modal

In `ModalManager` constructor or initialization:

```csharp
var myModal = new MyCustomModal();
modalManager.RegisterModal(myModal);
```

### 3. Open the Modal

```csharp
// Open with context
modalManager.OpenRegisteredModal("MyCustom", "Hello World");

// Open without context
modalManager.OpenRegisteredModal("MyCustom");
```

### 4. Close the Modal

Modals close automatically when `isOpen` is set to false in `Render()`, or manually:

```csharp
modalManager.CloseModal("MyCustom");
```

## Migration Guide

### Migrating Existing Modals

#### Option A: Create an Adapter (Recommended for Complex Modals)

For modals with specialized signatures like `Render(MainViewModel, CharacterViewModel, ref bool)`:

```csharp
// 1. Define a context class
public record CharacterContext(MainViewModel ViewModel, CharacterViewModel Character);

// 2. Create adapter
public class DialogueModalAdapter : IModal
{
    private readonly DialogueModal _dialogueModal = new();

    public string Name => "Dialogue";

    public void Render(object? context, ref bool isOpen)
    {
        if (context is CharacterContext ctx)
        {
            // Delegate to existing modal
            _dialogueModal.Render(ctx.ViewModel, ctx.Character, modalManager, ref isOpen);
        }
    }
}

// 3. Register adapter
modalManager.RegisterModal(new DialogueModalAdapter());

// 4. Use with context
modalManager.OpenRegisteredModal("Dialogue", new CharacterContext(viewModel, character));
```

#### Option B: Refactor Modal to Implement IModal (Best for New Modals)

```csharp
public class NewModal : IModal
{
    private MainViewModel? _viewModel;
    private CharacterViewModel? _character;

    public string Name => "NewModal";

    public void OnOpening(object? context)
    {
        if (context is CharacterContext ctx)
        {
            _viewModel = ctx.ViewModel;
            _character = ctx.Character;
        }
    }

    public void Render(object? context, ref bool isOpen)
    {
        // Use stored _viewModel and _character
        ImGui.Begin("New Modal", ref isOpen);
        // ... render logic
        ImGui.End();
    }

    public void OnClosed()
    {
        _viewModel = null;
        _character = null;
    }
}
```

### Migration Phases

**Phase 1: Setup (✅ COMPLETE)**
- Created `IModal` interface
- Created `ModalRegistry` class
- Integrated registry into `ModalManager`
- Registry renders alongside existing system

**Phase 2: Gradual Migration (IN PROGRESS)**
- Create adapters for complex modals
- Refactor simple modals to implement `IModal`
- Use registry for new modals

**Phase 3: Cleanup (FUTURE)**
- Remove manual rendering code from `ModalManager.Render()`
- Remove modal field declarations
- Consolidate all modals through registry

## Lifecycle Hooks

### OnOpening(context)
**When**: Modal is pushed to stack
**Use**: Initialize state from context
**Example**:
```csharp
public void OnOpening(object? context)
{
    if (context is QuestContext ctx)
    {
        _questRef = ctx.QuestRef;
        _sagaRef = ctx.SagaRef;
        LoadQuestData();
    }
}
```

### OnClosed()
**When**: Modal is popped from stack
**Use**: Cleanup, cancel async operations, clear state
**Example**:
```csharp
public void OnClosed()
{
    _cancellationTokenSource?.Cancel();
    _questData = null;
    _selectedItem = null;
}
```

### OnObscured()
**When**: Another modal opens on top
**Use**: Pause animations, save scroll position
**Example**:
```csharp
public void OnObscured()
{
    _scrollPosition = ImGui.GetScrollY();
    _isPaused = true;
}
```

### OnRevealed()
**When**: Obscuring modal closes
**Use**: Resume animations, restore scroll position
**Example**:
```csharp
public void OnRevealed()
{
    ImGui.SetScrollY(_scrollPosition);
    _isPaused = false;
    RefreshData(); // Modal might have been obscured for a while
}
```

## Context Patterns

### Simple Context (MainViewModel only)
```csharp
modalManager.OpenRegisteredModal("AvatarInfo", viewModel);
```

### Character Context
```csharp
public record CharacterContext(MainViewModel ViewModel, CharacterViewModel Character);
modalManager.OpenRegisteredModal("Dialogue", new CharacterContext(viewModel, character));
```

### Quest Context
```csharp
public record QuestContext(string QuestRef, string SagaRef, MainViewModel ViewModel);
modalManager.OpenRegisteredModal("Quest", new QuestContext(questRef, sagaRef, viewModel));
```

### Complex Context
```csharp
public record BattleContext(
    MainViewModel ViewModel,
    CharacterViewModel Character,
    ModalManager ModalManager,
    bool IsBossBattle
);
```

## Validation with CanOpen()

Prevent invalid modal opens:

```csharp
public bool CanOpen(object? context)
{
    // Require character context
    if (context is not CharacterContext ctx)
        return false;

    // Only open if character can be looted
    if (!ctx.Character.CanLoot)
        return false;

    return true;
}
```

If `CanOpen()` returns false, the modal will not open and no lifecycle hooks will be called.

## Best Practices

### 1. Use Descriptive Context Classes
```csharp
// Good: Clear intent
public record TradeContext(MainViewModel ViewModel, CharacterViewModel Merchant);

// Bad: Generic
public record ModalData(object Obj1, object Obj2);
```

### 2. Validate Context in CanOpen()
```csharp
public bool CanOpen(object? context)
{
    return context is TradeContext { Merchant.CanTrade: true };
}
```

### 3. Clean Up in OnClosed()
```csharp
public void OnClosed()
{
    // Always clean up
    _cancellationTokenSource?.Cancel();
    _cancellationTokenSource?.Dispose();
    _viewModel = null;
}
```

### 4. Make Modal Names Unique and Descriptive
```csharp
// Good
public string Name => "MerchantTrade";

// Bad
public string Name => "Modal1";
```

### 5. Handle Context Gracefully
```csharp
public void Render(object? context, ref bool isOpen)
{
    if (context is not ExpectedContext ctx)
    {
        // Log error and close
        Console.WriteLine($"Invalid context for {Name}");
        isOpen = false;
        return;
    }

    // Render normally
}
```

## Examples

See the following files for complete examples:
- `Examples/SimpleModalExample.cs` - Basic modal implementation
- `Examples/ModalAdapterExample.cs` - Adapter pattern for existing modals

## Future Enhancements

### Planned Improvements

1. **Modal Context Builder**: Fluent API for building complex contexts
   ```csharp
   modalManager.Open()
       .Modal("Dialogue")
       .WithViewModel(viewModel)
       .WithCharacter(character)
       .Execute();
   ```

2. **Modal Dependency Injection**: Support constructor injection for modals
   ```csharp
   public class MyModal : IModal
   {
       public MyModal(IMediator mediator, ILogger logger) { }
   }
   ```

3. **Modal Groups**: Group related modals for batch operations
   ```csharp
   modalManager.CloseGroup("CharacterInteractions");
   ```

4. **Modal Transitions**: Smooth transitions between modals
   ```csharp
   modalManager.TransitionTo("Loot", from: "Battle", animation: FadeOut);
   ```

## See Also

- `ModalStack.cs` - Stack infrastructure
- `ModalManager.cs` - Manager implementation
- `IModal.cs` - Interface definition
- `ARCHITECTURE.md` - Overall UI architecture
