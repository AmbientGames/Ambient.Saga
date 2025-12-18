using ImGuiNET;

namespace Ambient.Saga.UI.Components.Input;

/// <summary>
/// Composite input handler that combines multiple input handlers.
/// Processes input through all handlers in sequence.
/// 
/// Use case: Support multiple control schemes simultaneously
/// (e.g., both keyboard shortcuts and gamepad controls).
/// 
/// Note: PauseMenuRequested event fires if ANY child handler requests it.
/// WasPauseMenuRequested returns true if ANY child handler has a pending request.
/// </summary>
public class CompositeInputHandler : IInputHandler
{
    private readonly List<IInputHandler> _handlers = new();

    public event Action? PauseMenuRequested;

    public bool WasPauseMenuRequested => _handlers.Any(h => h.WasPauseMenuRequested);

    /// <summary>
    /// Add an input handler to the composite.
    /// Handlers are processed in the order they are added.
    /// </summary>
    public void AddHandler(IInputHandler handler)
    {
        if (handler != null)
        {
            _handlers.Add(handler);
            
            // Forward pause menu events from child handlers
            handler.PauseMenuRequested += () => PauseMenuRequested?.Invoke();
        }
    }

    public void ProcessInput(InputContext context)
    {
        foreach (var handler in _handlers)
        {
            handler.ProcessInput(context);
        }
    }
}
