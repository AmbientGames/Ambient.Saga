using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Services;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for ArchetypeSelectionModal to work with the Modal Registry Pattern.
/// </summary>
public class ArchetypeSelectionModalAdapter : IModal
{
    private readonly ArchetypeSelectionModal _modal = new();
    private readonly ImGuiArchetypeSelector? _archetypeSelector;

    public ArchetypeSelectionModalAdapter(ImGuiArchetypeSelector? archetypeSelector)
    {
        _archetypeSelector = archetypeSelector;
    }

    public string Name => "ArchetypeSelection";

    public bool CanOpen(object? context)
    {
        return context is MainViewModel;
    }

    public void OnOpening(object? context)
    {
        System.Diagnostics.Debug.WriteLine("[ArchetypeSelectionModal] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is MainViewModel viewModel)
        {
            _modal.Render(viewModel, _archetypeSelector, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[ArchetypeSelectionModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[ArchetypeSelectionModal] Closed");
    }
}
