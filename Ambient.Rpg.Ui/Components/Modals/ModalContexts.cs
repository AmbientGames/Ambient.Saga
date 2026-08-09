using Ambient.Rpg.Presentation.UI.ViewModels;

namespace Ambient.Rpg.Ui.Components.Modals;

/// <summary>
/// Context classes for modal operations.
/// These provide strongly-typed context objects for the modal registry pattern.
/// </summary>

/// <summary>
/// Context for modals that need MainViewModel and CharacterViewModel.
/// </summary>
public record CharacterContext(RpgMainViewModel ViewModel, CharacterViewModel Character);

/// <summary>
/// Context for modals that need MainViewModel, CharacterViewModel, and ModalManager.
/// Used for modals that can transition to other modals.
/// </summary>
public record CharacterModalContext(RpgMainViewModel ViewModel, CharacterViewModel Character, ModalManager ModalManager);

/// <summary>
/// Context for quest detail modal.
/// </summary>
public record QuestDetailContext(string QuestRef, string ArcRef, RpgMainViewModel ViewModel);
