using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for DialogueModal to work with the Modal Registry Pattern.
/// </summary>
public class DialogueModalAdapter : IModal
{
    private readonly DialogueModal _modal = new();
    private readonly ModalManager _modalManager;
    private SagaMainViewModel? _lastViewModel;

    public DialogueModalAdapter(ModalManager modalManager)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
    }

    public string Name => "Dialogue";

    public bool CanOpen(object? context)
    {
        return context is CharacterModalContext or CharacterContext;
    }

    public void OnOpening(object? context)
    {
        if (context is CharacterModalContext ctx)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Opening for character: {ctx.Character.DisplayName}");
        }
        else if (context is CharacterContext ctx2)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Opening for character: {ctx2.Character.DisplayName}");
        }
    }

    public void Render(object? context, ref bool isOpen)
    {
        CharacterViewModel? character = null;
        SagaMainViewModel? viewModel = null;

        if (context is CharacterModalContext ctx)
        {
            character = ctx.Character;
            viewModel = ctx.ViewModel;
        }
        else if (context is CharacterContext ctx2)
        {
            character = ctx2.Character;
            viewModel = ctx2.ViewModel;
        }

        if (viewModel != null && character != null)
        {
            _lastViewModel = viewModel;
            _modal.Render(viewModel, character, _modalManager, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[DialogueModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[DialogueModal] Closed");

        // Reset modal state so reopening doesn't show the previous conversation's
        // stale tree (ModalRegistry never renders with isOpen=false, so the modal's
        // own reset-on-close path never ran).
        _modal.Reset();

        // Seal the dialogue session so the player can re-open it later. Fire-and-forget:
        // clearing client state happens synchronously inside CloseCurrentDialogueAsync so
        // repeat interactions aren't blocked even if the server round-trip is in flight.
        if (_lastViewModel != null)
        {
            _ = _lastViewModel.CloseCurrentDialogueAsync();
            _lastViewModel = null;
        }
    }
}
