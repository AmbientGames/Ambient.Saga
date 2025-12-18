namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Tracks the stack of open modals and manages ESC key state.
/// Prevents ESC key from triggering multiple actions in the same frame.
/// 
/// Pattern: Last-In-First-Out (LIFO) - most recent modal is on top of stack.
/// ESC key closes the topmost modal, then propagates down if released and pressed again.
/// </summary>
public class ModalStack
{
    private readonly Stack<string> _modalStack = new();
    private bool _escKeyWasDown = false;

    /// <summary>
    /// Push a modal onto the stack (modal is opening).
    /// </summary>
    public void Push(string modalName)
    {
        _modalStack.Push(modalName);
        System.Diagnostics.Debug.WriteLine($"[ModalStack] Pushed: {modalName} (Stack depth: {_modalStack.Count})");
    }

    /// <summary>
    /// Pop a modal from the stack (modal is closing).
    /// </summary>
    public void Pop(string modalName)
    {
        if (_modalStack.Count > 0 && _modalStack.Peek() == modalName)
        {
            _modalStack.Pop();
            System.Diagnostics.Debug.WriteLine($"[ModalStack] Popped: {modalName} (Stack depth: {_modalStack.Count})");
        }
        else
        {
            // Modal closing out of order - just remove it
            var temp = new Stack<string>(_modalStack.Reverse());
            _modalStack.Clear();
            foreach (var modal in temp)
            {
                if (modal != modalName)
                    _modalStack.Push(modal);
            }
            System.Diagnostics.Debug.WriteLine($"[ModalStack] Removed: {modalName} (out of order, Stack depth: {_modalStack.Count})");
        }
    }

    /// <summary>
    /// Check if this modal is on top of the stack (should handle input).
    /// </summary>
    public bool IsTopModal(string modalName)
    {
        return _modalStack.Count > 0 && _modalStack.Peek() == modalName;
    }

    /// <summary>
    /// Check if any modal is open.
    /// </summary>
    public bool HasModals => _modalStack.Count > 0;

    /// <summary>
    /// Get the top modal name (null if none).
    /// </summary>
    public string? TopModal => _modalStack.Count > 0 ? _modalStack.Peek() : null;

    /// <summary>
    /// Check if ESC key was just pressed (transition from up to down).
    /// This prevents the same ESC press from triggering multiple actions.
    /// </summary>
    /// <param name="escKeyIsDown">Current state of ESC key</param>
    /// <returns>True if ESC was just pressed this frame</returns>
    public bool WasEscJustPressed(bool escKeyIsDown)
    {
        bool justPressed = escKeyIsDown && !_escKeyWasDown;
        _escKeyWasDown = escKeyIsDown;
        return justPressed;
    }

    /// <summary>
    /// Clear the entire stack (for cleanup/reset).
    /// </summary>
    public void Clear()
    {
        _modalStack.Clear();
        _escKeyWasDown = false;
        System.Diagnostics.Debug.WriteLine("[ModalStack] Cleared");
    }
}
