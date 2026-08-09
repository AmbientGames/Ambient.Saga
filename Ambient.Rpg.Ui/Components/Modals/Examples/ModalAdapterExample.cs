using Ambient.Rpg.Presentation.UI.ViewModels;

namespace Ambient.Rpg.Ui.Components.Modals.Examples;

/// <summary>
/// Example adapter that wraps an existing modal to work with the registry pattern.
/// This pattern allows gradual migration of existing modals to the new system.
/// </summary>
/// <remarks>
/// Usage Pattern:
///
/// // For modals with specialized context (like CharacterViewModel)
/// public class DialogueModalAdapter : IModal
/// {
///     private readonly DialogueModal _dialogueModal = new();
///     public string Name => "Dialogue";
///
///     public void Render(object? context, ref bool isOpen)
///     {
///         if (context is DialogueContext ctx)
///         {
///             _dialogueModal.Render(ctx.ViewModel, ctx.Character, ctx.ModalManager, ref isOpen);
///         }
///     }
/// }
///
/// // Context class to pass multiple parameters
/// public record DialogueContext(MainViewModel ViewModel, CharacterViewModel Character, ModalManager ModalManager);
///
/// // Then use it:
/// modalManager.RegisterModal(new DialogueModalAdapter());
/// modalManager.OpenRegisteredModal("Dialogue", new DialogueContext(viewModel, character, modalManager));
/// </remarks>
public static class ModalAdapterExample
{
    // Example context classes for different modal types

    /// <summary>
    /// Context for modals that need MainViewModel only
    /// </summary>
    public record SimpleContext(RpgMainViewModel ViewModel);

    /// <summary>
    /// Context for modals that need MainViewModel and CharacterViewModel
    /// </summary>
    public record CharacterContext(RpgMainViewModel ViewModel, CharacterViewModel Character);

    /// <summary>
    /// Context for modals that need MainViewModel, CharacterViewModel, and ModalManager
    /// </summary>
    public record FullContext(RpgMainViewModel ViewModel, CharacterViewModel Character, ModalManager ModalManager);

    /// <summary>
    /// Example: Adapter for a modal that needs CharacterViewModel context
    /// </summary>
    public class MerchantTradeModalAdapterExample : IModal
    {
        private readonly MerchantTradeModal _modal = new();

        public string Name => "MerchantTradeExample";

        public bool CanOpen(object? context)
        {
            // Validate that we have the required character context
            return context is CharacterContext { Character.CanTrade: true };
        }

        public void Render(object? context, ref bool isOpen)
        {
            if (context is CharacterContext ctx)
            {
                _modal.Render(ctx.ViewModel, ctx.Character, ref isOpen);
            }
        }
    }
}
