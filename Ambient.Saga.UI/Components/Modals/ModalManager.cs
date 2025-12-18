using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Engine.Application.Queries.Saga;
using MediatR;
using Ambient.Saga.UI.Services;

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

    // Reference to ImGui archetype selector for callbacks
    private readonly ImGuiArchetypeSelector? _archetypeSelector;
    private readonly IMediator _mediator;

    // Event for quit request (so host application can handle it)
    public event Action? QuitRequested;

    public ModalManager(ImGuiArchetypeSelector archetypeSelector, IMediator mediator, IWorldContentGenerator worldContentGenerator)
    {
        _archetypeSelector = archetypeSelector;
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _questModal = new QuestModal(_mediator);
        _questDetailModal = new QuestDetailModal(_mediator);
        _worldSelectionScreen = new WorldSelectionScreen(worldContentGenerator);
        
        // Wire up pause menu events
        _pauseMenuModal.ResumeRequested += () => ShowPauseMenu = false;
        _pauseMenuModal.SettingsRequested += OnSettingsRequested;
        _pauseMenuModal.QuitRequested += OnQuitRequested;
    }
    
    private void OnSettingsRequested()
    {
        // TODO: Open settings modal
        System.Diagnostics.Debug.WriteLine("Settings requested (not implemented yet)");
    }
    
    private void OnQuitRequested()
    {
        // Raise event for host application to handle
        System.Diagnostics.Debug.WriteLine("Quit requested");
        QuitRequested?.Invoke();
    }

    // Modal state flags
    public bool ShowWorldSelection { get; set; }
    public bool ShowArchetypeSelection { get; set; }
    public bool ShowAvatarInfo { get; set; }
    public bool ShowCharacters { get; set; }
    public bool ShowAchievements { get; set; }
    public bool ShowWorldCatalog { get; set; }
    public bool ShowMerchantTrade { get; set; }
    public bool ShowBossBattle { get; set; }
    public bool ShowQuest { get; set; }
    public bool ShowQuestLog { get; set; }
    public bool ShowQuestDetail { get; set; }
    public bool ShowDialogue { get; set; }
    public bool ShowLoot { get; set; }
    public bool ShowFactionReputation { get; set; }
    public bool ShowPauseMenu { get; set; }

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
    public bool IsAnyModalOpen =>
        ShowWorldSelection ||
        ShowArchetypeSelection ||
        ShowAvatarInfo ||
        ShowCharacters ||
        ShowAchievements ||
        ShowWorldCatalog ||
        ShowMerchantTrade ||
        ShowBossBattle ||
        ShowQuest ||
        ShowQuestLog ||
        ShowQuestDetail ||
        ShowDialogue ||
        ShowLoot ||
        ShowFactionReputation ||
        ShowPauseMenu;

    /// <summary>
    /// Check if any modal dialog is currently active (alias for IsAnyModalOpen).
    /// Used to suppress hotkey handling when modals are open.
    /// </summary>
    public bool HasActiveModal() => IsAnyModalOpen;

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
            var isOpen = ShowWorldSelection;
            _worldSelectionScreen.Render(viewModel, ref isOpen);
            ShowWorldSelection = isOpen;
        }

        if (ShowArchetypeSelection)
        {
            var isOpen = ShowArchetypeSelection;
            _archetypeSelectionModal.Render(viewModel, _archetypeSelector, ref isOpen);
            ShowArchetypeSelection = isOpen;
        }

        if (ShowAvatarInfo)
        {
            var isOpen = ShowAvatarInfo;
            _avatarInfoModal.Render(viewModel, ref isOpen);
            ShowAvatarInfo = isOpen;
        }

        if (ShowCharacters)
        {
            var isOpen = ShowCharacters;
            _charactersModal.Render(viewModel, ref isOpen, this);
            ShowCharacters = isOpen;
        }

        if (ShowAchievements)
        {
            var isOpen = ShowAchievements;
            _achievementsModal.Render(viewModel, ref isOpen);
            ShowAchievements = isOpen;
        }

        if (ShowWorldCatalog)
        {
            var isOpen = ShowWorldCatalog;
            _worldCatalogModal.Render(viewModel, ref isOpen);
            ShowWorldCatalog = isOpen;
        }

        // Character interaction modals
        if (ShowMerchantTrade && SelectedCharacter != null)
        {
            var isOpen = ShowMerchantTrade;
            _merchantTradeModal.Render(viewModel, SelectedCharacter, ref isOpen);
            ShowMerchantTrade = isOpen;
        }

        if (ShowBossBattle && SelectedCharacter != null)
        {
            // Render battle modal (it handles its own initialization)
            var isOpen = ShowBossBattle;
            _battleModal.Render(viewModel, SelectedCharacter, this, ref isOpen);
            ShowBossBattle = isOpen;
        }

        if (ShowQuest && _questViewModel != null)
        {
            var isOpen = ShowQuest;
            _questModal.Render(_questViewModel, ref isOpen);
            ShowQuest = isOpen;
        }

        if (ShowQuestLog)
        {
            var isOpen = ShowQuestLog;
            _questLogModal.Render(viewModel, this, ref isOpen);
            ShowQuestLog = isOpen;
        }

        if (ShowQuestDetail && _questDetailRef != null && _questDetailSagaRef != null)
        {
            var isOpen = ShowQuestDetail;
            _questDetailModal.Render(viewModel, ref isOpen);
            ShowQuestDetail = isOpen;
        }

        if (ShowDialogue && SelectedCharacter != null)
        {
            var isOpen = ShowDialogue;
            _dialogueModal.Render(viewModel, SelectedCharacter, this, ref isOpen);
            ShowDialogue = isOpen;
        }

        if (ShowLoot && SelectedCharacter != null)
        {
            var isOpen = ShowLoot;
            _lootModal.Render(viewModel, SelectedCharacter, ref isOpen);
            ShowLoot = isOpen;
        }

        if (ShowFactionReputation)
        {
            var isOpen = ShowFactionReputation;
            _factionReputationModal.Render(viewModel, ref isOpen);
            ShowFactionReputation = isOpen;
        }
        
        if (ShowPauseMenu)
        {
            var isOpen = ShowPauseMenu;
            _pauseMenuModal.Render(ref isOpen);
            ShowPauseMenu = isOpen;
        }
    }

    public void OpenCharacterInteraction(CharacterViewModel character)
    {
        SelectedCharacter = character;

        // Open appropriate modal based on available interactions (determined by character traits and state)
        if (character.CanLoot)
        {
            // Defeated character - show loot
            ShowLoot = true;
        }
        else if (character.CanDialogue)
        {
            // Living character with dialogue - start conversation
            ShowDialogue = true;
        }
        else if (character.CanAttack && character.IsAlive)
        {
            // Hostile character with no dialogue - go straight to battle
            ShowBossBattle = true;
        }
        else if (character.CanTrade)
        {
            // Friendly character with no dialogue - go straight to trade
            ShowMerchantTrade = true;
        }
        else
        {
            // No interactions available (shouldn't happen, but fallback to dialogue)
            ShowDialogue = true;
        }
    }


    public void OpenQuestSignpost(string questRef, string sagaRef, string signpostRef, MainViewModel viewModel)
    {
        _questRef = questRef;
        _questSagaRef = sagaRef;
        _questSignpostRef = signpostRef;
        _questViewModel = viewModel;

        _questModal.Open(questRef, sagaRef, signpostRef, viewModel);
        ShowQuest = true;
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
            ShowQuestDetail = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening quest detail: {ex.Message}");
        }
    }

    public void CloseAll()
    {
        ShowWorldSelection = false;
        ShowArchetypeSelection = false;
        ShowAvatarInfo = false;
        ShowCharacters = false;
        ShowAchievements = false;
        ShowWorldCatalog = false;
        ShowMerchantTrade = false;
        ShowBossBattle = false;
        ShowQuest = false;
        ShowQuestLog = false;
        ShowQuestDetail = false;
        ShowDialogue = false;
        ShowLoot = false;
        ShowFactionReputation = false;
        ShowPauseMenu = false;
        SelectedCharacter = null;
        _questViewModel = null;
    }
}
