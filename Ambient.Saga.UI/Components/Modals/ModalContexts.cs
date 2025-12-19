using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Context classes for modal operations.
/// These provide strongly-typed context objects for the modal registry pattern.
/// </summary>

/// <summary>
/// Context for modals that need MainViewModel and CharacterViewModel.
/// </summary>
public record CharacterContext(MainViewModel ViewModel, CharacterViewModel Character);

/// <summary>
/// Context for modals that need MainViewModel, CharacterViewModel, and ModalManager.
/// Used for modals that can transition to other modals.
/// </summary>
public record CharacterModalContext(MainViewModel ViewModel, CharacterViewModel Character, ModalManager ModalManager);

/// <summary>
/// Context for quest-related modals.
/// </summary>
public record QuestContext(string QuestRef, string SagaRef, string SignpostRef, MainViewModel ViewModel);

/// <summary>
/// Context for quest detail modal.
/// </summary>
public record QuestDetailContext(string QuestRef, string SagaRef, MainViewModel ViewModel);
