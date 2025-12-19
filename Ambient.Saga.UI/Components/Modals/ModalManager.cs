using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Engine.Application.Queries.Saga;
using MediatR;
using Ambient.Saga.UI.Services;
using Ambient.Saga.UI.Components.Panels;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Manages which modal dialog is currently open
/// Similar to BattleUI's ModalManager pattern
/// </summary>
public class ModalManager
{
    // Modal instances
    private WorldSelectionScreen _worldSelectionScreen;
    private ArchetypeSelectionModal _archetypeSelectionModal = new();
    private AvatarInfoModal _avatarInfoModal = new();
    private CharactersModal _charactersModal = new();
    private AchievementsModal _achievementsModal = new();
    private WorldCatalogModal _worldCatalogModal = new();
    private MerchantTradeModal _merchantTradeModal = new();
    private QuestModal _questModal;
    private QuestLogModal _questLogModal = new();
    private QuestDetailModal _questDetailModal;
    private DialogueModal _dialogueModal = new();
    private LootModal _lootModal = new();
    private BattleModal _battleModal = new();
    private FactionReputationModal _factionReputationModal = new();
    private PauseMenuModal _pauseMenuModal = new();
    private ISettingsPanel _settingsPanel;

    // Modal stack for proper hierarchical handling
    private readonly ModalStack _modalStack = new();

    // Modal registry for extensible modal management
    private readonly ModalRegistry _modalRegistry;

    // Reference to ImGui archetype selector for callbacks
    private readonly ImGuiArchetypeSelector? _archetypeSelector;
    private readonly IMediator _mediator;

    // Event for quit request (so host application can handle it)
    public event Action? QuitRequested;

    /// <summary>
    /// Requests the application to quit.
    /// Called when the user needs to exit (e.g., cancels mandatory archetype selection).
    /// </summary>
    public void RequestQuit()
    {
        QuitRequested?.Invoke();
    }

    public ModalManager(ImGuiArchetypeSelector archetypeSelector, IMediator mediator, IWorldContentGenerator worldContentGenerator, ISettingsPanel? settingsPanel = null)
    {
        _archetypeSelector = archetypeSelector;
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _questModal = new QuestModal(_mediator);
        _questDetailModal = new QuestDetailModal(_mediator);
        _worldSelectionScreen = new WorldSelectionScreen(worldContentGenerator);
        _settingsPanel = settingsPanel ?? new DefaultSettingsPanel();

        // Initialize modal registry
        _modalRegistry = new ModalRegistry(_modalStack);

        // Register modals with the registry (demonstration of migration pattern)
        RegisterModalAdapters();

        // Wire up pause menu events
        _pauseMenuModal.ResumeRequested += () => CloseModal("PauseMenu");
        _pauseMenuModal.SettingsRequested += OnSettingsRequested;
        _pauseMenuModal.QuitRequested += OnQuitRequested;
    }

    /// <summary>
    /// Register modal adapters with the modal registry.
    /// This demonstrates the migration pattern - gradually moving modals to the registry system.
    /// </summary>
    private void RegisterModalAdapters()
    {
        // Register Achievements modal (demonstration of adapter pattern)
        _modalRegistry.Register(new Adapters.AchievementsModalAdapter());

        // TODO: Register other modals as they are migrated
        // _modalRegistry.Register(new Adapters.WorldCatalogModalAdapter());
        // _modalRegistry.Register(new Adapters.FactionReputationModalAdapter());
    }

    private void OnSettingsRequested()
    {
        // Open settings panel
        OpenSettings();
        System.Diagnostics.Debug.WriteLine("Settings opened");
    }
    
    private void OnQuitRequested()
    {
        // Raise event for host application to handle
        System.Diagnostics.Debug.WriteLine("Quit requested");
        QuitRequested?.Invoke();
    }

    // Modal state - derived from stack (read-only)
    public bool ShowWorldSelection => _modalStack.Contains("WorldSelection");
    public bool ShowArchetypeSelection => _modalStack.Contains("ArchetypeSelection");
    public bool ShowAvatarInfo => _modalStack.Contains("AvatarInfo");
    public bool ShowCharacters => _modalStack.Contains("Characters");
    public bool ShowAchievements => _modalStack.Contains("Achievements");
    public bool ShowWorldCatalog => _modalStack.Contains("WorldCatalog");
    public bool ShowMerchantTrade => _modalStack.Contains("MerchantTrade");
    public bool ShowBossBattle => _modalStack.Contains("BossBattle");
    public bool ShowQuest => _modalStack.Contains("Quest");
    public bool ShowQuestLog => _modalStack.Contains("QuestLog");
    public bool ShowQuestDetail => _modalStack.Contains("QuestDetail");
    public bool ShowDialogue => _modalStack.Contains("Dialogue");
    public bool ShowLoot => _modalStack.Contains("Loot");
    public bool ShowFactionReputation => _modalStack.Contains("FactionReputation");
    public bool ShowPauseMenu => _modalStack.Contains("PauseMenu");
    public bool ShowSettings => _modalStack.Contains("Settings");

    // Selected character for interactions
    public CharacterViewModel? SelectedCharacter { get; set; }

    // Quest context (for quest signpost interactions)
    private string? _questRef;
    private string? _questSagaRef;
    private string? _questSignpostRef;
    private MainViewModel? _questViewModel;

    // Quest detail context
    private string? _questDetailRef;
    private string? _questDetailSagaRef;

    // Check if any modal is currently open
    public bool IsAnyModalOpen => _modalStack.HasModals;

    /// <summary>
    /// Check if any modal dialog is currently active (alias for IsAnyModalOpen).
    /// Used to suppress hotkey handling when modals are open.
    /// </summary>
    public bool HasActiveModal() => IsAnyModalOpen;

    // Modal manipulation methods - single source of truth
    public void OpenModal(string modalName)
    {
        if (!_modalStack.Contains(modalName))
        {
            _modalStack.Push(modalName);
        }
    }

    public void CloseModal(string modalName)
    {
        if (_modalStack.Contains(modalName))
        {
            _modalStack.Pop(modalName);
        }
    }

    // Modal registry methods for extensible modal management
    /// <summary>
    /// Register a modal with the modal registry system.
    /// This allows modals to be managed automatically with lifecycle hooks.
    /// </summary>
    public void RegisterModal(IModal modal)
    {
        _modalRegistry.Register(modal);
    }

    /// <summary>
    /// Open a registered modal with optional context.
    /// For non-registered modals, falls back to OpenModal().
    /// </summary>
    public void OpenRegisteredModal(string name, object? context = null)
    {
        _modalRegistry.Open(name, context);
    }

    public void OpenWorldSelection() => OpenModal("WorldSelection");
    public void OpenArchetypeSelection() => OpenModal("ArchetypeSelection");
    public void OpenAvatarInfo() => OpenModal("AvatarInfo");
    public void OpenCharacters() => OpenModal("Characters");
    public void OpenAchievements() => OpenModal("Achievements");
    public void OpenWorldCatalog() => OpenModal("WorldCatalog");
    public void OpenFactionReputation() => OpenModal("FactionReputation");
    public void OpenQuestLog() => OpenModal("QuestLog");
    public void OpenPauseMenu() => OpenModal("PauseMenu");
    public void OpenSettings() => OpenModal("Settings");

    public void Update(float deltaTime)
    {
        if (ShowBossBattle)
        {
            _battleModal.Update(deltaTime);
        }
    }

    public void Render(MainViewModel viewModel)
    {
        // Use local variables since properties can't be passed as ref
        // Render each modal if its flag is set

        // World selection (optional - typically used at startup or for "Load World" feature)
        if (ShowWorldSelection)
        {
            var isOpen = true;
            _worldSelectionScreen.Render(viewModel, ref isOpen);
            if (!isOpen) CloseModal("WorldSelection");
        }

        if (ShowArchetypeSelection)
        {
            var isOpen = true;
            _archetypeSelectionModal.Render(viewModel, _archetypeSelector, ref isOpen);
            if (!isOpen) CloseModal("ArchetypeSelection");
        }

        if (ShowAvatarInfo)
        {
            var isOpen = true;
            _avatarInfoModal.Render(viewModel, ref isOpen);
            if (!isOpen) CloseModal("AvatarInfo");
        }

        if (ShowCharacters)
        {
            var isOpen = true;
            _charactersModal.Render(viewModel, ref isOpen, this);
            if (!isOpen) CloseModal("Characters");
        }

        // NOTE: Achievements modal is now rendered via ModalRegistry (see RegisterModalAdapters)
        // This demonstrates the migration pattern - remove manual rendering once modal is registered
        //if (ShowAchievements)
        //{
        //    var isOpen = true;
        //    _achievementsModal.Render(viewModel, ref isOpen);
        //    if (!isOpen) CloseModal("Achievements");
        //}

        if (ShowWorldCatalog)
        {
            var isOpen = true;
            _worldCatalogModal.Render(viewModel, ref isOpen);
            if (!isOpen) CloseModal("WorldCatalog");
        }

        // Character interaction modals
        if (ShowMerchantTrade && SelectedCharacter != null)
        {
            var isOpen = true;
            _merchantTradeModal.Render(viewModel, SelectedCharacter, ref isOpen);
            if (!isOpen) CloseModal("MerchantTrade");
        }

        if (ShowBossBattle && SelectedCharacter != null)
        {
            var isOpen = true;
            _battleModal.Render(viewModel, SelectedCharacter, this, ref isOpen);
            if (!isOpen) CloseModal("BossBattle");
        }

        if (ShowQuest && _questViewModel != null)
        {
            var isOpen = true;
            _questModal.Render(_questViewModel, ref isOpen);
            if (!isOpen) CloseModal("Quest");
        }

        if (ShowQuestLog)
        {
            var isOpen = true;
            _questLogModal.Render(viewModel, this, ref isOpen);
            if (!isOpen) CloseModal("QuestLog");
        }

        if (ShowQuestDetail && _questDetailRef != null && _questDetailSagaRef != null)
        {
            var isOpen = true;
            _questDetailModal.Render(viewModel, ref isOpen);
            if (!isOpen) CloseModal("QuestDetail");
        }

        if (ShowDialogue && SelectedCharacter != null)
        {
            var isOpen = true;
            _dialogueModal.Render(viewModel, SelectedCharacter, this, ref isOpen);
            if (!isOpen) CloseModal("Dialogue");
        }

        if (ShowLoot && SelectedCharacter != null)
        {
            var isOpen = true;
            _lootModal.Render(viewModel, SelectedCharacter, ref isOpen);
            if (!isOpen) CloseModal("Loot");
        }

        if (ShowFactionReputation)
        {
            var isOpen = true;
            _factionReputationModal.Render(viewModel, ref isOpen);
            if (!isOpen) CloseModal("FactionReputation");
        }

        if (ShowPauseMenu)
        {
            var isOpen = true;
            _pauseMenuModal.Render(ref isOpen, _modalStack);
            if (!isOpen) CloseModal("PauseMenu");
        }

        if (ShowSettings)
        {
            var isOpen = true;
            _settingsPanel.Render(ref isOpen);
            if (!isOpen) CloseModal("Settings");
        }

        // Render any modals registered with the registry system
        // Pass viewModel as fallback context for modals opened via OpenModal() (legacy path)
        _modalRegistry.RenderRegistered(fallbackContext: viewModel);
    }

    public void OpenCharacterInteraction(CharacterViewModel character)
    {
        SelectedCharacter = character;

        // Open appropriate modal based on available interactions (determined by character traits and state)
        if (character.CanLoot)
        {
            // Defeated character - show loot
            OpenModal("Loot");
        }
        else if (character.CanDialogue)
        {
            // Living character with dialogue - start conversation
            OpenModal("Dialogue");
        }
        else if (character.CanAttack && character.IsAlive)
        {
            // Hostile character with no dialogue - go straight to battle
            OpenModal("BossBattle");
        }
        else if (character.CanTrade)
        {
            // Friendly character with no dialogue - go straight to trade
            OpenModal("MerchantTrade");
        }
        else
        {
            // No interactions available (shouldn't happen, but fallback to dialogue)
            OpenModal("Dialogue");
        }
    }


    public void OpenQuestSignpost(string questRef, string sagaRef, string signpostRef, MainViewModel viewModel)
    {
        _questRef = questRef;
        _questSagaRef = sagaRef;
        _questSignpostRef = signpostRef;
        _questViewModel = viewModel;

        _questModal.Open(questRef, sagaRef, signpostRef, viewModel);
        OpenModal("Quest");
    }

    public void OpenQuestDetail(string questRef)
    {
        if (_questViewModel?.PlayerAvatar == null) return;

        _ = OpenQuestDetailAsync(questRef);
    }

    private async Task OpenQuestDetailAsync(string questRef)
    {
        try
        {
            // Find saga containing this quest using Application layer query
            var sagaRef = await _mediator.Send(new GetSagaForQuestQuery
            {
                AvatarId = _questViewModel!.PlayerAvatar!.Id,
                QuestRef = questRef
            });

            if (sagaRef == null) return;

            _questDetailRef = questRef;
            _questDetailSagaRef = sagaRef;

            await _questDetailModal.OpenAsync(questRef, sagaRef, _questViewModel);
            OpenModal("QuestDetail");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening quest detail: {ex.Message}");
        }
    }

    public void CloseAll()
    {
        SelectedCharacter = null;
        _questViewModel = null;

        // Clear the modal stack - this is the single source of truth
        _modalStack.Clear();
    }
}
