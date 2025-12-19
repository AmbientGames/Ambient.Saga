using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for WorldCatalogModal to work with the Modal Registry Pattern.
/// </summary>
public class WorldCatalogModalAdapter : IModal
{
    private readonly WorldCatalogModal _modal = new();

    public string Name => "WorldCatalog";

    public bool CanOpen(object? context)
    {
        return context is MainViewModel;
    }

    public void OnOpening(object? context)
    {
        System.Diagnostics.Debug.WriteLine("[WorldCatalogModal] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is MainViewModel viewModel)
        {
            _modal.Render(viewModel, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[WorldCatalogModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[WorldCatalogModal] Closed");
    }
}
