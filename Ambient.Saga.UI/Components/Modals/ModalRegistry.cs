namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Registry for managing modals in a consistent, extensible way.
/// Eliminates the need for manual modal field declarations and rendering boilerplate.
/// </summary>
public class ModalRegistry
{
    private readonly Dictionary<string, IModal> _modals = new();
    private readonly Dictionary<string, object?> _contexts = new();
    private readonly ModalStack _stack;

    public ModalRegistry(ModalStack stack)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));

        // Subscribe to stack events to trigger lifecycle hooks
        _stack.ModalPushed += OnModalPushed;
        _stack.ModalPopped += OnModalPopped;
    }

    /// <summary>
    /// Register a modal for management by this registry.
    /// </summary>
    public void Register(IModal modal)
    {
        if (modal == null) throw new ArgumentNullException(nameof(modal));

        if (_modals.ContainsKey(modal.Name))
            throw new InvalidOperationException($"Modal '{modal.Name}' is already registered");

        _modals[modal.Name] = modal;
    }

    /// <summary>
    /// Open a modal with optional context.
    /// </summary>
    public void Open(string name, object? context = null)
    {
        if (!_modals.TryGetValue(name, out var modal))
            return; // Silently ignore unregistered modals for backwards compatibility

        // Check if modal can be opened with this context
        if (!modal.CanOpen(context))
            return;

        // Store context for this modal
        _contexts[name] = context;

        // Push to stack (which will trigger OnModalPushed -> OnOpening)
        if (!_stack.Contains(name))
            _stack.Push(name);
    }

    /// <summary>
    /// Close a modal.
    /// </summary>
    public void Close(string name)
    {
        if (!_stack.Contains(name))
            return;

        _stack.Pop(name);
        // OnModalPopped will trigger cleanup
    }

    /// <summary>
    /// Check if a modal is currently open.
    /// </summary>
    public bool IsOpen(string name) => _stack.Contains(name);

    /// <summary>
    /// Render all open modals in the registry.
    /// Modals not in the registry are ignored (for backwards compatibility).
    /// </summary>
    /// <param name="fallbackContext">
    /// Fallback context to use if no specific context was provided when opening the modal.
    /// Typically the MainViewModel.
    /// </param>
    public void RenderRegistered(object? fallbackContext = null)
    {
        // Get all open modals from stack
        var openModals = _stack.GetStack();

        foreach (var modalName in openModals)
        {
            // Only render if this modal is registered
            if (!_modals.TryGetValue(modalName, out var modal))
                continue;

            // Get context for this modal, or use fallback
            object? context;
            if (!_contexts.TryGetValue(modalName, out context) || context == null)
            {
                context = fallbackContext;
            }

            // Render the modal
            var isOpen = true;
            modal.Render(context, ref isOpen);

            // If modal requested close, pop it from stack
            if (!isOpen)
            {
                Close(modalName);
            }
        }
    }

    private void OnModalPushed(string modalName)
    {
        if (!_modals.TryGetValue(modalName, out var modal))
            return;

        // Get the stack to check if there's a modal being obscured
        var stack = _stack.GetStack().ToList();
        var pushedIndex = stack.IndexOf(modalName);

        // Trigger OnOpening for the newly pushed modal
        _contexts.TryGetValue(modalName, out var context);
        modal.OnOpening(context);

        // If there's a modal below this one, trigger OnObscured
        if (pushedIndex > 0)
        {
            var obscuredModalName = stack[pushedIndex - 1];
            if (_modals.TryGetValue(obscuredModalName, out var obscuredModal))
            {
                obscuredModal.OnObscured();
            }
        }
    }

    private void OnModalPopped(string modalName)
    {
        if (!_modals.TryGetValue(modalName, out var modal))
            return;

        // Trigger OnClosed for the popped modal
        modal.OnClosed();

        // Clear context for this modal
        _contexts.Remove(modalName);

        // If there's a modal that's now revealed, trigger OnRevealed
        var stack = _stack.GetStack().ToList();
        if (stack.Count > 0)
        {
            var revealedModalName = stack[^1]; // Last item (top of stack)
            if (_modals.TryGetValue(revealedModalName, out var revealedModal))
            {
                revealedModal.OnRevealed();
            }
        }
    }

    /// <summary>
    /// Get context for a specific modal (for testing/debugging).
    /// </summary>
    internal object? GetContext(string modalName)
    {
        _contexts.TryGetValue(modalName, out var context);
        return context;
    }
}
