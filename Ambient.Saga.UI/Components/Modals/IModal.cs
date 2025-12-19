namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Interface for modal dialogs in the modal system.
/// Provides lifecycle hooks and rendering capability.
/// </summary>
public interface IModal
{
    /// <summary>
    /// Unique name for this modal (used in modal stack).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Check if this modal can be opened with the given context.
    /// Used for validation before opening.
    /// </summary>
    bool CanOpen(object? context) => true;

    /// <summary>
    /// Called when the modal is about to open.
    /// Use this to initialize state from context.
    /// </summary>
    void OnOpening(object? context) { }

    /// <summary>
    /// Render the modal. Set isOpen to false to close.
    /// </summary>
    void Render(object? context, ref bool isOpen);

    /// <summary>
    /// Called when the modal has been closed.
    /// Use this for cleanup (cancel async operations, clear state, etc.).
    /// </summary>
    void OnClosed() { }

    /// <summary>
    /// Called when another modal opens on top of this one.
    /// The modal is still in the stack but obscured.
    /// </summary>
    void OnObscured() { }

    /// <summary>
    /// Called when obscuring modal closes and this becomes top again.
    /// </summary>
    void OnRevealed() { }
}
