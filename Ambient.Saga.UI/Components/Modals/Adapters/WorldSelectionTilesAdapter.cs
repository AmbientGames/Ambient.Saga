using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for WorldSelectionTiles to work with the Modal Registry Pattern.
/// This is the user-friendly tile-based version (vs WorldSelectionScreen for developers).
/// </summary>
public class WorldSelectionTilesAdapter : IModal
{
    private readonly WorldSelectionTiles _modal = new();

    public string Name => "WorldSelectionTiles";

    public bool CanOpen(object? context)
    {
        return context is MainViewModel;
    }

    public void OnOpening(object? context)
    {
        System.Diagnostics.Debug.WriteLine("[WorldSelectionTiles] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is MainViewModel viewModel)
        {
            _modal.Render(viewModel, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[WorldSelectionTiles] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[WorldSelectionTiles] Closed");
    }
}
