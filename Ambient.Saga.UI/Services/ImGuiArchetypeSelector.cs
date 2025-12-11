using Ambient.Domain;
using Ambient.Saga.UI.Components.Modals;

namespace Ambient.Saga.UI.Services;

/// <summary>
/// ImGui implementation of archetype selection using modal dialogs
/// Uses TaskCompletionSource pattern since ImGui is immediate-mode
/// </summary>
public class ImGuiArchetypeSelector : IArchetypeSelector
{
    private ModalManager? _modalManager;
    private TaskCompletionSource<AvatarArchetype?>? _selectionTask;
    private IEnumerable<AvatarArchetype>? _currentArchetypes;
    private string? _currentCurrencyName;

    /// <summary>
    /// Sets the modal manager - must be called after DI creates both objects
    /// </summary>
    public void SetModalManager(ModalManager modalManager)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
    }

    public Task<AvatarArchetype?> SelectArchetypeAsync(
        IEnumerable<AvatarArchetype> archetypes,
        string? currencyName)
    {
        // Store data for modal to access
        _currentArchetypes = archetypes;
        _currentCurrencyName = currencyName;

        // Create task that will complete when selection is made
        _selectionTask = new TaskCompletionSource<AvatarArchetype?>();

        // Show the modal
        _modalManager.ShowArchetypeSelection = true;

        return _selectionTask.Task;
    }

    /// <summary>
    /// Called by ArchetypeSelectionModal when user makes a selection
    /// </summary>
    public void CompleteSelection(AvatarArchetype? selectedArchetype)
    {
        _selectionTask?.SetResult(selectedArchetype);
        _selectionTask = null;
        _currentArchetypes = null;
        _currentCurrencyName = null;
    }

    /// <summary>
    /// Called by ArchetypeSelectionModal when user cancels
    /// </summary>
    public void CancelSelection()
    {
        CompleteSelection(null);
    }

    public IEnumerable<AvatarArchetype>? CurrentArchetypes => _currentArchetypes;
    public string? CurrentCurrencyName => _currentCurrencyName;
}
