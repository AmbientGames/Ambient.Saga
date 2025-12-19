using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for BattleModal to work with the Modal Registry Pattern.
/// </summary>
public class BattleModalAdapter : IModal
{
    private readonly BattleModal _modal = new();
    private readonly ModalManager _modalManager;

    public BattleModalAdapter(ModalManager modalManager)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
    }

    public string Name => "BossBattle";

    public bool CanOpen(object? context)
    {
        return context is CharacterModalContext or CharacterContext;
    }

    public void OnOpening(object? context)
    {
        if (context is CharacterModalContext ctx)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Opening battle with: {ctx.Character.DisplayName}");
        }
        else if (context is CharacterContext ctx2)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Opening battle with: {ctx2.Character.DisplayName}");
        }
    }

    public void Render(object? context, ref bool isOpen)
    {
        CharacterViewModel? character = null;
        MainViewModel? viewModel = null;

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
            _modal.Render(viewModel, character, _modalManager, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[BattleModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[BattleModal] Closed");
    }
}
