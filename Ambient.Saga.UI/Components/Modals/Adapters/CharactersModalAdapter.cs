using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for CharactersModal to work with the Modal Registry Pattern.
/// </summary>
public class CharactersModalAdapter : IModal
{
    private readonly CharactersModal _modal = new();
    private readonly ModalManager _modalManager;

    public CharactersModalAdapter(ModalManager modalManager)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
    }

    public string Name => "Characters";

    public bool CanOpen(object? context)
    {
        return context is MainViewModel;
    }

    public void OnOpening(object? context)
    {
        System.Diagnostics.Debug.WriteLine("[CharactersModal] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is MainViewModel viewModel)
        {
            _modal.Render(viewModel, ref isOpen, _modalManager);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[CharactersModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[CharactersModal] Closed");
    }
}
