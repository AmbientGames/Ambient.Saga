using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for AchievementsModal to work with the Modal Registry Pattern.
/// Demonstrates migration of a simple modal with internal state.
/// </summary>
public class AchievementsModalAdapter : IModal
{
    private readonly AchievementsModal _modal = new();

    public string Name => "Achievements";

    public bool CanOpen(object? context)
    {
        // Validate that we have a MainViewModel
        return context is MainViewModel;
    }

    public void OnOpening(object? context)
    {
        // Modal has internal state (_showLocked, _filterText) that persists
        // No initialization needed from context
        System.Diagnostics.Debug.WriteLine("[AchievementsModal] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is MainViewModel viewModel)
        {
            // Delegate to existing modal
            _modal.Render(viewModel, ref isOpen);
        }
        else
        {
            // Invalid context - close modal
            System.Diagnostics.Debug.WriteLine("[AchievementsModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        // Modal's internal state persists between opens
        // This is intentional - filters and settings remain
        System.Diagnostics.Debug.WriteLine("[AchievementsModal] Closed");
    }

    public void OnObscured()
    {
        // Another modal opened on top
        System.Diagnostics.Debug.WriteLine("[AchievementsModal] Obscured");
    }

    public void OnRevealed()
    {
        // Back on top after obscuring modal closed
        System.Diagnostics.Debug.WriteLine("[AchievementsModal] Revealed");
    }
}
