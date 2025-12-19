using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Examples;

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
    public record SimpleContext(MainViewModel ViewModel);

    /// <summary>
    /// Context for modals that need MainViewModel and CharacterViewModel
    /// </summary>
    public record CharacterContext(MainViewModel ViewModel, CharacterViewModel Character);

    /// <summary>
    /// Context for modals that need MainViewModel, CharacterViewModel, and ModalManager
    /// </summary>
    public record FullContext(MainViewModel ViewModel, CharacterViewModel Character, ModalManager ModalManager);

    /// <summary>
    /// Example: Adapter for a simple modal that only needs MainViewModel
    /// </summary>
    public class AvatarInfoModalAdapter : IModal
    {
        private readonly AvatarInfoModal _modal = new();

        public string Name => "AvatarInfo";

        public void Render(object? context, ref bool isOpen)
        {
            if (context is SimpleContext ctx)
            {
                _modal.Render(ctx.ViewModel, ref isOpen);
            }
            else if (context is MainViewModel viewModel)
            {
                // Also support direct MainViewModel for convenience
                _modal.Render(viewModel, ref isOpen);
            }
        }

        // Optional: Add lifecycle hooks for cleanup
        public void OnClosed()
        {
            // Any cleanup needed for AvatarInfoModal
        }
    }

    /// <summary>
    /// Example: Adapter for a complex modal that needs multiple parameters
    /// </summary>
    public class LootModalAdapter : IModal
    {
        private readonly LootModal _modal = new();

        public string Name => "Loot";

        public bool CanOpen(object? context)
        {
            // Validate that we have the required character context
            return context is CharacterContext { Character: { CanLoot: true } };
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
