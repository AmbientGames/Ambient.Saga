namespace Ambient.Rpg.Rendering.DirectX;

/// <summary>
/// LIFO stack of open modals with ESC key edge detection — one home for both games'
/// ImGui modal systems (the per-app managers and IModal contracts stay per-app; this
/// is the piece that never diverged).
/// </summary>
public class ModalStack
{
    private readonly Stack<string> _modalStack = new();
    private bool _escKeyWasDown;

    /// <summary>
    /// Push a modal onto the stack (modal is opening).
    /// </summary>
    public void Push(string modalName)
    {
        _modalStack.Push(modalName);
        // Consume the current ESC key state so the modal that just opened
        // doesn't immediately close from the same keypress that opened it
        _escKeyWasDown = true;
        ModalPushed?.Invoke(modalName);
    }

    /// <summary>
    /// Pop a modal from the stack (modal is closing).
    /// Supports out-of-order removal.
    /// </summary>
    public void Pop(string modalName)
    {
        if (_modalStack.Count > 0 && _modalStack.Peek() == modalName)
        {
            _modalStack.Pop();
        }
        else
        {
            // Modal closing out of order — rebuild stack without it
            var temp = new Stack<string>(_modalStack.Reverse());
            _modalStack.Clear();
            foreach (var modal in temp)
            {
                if (modal != modalName)
                    _modalStack.Push(modal);
            }
        }
        ModalPopped?.Invoke(modalName);
    }

    /// <summary>
    /// Check if this modal is on top of the stack (should handle input).
    /// </summary>
    public bool IsTopModal(string modalName) =>
        _modalStack.Count > 0 && _modalStack.Peek() == modalName;

    /// <summary>
    /// True if any modal is open.
    /// </summary>
    public bool HasModals => _modalStack.Count > 0;

    /// <summary>
    /// The name of the topmost modal, or null.
    /// </summary>
    public string? TopModal => _modalStack.Count > 0 ? _modalStack.Peek() : null;

    /// <summary>
    /// Number of modals currently in the stack.
    /// </summary>
    public int Depth => _modalStack.Count;

    /// <summary>
    /// Check if a specific modal is anywhere in the stack.
    /// </summary>
    public bool Contains(string modalName) => _modalStack.Contains(modalName);

    /// <summary>
    /// Get all modal names currently in the stack (top to bottom).
    /// </summary>
    public IEnumerable<string> GetStack() => _modalStack.ToArray();

    /// <summary>
    /// Check if ESC key was just pressed (transition from up to down).
    /// Edge detection prevents the same press from triggering multiple actions.
    /// </summary>
    public bool WasEscJustPressed(bool escKeyIsDown)
    {
        bool justPressed = escKeyIsDown && !_escKeyWasDown;
        _escKeyWasDown = escKeyIsDown;
        return justPressed;
    }

    /// <summary>
    /// Clear the entire stack.
    /// </summary>
    public void Clear()
    {
        _modalStack.Clear();
        _escKeyWasDown = false;
    }

    public event Action<string>? ModalPushed;
    public event Action<string>? ModalPopped;
}
