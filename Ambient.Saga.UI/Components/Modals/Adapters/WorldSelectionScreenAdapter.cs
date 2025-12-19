using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for WorldSelectionScreen to work with the Modal Registry Pattern.
/// </summary>
public class WorldSelectionScreenAdapter : IModal
{
    private readonly WorldSelectionScreen _modal;

    public WorldSelectionScreenAdapter(IWorldContentGenerator worldContentGenerator)
    {
        _modal = new WorldSelectionScreen(worldContentGenerator ?? throw new ArgumentNullException(nameof(worldContentGenerator)));
    }

    public string Name => "WorldSelection";

    public bool CanOpen(object? context)
    {
        return context is MainViewModel;
    }

    public void OnOpening(object? context)
    {
        System.Diagnostics.Debug.WriteLine("[WorldSelectionScreen] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is MainViewModel viewModel)
        {
            _modal.Render(viewModel, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[WorldSelectionScreen] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[WorldSelectionScreen] Closed");
    }
}
