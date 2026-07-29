using Ambient.Application.Contracts;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Presentation.UI.ViewModels;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using MediatR;
using Ambient.Rpg.Ui.Components.Panels;
using Ambient.Rpg.Ui.Services;
using Microsoft.Extensions.Logging;

namespace Ambient.Rpg.Ui.Components.Modals;

/// <summary>
/// Manages which modal dialog is currently open
/// Similar to BattleUI's ModalManager pattern
/// </summary>
public class ModalManager
{
    // Modal instances
    private PauseMenuModal _pauseMenuModal = new();
    private ISettingsPanel _settingsPanel;

    // The registered battle adapter — kept so Update() ticks the SAME BattleModal
    // instance the registry renders (reaction countdown, enemy-turn delay clearing,
    // and pending battle dialogue effects all live on that instance).
    private Adapters.BattleModalAdapter _battleModalAdapter = null!;

    // Modal stack for proper hierarchical handling
    private readonly ModalStack _modalStack = new();

    // Modal registry for extensible modal management
    private readonly ModalRegistry _modalRegistry;

    // Reference to ImGui archetype selector for callbacks
    private readonly ImGuiArchetypeSelector? _archetypeSelector;
    private readonly IMediator _mediator;
    private readonly IWorldContentGenerator _worldContentGenerator;
    private readonly IGameSettings _gameSettings;
    private readonly ILoggerFactory? _loggerFactory;

    // Event for quit request (so host application can handle it)
    public event Action? QuitRequested;

    /// <summary>
    /// Fired when the avatar is defeated in an arc battle.
    /// Games subscribe to handle defeat differently from environmental death.
    /// </summary>
    public event Action? BattleDefeatRequested;

    /// <summary>
    /// Requests the application to quit.
    /// Called when the user needs to exit (e.g., cancels mandatory archetype selection).
    /// </summary>
    public void RequestQuit()
    {
        QuitRequested?.Invoke();
    }

    public ModalManager(
        ImGuiArchetypeSelector archetypeSelector,
        IMediator mediator,
        IWorldContentGenerator worldContentGenerator,
        IGameSettings gameSettings,
        ISettingsPanel? settingsPanel,
        ILoggerFactory? loggerFactory = null)
    {
        _archetypeSelector = archetypeSelector;
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _worldContentGenerator = worldContentGenerator ?? throw new ArgumentNullException(nameof(worldContentGenerator));
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _loggerFactory = loggerFactory;
        _settingsPanel = settingsPanel ?? new DefaultSettingsPanel();

        // Initialize modal registry
        _modalRegistry = new ModalRegistry(_modalStack);

        // Register modals with the registry
        RegisterModalAdapters();

        // Wire up pause menu events
        _pauseMenuModal.ResumeRequested += () => CloseModal("PauseMenu");
        _pauseMenuModal.SettingsRequested += OnSettingsRequested;
        _pauseMenuModal.QuitRequested += OnQuitRequested;
    }

    /// <summary>
    /// Register modal adapters with the modal registry.
    /// All modals are now managed through the registry pattern.
    /// </summary>
    private void RegisterModalAdapters()
    {
        // Character context modals
        // Note: the legacy Loot modal was removed — looting flows through the
        // MerchantTrade family (GeoCache, RemnantLoot, Market, Trade).
        _modalRegistry.Register(new Adapters.MerchantTradeModalAdapter());

        // Complex modals (need ModalManager reference)
        _modalRegistry.Register(new Adapters.DialogueModalAdapter(this));
        _battleModalAdapter = new Adapters.BattleModalAdapter(this);
        _battleModalAdapter.AvatarDefeated += () => BattleDefeatRequested?.Invoke();
        // Victory loot: when a battle ends in victory and its modal closes, offer the
        // defeated character's remaining Loot as a free-take (works in EVERY host that
        // renders through this ModalManager — 3D host games and the sandbox alike).
        _battleModalAdapter.VictoryLootAvailable += (viewModel, character) =>
            _ = QueueVictoryLootAsync(viewModel, character);
        _modalRegistry.Register(_battleModalAdapter);

        // Quest-log detail modal (needs IMediator). The signpost offer modal
        // ("Quest"/QuestModalAdapter) was removed 2026-07-04 — quests are
        // offered via dialogue.
        _modalRegistry.Register(new Adapters.QuestDetailModalAdapter(_mediator));

        // Note: Journal is now a panel (not a modal), accessed via J key

        // Special modals
        var worldSelectionLogger = _loggerFactory?.CreateLogger<WorldSelectionScreen>();
        _modalRegistry.Register(new Adapters.WorldSelectionScreenAdapter(_worldContentGenerator, _gameSettings, worldSelectionLogger));
        _modalRegistry.Register(new Adapters.ArchetypeSelectionModalAdapter(_archetypeSelector));

        // Note: PauseMenu and Settings are not migrated as they have special rendering requirements
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
    public bool ShowMerchantTrade => _modalStack.Contains("MerchantTrade");
    public bool ShowBossBattle => _modalStack.Contains("BossBattle");
    public bool ShowQuestDetail => _modalStack.Contains("QuestDetail");
    public bool ShowDialogue => _modalStack.Contains("Dialogue");
    public bool ShowPauseMenu => _modalStack.Contains("PauseMenu");
    public bool ShowSettings => _modalStack.Contains("Settings");
    public bool ShowJournal => _modalStack.Contains("Journal");

    // Selected character for interactions
    public CharacterViewModel? SelectedCharacter { get; set; }

    /// <summary>
    /// Battle-affecting dialogue events (HealSelf, ChangeStance, ChangeAffinity,
    /// EndBattle) deposited by the DialogueModal during mid-battle conversations.
    /// The BattleModal drains this each frame and applies them to the running battle.
    /// </summary>
    public List<Ambient.Rpg.Engine.Domain.Dialogue.Events.DialogueSystemEvent>? PendingBattleDialogueEffects { get; set; }

    /// <summary>
    /// One-shot dialogue start override set by battle dialogue triggers: the boss's
    /// battle tree and the authored entry node (battle_enraged, ...). Consumed by
    /// the DialogueModal on its next initialization.
    /// </summary>
    public (string TreeRef, string? NodeId)? DialogueStartOverride { get; set; }

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
    public void OpenPauseMenu() => OpenModal("PauseMenu");
    public void OpenSettings() => OpenModal("Settings");
    // Note: Journal panel (J key) now consolidates Quests and Characters info

    public void Update(float deltaTime)
    {
        if (ShowBossBattle)
        {
            _battleModalAdapter.Update(deltaTime);
        }

        // Open the queued battle-remains modal once no other modal is in the way —
        // same deferred-never-dropped contract as TryOpenAssault (the request stays
        // queued while dialogue/trade/pause modals are open, then fires).
        if (_pendingBattleRemainsOpen is { } openRemains && !HasActiveModal())
        {
            _pendingBattleRemainsOpen = null;
            openRemains();
        }
    }

    /// <summary>
    /// Host hook for battle drops: given the defeated character and its remaining loot,
    /// creates the victor-only BattleLoot remains arc on the server and returns an action
    /// that opens its trade modal. Returns null when the remains cannot be created (no
    /// server session) — the loot is lost, exactly like a death with no server. Wired by
    /// the game host (GameForm); the sandbox leaves it null.
    /// </summary>
    public Func<CharacterViewModel, Ambient.Domain.ItemCollection, Task<Action?>>? CreateBattleRemains { get; set; }

    /// <summary>
    /// Pending open-the-remains action: set after a victorious battle's modal closes and
    /// the BattleLoot arc is created, consumed by Update() once the modal stack is free.
    /// </summary>
    private Action? _pendingBattleRemainsOpen;

    /// <summary>
    /// Validates a victory drop against the replayed arc state (server-of-record for
    /// the drop): the character must be dead, THIS avatar must be its victor, and its
    /// remaining Loot must be non-empty — an empty drop leaves nothing. On success the
    /// host creates a victor-only BattleLoot remains arc from the loot and its modal is
    /// queued; Update() opens it when no other modal is active (deferred, never dropped).
    /// </summary>
    private async Task QueueVictoryLootAsync(RpgMainViewModel viewModel, CharacterViewModel character)
    {
        try
        {
            if (viewModel.Avatar == null || CreateBattleRemains == null)
                return;

            var arcState = await _mediator.Send(new GetArcStateQuery
            {
                AvatarId = viewModel.Avatar.AvatarId,
                ArcRef = character.ArcRef,
            });

            if (arcState == null ||
                !arcState.Characters.TryGetValue(character.CharacterInstanceId.ToString(), out var characterState))
                return;

            if (characterState.IsAlive)
                return;

            var avatarId = viewModel.Avatar.AvatarId.ToString();
            if (string.IsNullOrEmpty(characterState.DefeatedByAvatarId) ||
                !string.Equals(characterState.DefeatedByAvatarId, avatarId, StringComparison.OrdinalIgnoreCase))
                return;

            // Empty Loot → nothing drops
            if (!(characterState.CurrentInventory?.HasAnyItems() ?? false))
                return;

            var openRemains = await CreateBattleRemains(character, characterState.CurrentInventory);
            if (openRemains == null)
            {
                System.Diagnostics.Debug.WriteLine("[ModalManager] Battle remains not created — loot lost (no server session?)");
                return;
            }

            _pendingBattleRemainsOpen = openRemains;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModalManager] Victory loot check failed: {ex.Message}");
        }
    }

    public void Render(RpgMainViewModel viewModel)
    {
        // ====================================================================
        // ALL MODALS NOW RENDERED VIA MODAL REGISTRY (see RegisterModalAdapters)
        // ====================================================================
        // The following modals have been migrated to the registry pattern:
        // - WorldSelection, ArchetypeSelection, Characters
        // - Achievements, WorldCatalog, MerchantTrade, BossBattle
        // - QuestDetail, Dialogue
        //
        // Only PauseMenu and Settings remain with manual rendering due to
        // special requirements (PauseMenu uses ModalStack directly, Settings
        // uses ISettingsPanel interface).
        // ====================================================================

        // PauseMenu - special rendering (passes ModalStack directly)
        if (ShowPauseMenu)
        {
            var isOpen = true;
            _pauseMenuModal.Render(ref isOpen, _modalStack);
            if (!isOpen) CloseModal("PauseMenu");
        }

        // Settings - special rendering (uses ISettingsPanel interface)
        if (ShowSettings)
        {
            var isOpen = true;
            _settingsPanel.Render(ref isOpen);
            if (!isOpen) CloseModal("Settings");
        }

        // Render all registered modals automatically
        // Pass viewModel as fallback context for modals opened via OpenModal() (legacy path)
        _modalRegistry.RenderRegistered(fallbackContext: viewModel);
    }

    public void OpenCharacterInteraction(CharacterViewModel character, RpgMainViewModel viewModel)
    {
        SelectedCharacter = character;

        // Create character context for registry-based modals
        var context = new CharacterContext(viewModel, character);

        // Open appropriate modal based on available interactions (determined by character
        // traits and state). There is no loot-the-corpse path: a battle drop is a
        // victor-only BattleLoot remains ARC, opened via its own trace/marker.
        if (character.CanDialogue)
        {
            // Character with dialogue - start conversation
            OpenRegisteredModal("Dialogue", context);
        }
        else if (character.CanAttack && character.IsAlive)
        {
            // Hostile character with no dialogue - go straight to battle
            OpenRegisteredModal("BossBattle", context);
        }
        else if (character.CanTrade)
        {
            // Friendly character with no dialogue - go straight to trade
            OpenRegisteredModal("MerchantTrade", context);
        }
        else
        {
            // No interactions available (shouldn't happen, but fallback to dialogue)
            OpenRegisteredModal("Dialogue", context);
        }
    }


    /// <summary>
    /// Proximity assault: a hostile character initiated battle
    /// (RpgMainViewModel.AssaultRequested). Opens the battle modal exactly like
    /// clicking Attack on the character (OpenCharacterInteraction's battle branch) —
    /// straight into battle, no pre-fight conversation; menace speech is the
    /// character's battle_opening dialogue trigger. Returns false without opening
    /// when any modal is already active (dialogue, trade, pause, a running battle):
    /// the assault is deferred, not dropped — the view model re-raises on its next
    /// ~1 s interaction check while the avatar remains in range.
    /// </summary>
    public bool TryOpenAssault(CharacterViewModel character, RpgMainViewModel viewModel)
    {
        if (HasActiveModal())
            return false;

        SelectedCharacter = character;
        OpenRegisteredModal("BossBattle", new CharacterContext(viewModel, character));
        return true;
    }

    public void OpenQuestDetail(string questRef, RpgMainViewModel viewModel)
    {
        if (viewModel.Avatar == null) return;

        _ = OpenQuestDetailAsync(questRef, viewModel);
    }

    private async Task OpenQuestDetailAsync(string questRef, RpgMainViewModel viewModel)
    {
        try
        {
            // Find arc containing this quest using Application layer query
            var arcRef = await _mediator.Send(new GetArcForQuestQuery
            {
                AvatarId = viewModel.Avatar!.AvatarId,
                QuestRef = questRef
            });

            if (arcRef == null) return;

            // Create quest detail context and open via registry
            var context = new QuestDetailContext(questRef, arcRef, viewModel);
            OpenRegisteredModal("QuestDetail", context);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening quest detail: {ex.Message}");
        }
    }

    public void CloseAll()
    {
        SelectedCharacter = null;

        // Clear the modal stack - this is the single source of truth
        _modalStack.Clear();
    }
}
