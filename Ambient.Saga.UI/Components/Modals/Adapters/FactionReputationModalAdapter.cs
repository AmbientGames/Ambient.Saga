using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for FactionReputationModal to work with the Modal Registry Pattern.
/// </summary>
public class FactionReputationModalAdapter : IModal
{
    private readonly FactionReputationModal _modal = new();

    public string Name => "FactionReputation";

    public bool CanOpen(object? context)
    {
        return context is MainViewModel;
    }

    public void OnOpening(object? context)
    {
        System.Diagnostics.Debug.WriteLine("[FactionReputationModal] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is MainViewModel viewModel)
        {
            _modal.Render(viewModel, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[FactionReputationModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[FactionReputationModal] Closed");
    }
}
