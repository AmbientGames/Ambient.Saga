using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Infrastructure.GameLogic;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.Presentation.UI.Models;
using Ambient.Saga.Presentation.UI.Services;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.StoryGenerator;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Application.Queries.Loading;
using Ambient.SagaEngine.Application.Queries.Saga;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Contracts;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using Ambient.SagaEngine.Domain.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.ObjectModel;

namespace Ambient.Saga.Presentation.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly string _dataDirectory;
    private readonly string _schemaDirectory;

    [ObservableProperty]
    private ObservableCollection<WorldConfiguration> _availableConfigurations = new();

    [ObservableProperty]
    private WorldConfiguration? _selectedConfiguration;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private HeightMapImageData? _heightMapImage;

    [ObservableProperty]
    private string? _heightMapInfo;

    [ObservableProperty]
    private double zoomFactor = 1.0;

    [ObservableProperty]
    private ObservableCollection<SagaViewModel> _sagas = new();

    [ObservableProperty]
    private ObservableCollection<ProximityTriggerViewModel> _allTriggers = new();

    [ObservableProperty]
    private ObservableCollection<CharacterViewModel> _characters = new();

    [ObservableProperty]
    private double _avatarPixelX;

    [ObservableProperty]
    private double _avatarPixelY;

    [ObservableProperty]
    private double _avatarLatitude;

    [ObservableProperty]
    private double _avatarLongitude;

    [ObservableProperty]
    private int _avatarElevation;

    [ObservableProperty]
    private bool _hasAvatarPosition;

    [ObservableProperty]
    private bool _shouldCenterOnAvatar;

    [ObservableProperty]
    private double _mousePixelX;

    [ObservableProperty]
    private double _mousePixelY;

    [ObservableProperty]
    private double _mouseLatitude;

    [ObservableProperty]
    private double _mouseLongitude;

    [ObservableProperty]
    private int _mouseElevation;

    [ObservableProperty]
    private bool _hasMousePosition;

    [ObservableProperty]
    private double _minimumZoom = 0.1;

    [ObservableProperty]
    private double _viewportWidth = 800;

    [ObservableProperty]
    private double _viewportHeight = 600;

    [ObservableProperty]
    private AvatarEntity? _playerAvatar;

    [ObservableProperty]
    private ObservableCollection<AvatarArchetype> _availableArchetypes = new();

    [ObservableProperty]
    private World? _currentWorld;

    [ObservableProperty]
    private ProximityTriggerViewModel? _selectedTrigger;

    [ObservableProperty]
    private Character? _triggeredCharacter;

    [ObservableProperty]
    private Guid _triggeredCharacterInstanceId;

    [ObservableProperty]
    private ObservableCollection<string> _activityLog = new();

    [ObservableProperty]
    private bool _isSagaInteractionActive = false;

    public MerchantTradeViewModel? MerchantTrade { get; private set; }
    public AvatarInfoViewModel AvatarInfo { get; }
    public QuestLogViewModel? QuestLog { get; private set; }
    public AchievementViewModel? Achievements { get; private set; }

    // Context that SagaInteractionViewModel references
    private SagaInteractionContext _sagaInteractionContext = new()
    {
        World = null!,
        AvatarEntity = null!,
        ActiveCharacter = null
    };

    // Event for when character changes so dialogue can be loaded
    public event EventHandler? CharacterChanged;

    private ProximityTriggerViewModel? _previousTrigger;
    private IDisposable? _worldDatabase;
    private IWorldStateRepository? _worldRepository;
    private ISteamAchievementService? _steamAchievementService;
    private HeightMapProcessor.ProcessedHeightMap? _processedHeightMap;

    // Track current entity being looted (for recording triggers)
    private string? _currentEntityRef;
    private FeatureType? _currentEntityType;

    // Track current saga/character context for CQRS commands
    private string? _currentSagaRef;

    // Track Sagas where Saga-level spawns have already occurred (to avoid duplicates)
    private readonly HashSet<string> _spawnedSagas = new();

    private const double MaxZoom = 40.0;
    private const double Step = 1.1;

    // CQRS providers and factory
    private readonly WorldProvider _worldProvider;
    private readonly SagaInstanceRepositoryProvider _repositoryProvider;
    private readonly GameAvatarRepositoryProvider _avatarRepositoryProvider;
    private readonly WorldStateRepositoryProvider _worldStateRepositoryProvider;
    private readonly IWorldRepositoryFactory _repositoryFactory;
    private readonly MediatR.IMediator _mediator;
    //private readonly Services.IArchetypeSelector _wpfArchetypeSelector;
    private readonly IArchetypeSelector _imguiArchetypeSelector;
    //private bool _useImGuiMode = false;

    // Public accessor for mediator (used by modals)
    public MediatR.IMediator Mediator => _mediator;

    public MainViewModel(
        WorldProvider worldProvider,
        SagaInstanceRepositoryProvider repositoryProvider,
        GameAvatarRepositoryProvider avatarRepositoryProvider,
        WorldStateRepositoryProvider worldStateRepositoryProvider,
        IWorldRepositoryFactory repositoryFactory,
        MediatR.IMediator mediator,
        [Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute("imgui")] IArchetypeSelector imguiArchetypeSelector)
    {
        _worldProvider = worldProvider ?? throw new ArgumentNullException(nameof(worldProvider));
        _repositoryProvider = repositoryProvider ?? throw new ArgumentNullException(nameof(repositoryProvider));
        _avatarRepositoryProvider = avatarRepositoryProvider ?? throw new ArgumentNullException(nameof(avatarRepositoryProvider));
        _worldStateRepositoryProvider = worldStateRepositoryProvider ?? throw new ArgumentNullException(nameof(worldStateRepositoryProvider));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        //_wpfArchetypeSelector = wpfArchetypeSelector ?? throw new ArgumentNullException(nameof(wpfArchetypeSelector));
        _imguiArchetypeSelector = imguiArchetypeSelector ?? throw new ArgumentNullException(nameof(imguiArchetypeSelector));

        // Set up directories similar to the unit tests
        var baseDirectory = AppContext.BaseDirectory;
        _dataDirectory = Path.Combine(baseDirectory, "WorldDefinitions");
        _schemaDirectory = Path.Combine(baseDirectory, "DefinitionXsd");

        // Initialize merchant trade view model with the context
        MerchantTrade = new MerchantTradeViewModel(_sagaInteractionContext, _mediator);

        // Subscribe to merchant trade events
        MerchantTrade.StatusMessageChanged += (sender, message) => StatusMessage = message;
        MerchantTrade.ActivityMessageGenerated += (sender, message) => ActivityLog.Insert(0, message);

        // Initialize avatar info view model
        AvatarInfo = new AvatarInfoViewModel();

        // Initialize quest and achievement view models (will be populated when world loads)
        QuestLog = new QuestLogViewModel(_sagaInteractionContext, _mediator);
        Achievements = new AchievementViewModel(_sagaInteractionContext);

        // Load configurations on startup
        _ = LoadAvailableConfigurationsAsync();
    }

    /// <summary>
    /// Called every frame by the game loop to update game logic.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since last frame in seconds.</param>
    public void Update(double deltaTime)
    {
        if (CurrentWorld == null)
            return;

        // Calculate world time (elapsed ticks since world started)
        var worldTimeTicks = DateTime.UtcNow.Ticks - CurrentWorld.UtcStartTick;

        // Update MerchantTradeViewModel with delta time and world time
        //MerchantTrade?.Update(deltaTime, worldTimeTicks);

        // Check for entity respawns (throttle to once per second)
        _respawnCheckAccumulator += deltaTime;
        if (_respawnCheckAccumulator >= 1.0)
        {
            _respawnCheckAccumulator = 0;
            CheckEntityRespawns();
        }

        // Check for available interactions (throttle to twice per second)
        _interactionCheckAccumulator += deltaTime;
        if (_interactionCheckAccumulator >= 5.0)
        {
            _interactionCheckAccumulator = 0;
            _ = CheckAvailableInteractionsAsync();
        }
    }

    private double _respawnCheckAccumulator = 0;
    private double _interactionCheckAccumulator = 0;

    private void CheckEntityRespawns()
    {
        // NOTE: Respawns are now automatic via event sourcing!
        // Character availability is computed from SagaState:
        //   IsAvailable = (!IsAlive && DefeatedAt + RespawnDurationSeconds <= Now)
        // SagaStateMachine handles this automatically during replay.
        // No manual respawn logic needed.
    }

    private async Task CheckAvailableInteractionsAsync()
    {
        if (PlayerAvatar == null || CurrentWorld == null || !HasAvatarPosition)
            return;

        if (CurrentWorld.HeightMapMetadata == null)
            return; // Can't convert to pixel coords without metadata

        try
        {
            // Clear previous character list
            Characters.Clear();

            //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Querying {_sagas.Count} Sagas for spawned characters...");

            // Query ALL spawned characters for each Saga (not just nearby ones - this is Sandbox)
            foreach (var sagaVm in _sagas)
            {
                // Get Saga template for center coordinates
                if (!CurrentWorld.SagaArcLookup.TryGetValue(sagaVm.RefName, out var sagaTemplate))
                    continue;

                var query = new GetSpawnedCharactersQuery
                {
                    AvatarId = PlayerAvatar.AvatarId,
                    SagaRef = sagaVm.RefName,
                    SpawnedOnly = true,  // Only show spawned (not despawned)
                    AliveOnly = false    // Show both alive and dead
                };

                var characterStates = await _mediator.Send(query);

                //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Saga '{sagaVm.RefName}' returned {characterStates.Count} characters");

                // Add ALL spawned characters to render collection
                foreach (var characterState in characterStates)
                {
                    //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Processing character '{characterState.CharacterRef}' at Saga-relative ({characterState.CurrentLongitudeX:F6}, {characterState.CurrentLatitudeZ:F6})");

                    // Get character template for display name
                    if (!CurrentWorld.CharactersLookup.TryGetValue(characterState.CharacterRef, out var characterTemplate))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Character template '{characterState.CharacterRef}' not found in lookup");
                        continue;
                    }

                    // Convert from Saga-relative coordinates (X/Z in meters) to world GPS coordinates
                    var worldLon = CoordinateConverter.SagaRelativeXToLongitude(
                        characterState.CurrentLongitudeX,
                        sagaTemplate.LongitudeX,
                        CurrentWorld);
                    var worldLat = CoordinateConverter.SagaRelativeZToLatitude(
                        characterState.CurrentLatitudeZ,
                        sagaTemplate.LatitudeZ,
                        CurrentWorld);

                    //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Converted to world GPS: ({worldLon:F6}, {worldLat:F6})");

                    var pixelX = CoordinateConverter.HeightMapLongitudeToPixelX(
                        worldLon,
                        CurrentWorld.HeightMapMetadata);
                    var pixelY = CoordinateConverter.HeightMapLatitudeToPixelY(
                        worldLat,
                        CurrentWorld.HeightMapMetadata);

                    //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Character '{characterTemplate.DisplayName}' pixel coords: ({pixelX:F0}, {pixelY:F0})");

                    var characterVm = new CharacterViewModel
                    {
                        CharacterInstanceId = characterState.CharacterInstanceId,
                        CharacterRef = characterState.CharacterRef,
                        DisplayName = characterTemplate.DisplayName,
                        CharacterType = "Character", // Could determine from context
                        PixelX = pixelX,
                        PixelY = pixelY,
                        IsAlive = characterState.IsAlive,
                        CanDialogue = true, // Sandbox - assume all interactions available
                        CanTrade = true,
                        CanAttack = characterState.IsAlive,
                        SagaRef = sagaVm.RefName
                    };

                    // Color based on alive/dead
                    characterVm.MarkerColor = characterState.IsAlive
                        ? new System.Numerics.Vector4(1f, 0.65f, 0f, 1f) // Orange
                        : new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f); // Gray

                    Characters.Add(characterVm);

                    //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Added '{characterTemplate.DisplayName}' to collection - Total: {Characters.Count}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Finished - Total characters in collection: {Characters.Count}");

            // Now check for characters/features that want to interact (within ApproachRadius)
            await CheckForInitiatedInteractionsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CheckInteractions] ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Checks for the highest-priority interaction that wants to engage with the avatar.
    /// Uses the arbiter query to select ONE interaction across all Sagas.
    /// </summary>
    private async Task CheckForInitiatedInteractionsAsync()
    {
        if (PlayerAvatar == null || CurrentWorld == null || !HasAvatarPosition)
            return;

        try
        {
            // Query the arbiter for the single highest-priority interaction
            var query = new GetInitiatedInteractionQuery
            {
                AvatarId = PlayerAvatar.AvatarId,
                Latitude = AvatarLatitude,
                Longitude = AvatarLongitude,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(query);

            if (result.HasInteraction)
            {
                if (result.Character != null)
                {
                    // Start dialogue if character wants to talk
                    if (result.Character.Options.CanDialogue)
                    {
                        await StartDialogueWithCharacterAsync(result.SagaRef, result.Character.CharacterInstanceId);
                    }
                }
                else if (result.Feature != null)
                {
                    // WPF WINDOW CODE - TO BE DELETED WITH XAML
                    // Don't auto-interact if an interaction window is already open (WPF only)
                    // In ImGui mode, modals handle this differently
                    //if (_isInteractionWindowOpen)
                    //{
                    //    System.Diagnostics.Debug.WriteLine($"*** Feature '{result.Feature.DisplayName}' nearby but interaction window already open - skipping");
                    //    return;
                    //}

                    System.Diagnostics.Debug.WriteLine($"*** Feature '{result.Feature.DisplayName}' ({result.Feature.FeatureType}) nearby - auto-interacting");
                    // Features are simple: immediate loot/token rewards (no dialogue)
                    // For "Spirit in Temple" scenarios, spawn a Character via trigger instead
                    await InteractWithFeatureAsync(result.SagaRef, result.Feature.FeatureRef);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"*** ERROR checking initiated interactions: {ex.Message}");
        }
    }

    private string? _currentDialogueSagaRef;
    private Guid _currentDialogueCharacterInstanceId;
    private bool _isInDialogue;

    /// <summary>
    /// Interacts with a feature (shrine, chest, signpost) to receive loot/tokens.
    /// Features are stateless and give immediate rewards.
    /// </summary>
    private async Task InteractWithFeatureAsync(string sagaRef, string featureRef)
    {
        if (PlayerAvatar == null || CurrentWorld == null)
            return;

        try
        {
            var command = new InteractWithFeatureCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaArcRef = sagaRef,
                FeatureRef = featureRef,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                System.Diagnostics.Debug.WriteLine($"[FeatureInteraction] Interacted with feature '{featureRef}' - Transactions: {result.TransactionIds.Count}");
                ActivityLog?.Insert(0, $"📦 Interacted with {featureRef}");

                // Show interaction results (loot, effects, quest tokens)
                await ShowFeatureInteractionResultAsync(sagaRef, featureRef);

                // Save avatar (inventory/stats may have changed)
                await SavePlayerAvatarAsync();
                NotifyPlayerAvatarChanged();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[FeatureInteraction] ERROR: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FeatureInteraction] EXCEPTION: {ex.Message}");
        }
    }

    private async Task ShowFeatureInteractionResultAsync(string sagaRef, string featureRef)
    {
        if (CurrentWorld == null)
            return;

        // Find the feature
        var feature = CurrentWorld.TryGetSagaFeatureByRefName(featureRef);

        string title = string.Empty;
        InteractableBase? interactable = null;

        if (feature != null)
        {
            title = feature.DisplayName;
            interactable = feature.Interactable;
        }

        // WPF WINDOW CODE - TO BE DELETED WITH XAML
        /*if (interactable != null)
        {
            // Create result view model
            UpdateSagaInteractionContext();

            var resultVM = new FeatureInteractionResultViewModel();
            resultVM.LoadResults(title, interactable);

            var locationViewModel = new SagaInteractionWindowViewModel(_sagaInteractionContext)
            {
                ActivityLog = ActivityLog,
                FeatureInteractionResultViewModel = resultVM
            };

            // Mark window as open
            _isInteractionWindowOpen = true;

            // Show in window
            _interactionWindow = new SagaInteractionWindow
            {
                DataContext = locationViewModel,
                Owner = System.Windows.Application.Current.MainWindow
            };

            // Handle window closed
            _interactionWindow.Closed += (s, e) =>
            {
                _isInteractionWindowOpen = false;
            };

            _interactionWindow.ShowDialog();
            _interactionWindow = null;
            _isInteractionWindowOpen = false;
        }*/

        await Task.CompletedTask;
    }

    /// <summary>
    /// Starts dialogue with a character using CQRS commands.
    /// </summary>
    private async Task StartDialogueWithCharacterAsync(string sagaRef, Guid characterInstanceId)
    {
        if (PlayerAvatar == null)
            return;

        // Don't re-open dialogue if already in dialogue with this character
        if (_isInDialogue && _currentDialogueSagaRef == sagaRef && _currentDialogueCharacterInstanceId == characterInstanceId)
        {
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"*** Starting dialogue with character {characterInstanceId} in Saga {sagaRef}");

            // Store for later use and mark as in dialogue
            _currentDialogueSagaRef = sagaRef;
            _currentDialogueCharacterInstanceId = characterInstanceId;
            _isInDialogue = true;

            // Start dialogue command
            var startCommand = new StartDialogueCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaArcRef = sagaRef,
                CharacterInstanceId = characterInstanceId,
                Avatar = PlayerAvatar
            };

            var startResult = await _mediator.Send(startCommand);

            if (!startResult.Successful)
            {
                System.Diagnostics.Debug.WriteLine($"*** ERROR starting dialogue: {startResult.ErrorMessage}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"*** Dialogue started successfully. Transactions: {string.Join(", ", startResult.TransactionIds)}");

            // Get current dialogue state
            var state = await GetDialogueStateAsync(sagaRef, characterInstanceId);

            if (state == null || !state.IsActive)
            {
                System.Diagnostics.Debug.WriteLine($"*** DIALOGUE ENDED or NOT ACTIVE");
                return;
            }

            // Log state
            System.Diagnostics.Debug.WriteLine($"*** DIALOGUE STATE:");
            System.Diagnostics.Debug.WriteLine($"    Node: {state.CurrentNodeId}");
            System.Diagnostics.Debug.WriteLine($"    CanContinue: {state.CanContinue}");
            System.Diagnostics.Debug.WriteLine($"    HasEnded: {state.HasEnded}");
            System.Diagnostics.Debug.WriteLine($"    Text:");
            foreach (var text in state.DialogueText)
            {
                System.Diagnostics.Debug.WriteLine($"      \"{text}\"");
            }
            System.Diagnostics.Debug.WriteLine($"    Choices: {state.Choices.Count}");
            foreach (var choice in state.Choices)
            {
                var availability = choice.IsAvailable ? "✓" : "✗";
                System.Diagnostics.Debug.WriteLine($"      [{availability}] {choice.Text} -> {choice.ChoiceId}");
            }

            // Create DialogueViewModel
            var dialogueVM = new DialogueViewModel(
                onChoiceSelected: async (choice) => await OnDialogueChoiceSelectedAsync(choice),
                onContinue: async () => await OnDialogueContinueAsync()
            );
            dialogueVM.UpdateState(state);

            // WPF WINDOW CODE - TO BE DELETED WITH XAML
            // Show interaction window with dialogue
            // In ImGui mode, DialogueModal handles display
            //ShowDialogueWindow(dialogueVM);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"*** ERROR in dialogue flow: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async Task<DialogueStateResult?> GetDialogueStateAsync(string sagaRef, Guid characterInstanceId)
    {
        if (PlayerAvatar == null)
            return null;

        var stateQuery = new GetDialogueStateQuery
        {
            AvatarId = PlayerAvatar.AvatarId,
            SagaRef = sagaRef,
            CharacterInstanceId = characterInstanceId,
            Avatar = PlayerAvatar
        };

        return await _mediator.Send(stateQuery);
    }

    private async Task OnDialogueChoiceSelectedAsync(DialogueChoiceOption choice)
    {
        if (PlayerAvatar == null || _currentDialogueSagaRef == null)
            return;

        System.Diagnostics.Debug.WriteLine($"*** Player selected choice: {choice.Text} -> {choice.ChoiceId}");

        // Send SelectDialogueChoiceCommand
        var selectCommand = new SelectDialogueChoiceCommand
        {
            AvatarId = PlayerAvatar.AvatarId,
            SagaArcRef = _currentDialogueSagaRef,
            CharacterInstanceId = _currentDialogueCharacterInstanceId,
            ChoiceId = choice.ChoiceId,
            Avatar = PlayerAvatar
        };

        var result = await _mediator.Send(selectCommand);

        if (!result.Successful)
        {
            System.Diagnostics.Debug.WriteLine($"*** ERROR selecting choice: {result.ErrorMessage}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"*** Choice selected successfully. Transactions: {string.Join(", ", result.TransactionIds)}");

        // Check for pending system events (OpenMerchantTrade, StartBossBattle, etc.)
        System.Diagnostics.Debug.WriteLine($"*** Checking result.Data for events. Keys: {string.Join(", ", result.Data.Keys)}");

        if (result.Data.TryGetValue("PendingEvents", out var eventsObj))
        {
            System.Diagnostics.Debug.WriteLine($"*** Found PendingEvents. Type: {eventsObj?.GetType().Name ?? "null"}");

            // Try to cast to IList or IEnumerable to handle different list types
            if (eventsObj is System.Collections.IList eventsList)
            {
                System.Diagnostics.Debug.WriteLine($"*** Processing {eventsList.Count} events");
                foreach (var evt in eventsList)
                {
                    if (evt == null) continue;

                    System.Diagnostics.Debug.WriteLine($"*** Processing dialogue event: {evt.GetType().Name}");

                    // WPF WINDOW CODE - TO BE DELETED WITH XAML
                    /*if (evt is Game.Gameplay.SagaEngine.Domain.Rpg.Dialogue.Events.OpenMerchantTradeEvent merchantEvent)
                    {
                        System.Diagnostics.Debug.WriteLine($"*** Opening merchant trade for {merchantEvent.CharacterRef}");

                        // Switch from dialogue to trade in the same window
                        _currentDialogueViewModel = null;
                        _isInDialogue = false;

                        // Close current interaction window if open (WPF only)
                        if (!_useImGuiMode && _interactionWindow != null)
                        {
                            _interactionWindow.Close();
                            _interactionWindow = null;
                        }

                        // Show merchant trade (WPF only - ImGui handles via modals)
                        if (!_useImGuiMode)
                        {
                            await ShowMerchantTradeAsync(_currentDialogueSagaRef!, _currentDialogueCharacterInstanceId);
                        }
                        return; // Don't refresh dialogue state, we've switched to trade
                    }
                    else if (evt is Game.Gameplay.SagaEngine.Domain.Rpg.Dialogue.Events.StartBossBattleEvent bossEvent)
                    {
                        System.Diagnostics.Debug.WriteLine($"*** Starting boss battle with {bossEvent.CharacterRef}");

                        // Switch from dialogue to battle in the same window
                        _currentDialogueViewModel = null;
                        _isInDialogue = false;

                        // Close current interaction window if open (WPF only)
                        if (!_useImGuiMode && _interactionWindow != null)
                        {
                            _interactionWindow.Close();
                            _interactionWindow = null;
                        }

                        // Show boss battle (WPF only - ImGui handles via modals)
                        if (!_useImGuiMode)
                        {
                            await ShowBossBattleAsync(_currentDialogueSagaRef!, _currentDialogueCharacterInstanceId);
                        }
                        return; // Don't refresh dialogue state, we've switched to battle
                    }*/
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"*** PendingEvents is not IList, cannot process");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"*** No PendingEvents in result.Data");
        }

        // Refresh dialogue state
        await RefreshDialogueStateAsync();
    }

    private async Task OnDialogueContinueAsync()
    {
        System.Diagnostics.Debug.WriteLine($"*** Player clicked Continue");
        // For auto-advance nodes, we could call an AdvanceDialogueCommand here
        // For now, just refresh to see if dialogue has ended
        await RefreshDialogueStateAsync();
    }

    // WPF WINDOW CODE - TO BE DELETED WITH XAML
    /*private async Task ShowMerchantTradeAsync(string sagaRef, Guid characterInstanceId)
    {
        System.Diagnostics.Debug.WriteLine($"*** ShowMerchantTradeAsync: {sagaRef}, {characterInstanceId}");

        if (CurrentWorld == null || PlayerAvatar == null)
            return;

        // Get Saga state to find the character
        var getSpawnedCharsQuery = new Game.Gameplay.SagaEngine.GameCqrs.Queries.Saga.GetSpawnedCharactersQuery
        {
            AvatarId = PlayerAvatar.AvatarId,
            SagaRef = sagaRef
        };

        var spawnedCharacters = await _mediator.Send(getSpawnedCharsQuery);
        if (spawnedCharacters == null || spawnedCharacters.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"*** No characters found in Saga");
            return;
        }

        var characterState = spawnedCharacters.FirstOrDefault(c => c.CharacterInstanceId == characterInstanceId);
        if (characterState == null)
        {
            System.Diagnostics.Debug.WriteLine($"*** Character {characterInstanceId} not found");
            return;
        }

        // Get character template
        if (!CurrentWorld.CharactersLookup.TryGetValue(characterState.CharacterRef, out var characterTemplate))
        {
            System.Diagnostics.Debug.WriteLine($"*** Character template '{characterState.CharacterRef}' not found");
            return;
        }

        // Create character instance for the context (use template data)
        // The character template has both Capabilities (what they can DO) and Interactable.Loot (what they HAVE)
        var character = new Character
        {
            DisplayName = characterTemplate.DisplayName,
            Capabilities = characterTemplate.Capabilities,
            Stats = characterTemplate.Stats,
            Interactable = characterTemplate.Interactable
        };

        // Update context with active character
        UpdateSagaInteractionContext();
        _sagaInteractionContext.ActiveCharacter = character;

        // Create merchant trade viewmodel
        var merchantTradeVM = new MerchantTradeViewModel(_sagaInteractionContext, _mediator);
        merchantTradeVM.ActivityMessageGenerated += (s, msg) => ActivityLog.Add(msg);
        merchantTradeVM.RefreshCategories();

        // Create interaction window viewmodel
        var locationViewModel = new SagaInteractionWindowViewModel(_sagaInteractionContext)
        {
            ActivityLog = ActivityLog,
            MerchantTradeViewModel = merchantTradeVM
        };

        // Mark window as open
        _isInteractionWindowOpen = true;

        // Show in window
        _interactionWindow = new SagaInteractionWindow
        {
            DataContext = locationViewModel,
            Owner = System.Windows.Application.Current.MainWindow
        };

        // Handle window closed
        _interactionWindow.Closed += async (s, e) =>
        {
            _isInteractionWindowOpen = false;

            // Save player avatar after trading
            await SavePlayerAvatarAsync();
            NotifyPlayerAvatarChanged();
        };

        _interactionWindow.ShowDialog();
        _interactionWindow = null;
        _isInteractionWindowOpen = false;
    }*/

    // WPF WINDOW CODE - TO BE DELETED WITH XAML
    /*private async Task ShowBossBattleAsync(string sagaRef, Guid characterInstanceId)
    {
        System.Diagnostics.Debug.WriteLine($"*** ShowBossBattleAsync: {sagaRef}, {characterInstanceId}");

        if (CurrentWorld == null || PlayerAvatar == null)
            return;

        // Get Saga state to find the character
        var getSpawnedCharsQuery = new Game.Gameplay.SagaEngine.GameCqrs.Queries.Saga.GetSpawnedCharactersQuery
        {
            AvatarId = PlayerAvatar.AvatarId,
            SagaRef = sagaRef
        };

        var spawnedCharacters = await _mediator.Send(getSpawnedCharsQuery);
        if (spawnedCharacters == null || spawnedCharacters.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"*** No characters found in Saga");
            return;
        }

        var characterState = spawnedCharacters.FirstOrDefault(c => c.CharacterInstanceId == characterInstanceId);
        if (characterState == null)
        {
            System.Diagnostics.Debug.WriteLine($"*** Character {characterInstanceId} not found");
            return;
        }

        // Get character template
        if (!CurrentWorld.CharactersLookup.TryGetValue(characterState.CharacterRef, out var characterTemplate))
        {
            System.Diagnostics.Debug.WriteLine($"*** Character template '{characterState.CharacterRef}' not found");
            return;
        }

        // Create character instance for battle
        var character = new Character
        {
            RefName = characterTemplate.RefName,
            DisplayName = characterTemplate.DisplayName,
            Capabilities = characterTemplate.Capabilities,
            Stats = characterTemplate.Stats
        };

        // Update context with active character
        UpdateSagaInteractionContext();
        _sagaInteractionContext.ActiveCharacter = character;

        // Create boss battle viewmodel
        var bossBattleVM = new BossBattleViewModel();
        bossBattleVM.Initialize(character, PlayerAvatar, isBossAlive: true, CurrentWorld);

        // Subscribe to boss defeated event
        bossBattleVM.BossDefeated += async (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"*** Boss defeated: {args.BossRefName}");
            ActivityLog.Add($"Victory! {args.BossRefName} defeated!");

            // Mark boss as defeated in saga state
            var defeatCharacterCommand = new Game.Gameplay.SagaEngine.GameCqrs.Commands.Saga.DefeatCharacterCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaRef = sagaRef,
                CharacterInstanceId = characterInstanceId
            };

            var result = await _mediator.Send(defeatCharacterCommand);
            if (!result.Successful)
            {
                ActivityLog.Insert(0, $"⚠️ Failed to record boss defeat: {result.ErrorMessage}");
            }
        };

        // Create interaction window viewmodel
        var locationViewModel = new SagaInteractionWindowViewModel(_sagaInteractionContext)
        {
            ActivityLog = ActivityLog,
            BossBattleViewModel = bossBattleVM
        };

        // Mark window as open
        _isInteractionWindowOpen = true;

        // Show in window
        _interactionWindow = new SagaInteractionWindow
        {
            DataContext = locationViewModel,
            Owner = System.Windows.Application.Current.MainWindow
        };

        // Handle window closed
        _interactionWindow.Closed += async (s, e) =>
        {
            _isInteractionWindowOpen = false;

            // Save player avatar after battle
            await SavePlayerAvatarAsync();
            NotifyPlayerAvatarChanged();
        };

        _interactionWindow.ShowDialog();
        _interactionWindow = null;
        _isInteractionWindowOpen = false;
    }*/

    private async Task RefreshDialogueStateAsync()
    {
        if (_currentDialogueSagaRef == null || _currentDialogueViewModel == null)
            return;

        var state = await GetDialogueStateAsync(_currentDialogueSagaRef, _currentDialogueCharacterInstanceId);

        if (state == null || !state.IsActive)
        {
            System.Diagnostics.Debug.WriteLine($"*** Dialogue ended, transitioning based on character state");
            _currentDialogueViewModel = null;
            _isInDialogue = false;

            // WPF WINDOW CODE - TO BE DELETED WITH XAML
            // Query character state from transaction log to determine next interaction
            // In ImGui mode, DialogueModal handles transitions itself
            //await TransitionAfterDialogueAsync();
            return;
        }

        // Update ViewModel
        _currentDialogueViewModel.UpdateState(state);
    }

    // WPF WINDOW CODE - TO BE DELETED WITH XAML
    /*/// <summary>
    /// After dialogue ends, query character state and transition to appropriate UI.
    /// Keeps the window open, just switches what's displayed (dialogue → battle → trade → loot).
    /// </summary>
    private async Task TransitionAfterDialogueAsync()
    {
        if (_currentDialogueSagaRef == null || PlayerAvatar == null)
        {
            _interactionWindow?.Close();
            return;
        }

        try
        {
            // Query available interactions from transaction log
            var interactionsQuery = new Game.Gameplay.SagaEngine.GameCqrs.Queries.Saga.GetAvailableInteractionsQuery
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaRef = _currentDialogueSagaRef,
                Latitude = AvatarLatitude,
                Longitude = AvatarLongitude,
                Avatar = PlayerAvatar
            };

            var interactions = await _mediator.Send(interactionsQuery);

            // Find the character we were just talking to
            var character = interactions.NearbyCharacters.FirstOrDefault(c => c.CharacterInstanceId == _currentDialogueCharacterInstanceId);

            if (character == null)
            {
                System.Diagnostics.Debug.WriteLine($"*** Character no longer available, closing window");
                _interactionWindow?.Close();
                return;
            }

            System.Diagnostics.Debug.WriteLine($"*** Character state after dialogue: CanAttack={character.Options.CanAttack}, CanTrade={character.Options.CanTrade}, CanLoot={character.Options.CanLoot}");

            // Transition based on character state
            if (character.Options.CanAttack && character.State.IsAlive)
            {
                System.Diagnostics.Debug.WriteLine($"*** Transitioning to COMBAT (character is hostile)");
                // TODO: Transition to BossBattleControl in same window
                // For now, just log it
                ActivityLog?.Insert(0, $"⚔️ {character.DisplayName} attacks!");
            }
            else if (character.Options.CanLoot)
            {
                System.Diagnostics.Debug.WriteLine($"*** Transitioning to LOOT (character defeated)");
                ActivityLog?.Insert(0, $"💀 {character.DisplayName} has been defeated");
                // TODO: Show loot UI
            }
            else if (character.Options.CanTrade)
            {
                System.Diagnostics.Debug.WriteLine($"*** Transitioning to TRADE (character is friendly merchant)");
                ActivityLog?.Insert(0, $"💰 Trading with {character.DisplayName}");
                // TODO: Transition to MerchantTradeControl in same window
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"*** No further interactions available, closing window");
                ActivityLog?.Insert(0, $"👋 Conversation with {character.DisplayName} ended");
                _interactionWindow?.Close();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"*** ERROR transitioning after dialogue: {ex.Message}");
            _interactionWindow?.Close();
        }
    }*/

    private DialogueViewModel? _currentDialogueViewModel;

    // WPF WINDOW CODE - TO BE DELETED WITH XAML
    /*private void ShowDialogueWindow(DialogueViewModel dialogueVM)
    {
        // Close existing window
        _interactionWindow?.Close();

        _currentDialogueViewModel = dialogueVM;

        // Create SagaInteractionWindowViewModel with dialogue
        var locationViewModel = new SagaInteractionWindowViewModel(_sagaInteractionContext)
        {
            TriggeredCharacter = TriggeredCharacter,
            ActivityLog = ActivityLog,
            DialogueViewModel = dialogueVM
        };

        // Create and show window
        _interactionWindow = new Ambient.Presentation.WindowsUI.RpgControls.Views.SagaInteraction.SagaInteractionWindow
        {
            DataContext = locationViewModel,
            Owner = System.Windows.Application.Current.MainWindow
        };

        // Clear dialogue flag when window closes
        _interactionWindow.Closed += (s, e) =>
        {
            _isInDialogue = false;
            _currentDialogueViewModel = null;
            System.Diagnostics.Debug.WriteLine($"*** Dialogue window closed");
        };

        _interactionWindow.Show();
        System.Diagnostics.Debug.WriteLine($"*** Dialogue window opened");
    }*/

    /// <summary>
    /// Called every frame by the game loop to update visual state.
    /// </summary>
    public void Render()
    {
        if (CurrentWorld == null)
            return;

        // Render updates for child ViewModels
        //MerchantTrade?.Render();
    }

    private async Task LoadAvailableConfigurationsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading available configurations...";

            var configurations = await _mediator.Send(new LoadAvailableWorldConfigurationsQuery
            {
                DataDirectory = _dataDirectory,
                DefinitionDirectory = _schemaDirectory
            });

            AvailableConfigurations.Clear();
            foreach (var config in configurations)
            {
                AvailableConfigurations.Add(config);
            }

            // Don't auto-select - user will select from dropdown
            StatusMessage = $"Loaded {AvailableConfigurations.Count} world configurations";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading configurations: {ex.Message}";
            // MessageBox removed - UI layer should display StatusMessage
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadSelectedConfigurationAsync()
    {
        if (SelectedConfiguration == null)
        {
            StatusMessage = "Please select a configuration to load.";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Loading world configuration: {SelectedConfiguration.RefName}...";

            var world = await _mediator.Send(new LoadWorldQuery
            {
                DataDirectory = _dataDirectory,
                DefinitionDirectory = _schemaDirectory,
                ConfigurationRefName = SelectedConfiguration.RefName
            });

            // Initialize world bootstrapper (required)
            WorldBootstrapper.Initialize(world);

            CurrentWorld = world;

            // Update AvatarInfo with world data
            AvatarInfo.UpdateWorld(world);

            // Initialize LiteDB persistence for this world
            InitializeWorldDatabase(world);

            // Load or create avatar
            await LoadOrCreateAvatarAsync(world);

            // Load and display height map if available
            await LoadHeightMapImageAsync(world);

            // Load Sagas and triggers from world with feature status
            var (sagas, triggers) = SagaViewModel.LoadFromWorld(world, PlayerAvatar, _worldRepository);
            Sagas.Clear();
            AllTriggers.Clear();
            foreach (var saga in sagas) Sagas.Add(saga);
            foreach (var trigger in triggers) AllTriggers.Add(trigger);
            StatusMessage = $"Initialized {sagas.Count} Sagas with {triggers.Count} triggers";

            // Initialize avatar position at spawn if available
            InitializeAvatarPosition(world);

            // NOTE: Character spawning/display removed - triggers only

            StatusMessage = $"Loaded: {SelectedConfiguration.RefName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading configuration: {ex.Message}";
            // MessageBox removed - UI layer should display StatusMessage
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadHeightMapImageAsync(World world)
    {
        // Clear previous image
        HeightMapImage = null;
        HeightMapInfo = null;
        AvatarInfo.UpdateHeightMapStatus(false);

        // Check if this world has height map settings
        if (world.IsProcedural)
        {
            // For procedural worlds with GenerationConfiguration, create a placeholder heightmap
            // so spiral-generated SagaArcs can be visualized
            var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var configsDir = Path.Combine(solutionDir, "Ambient.Saga.StoryGenerator", "GenerationConfigs");
            var generationConfigLoader = new GenerationConfigurationLoader(configsDir);
            if (generationConfigLoader.HasGenerationConfig(world.WorldConfiguration.RefName))
            {
                StatusMessage = "Creating placeholder visualization for procedural world...";
                const int placeholderSize = 1024;
                var placeholderBitmap = await CreatePlaceholderHeightMapAsync(placeholderSize, placeholderSize);
                HeightMapImage = placeholderBitmap;
                AvatarInfo.UpdateHeightMapStatus(true);

                // Create fake HeightMapMetadata for coordinate conversion
                // Spiral extends ~260m from spawn, so create bounds of ±0.003° (~330m at equator)
                var spawnLat = world.WorldConfiguration.SpawnLatitude;
                var spawnLon = world.WorldConfiguration.SpawnLongitude;
                const double boundsSize = 0.003; // degrees

                world.HeightMapMetadata = new Ambient.Domain.ValueObjects.GeoTiffMetadata
                {
                    North = spawnLat + boundsSize,
                    South = spawnLat - boundsSize,
                    East = spawnLon + boundsSize,
                    West = spawnLon - boundsSize,
                    ImageWidth = placeholderSize,
                    ImageHeight = placeholderSize,
                    BitsPerSample = 16,
                    SamplesPerPixel = 1,
                    PixelScale = (boundsSize * 2 / placeholderSize, boundsSize * 2 / placeholderSize, 0),
                    TiePoint = (0, 0, 0, spawnLon - boundsSize, spawnLat + boundsSize, 0)
                };

                HeightMapInfo = "Placeholder visualization (1024 x 1024)\nProcedural world with spiral SagaArcs\n" +
                               $"Bounds: {world.HeightMapMetadata.North:F4}°N to {world.HeightMapMetadata.South:F4}°S, " +
                               $"{world.HeightMapMetadata.West:F4}°W to {world.HeightMapMetadata.East:F4}°E";
                UpdateMinimumZoom();
            }
            else
            {
                HeightMapInfo = "This world uses procedural settings (no height map)";
            }
            return;
        }

        try
        {
            var heightMapPath = Path.Combine(_dataDirectory, world.WorldConfiguration.HeightMapSettings.RelativePath);
            
            if (!File.Exists(heightMapPath))
            {
                HeightMapInfo = $"Height map file not found: {heightMapPath}";
                return;
            }

            StatusMessage = "Loading height map image...";

            // Load the TIFF using ImageSharp
            using var image = await SixLabors.ImageSharp.Image.LoadAsync<L16>(heightMapPath);
            
            // Preprocess height map with water detection
            StatusMessage = "Processing height map for water detection...";
            var processedMap = await Task.Run(() => HeightMapProcessor.ProcessHeightMap(image, 40, true));
            _processedHeightMap = processedMap;
            
            // Convert to platform-agnostic image data for display with water-aware coloring
            var imageData = await ConvertProcessedMapToImageDataAsync(processedMap);
            HeightMapImage = imageData;
            AvatarInfo.UpdateHeightMapStatus(true);
            
            // Update minimum zoom to ensure image always fills viewport
            UpdateMinimumZoom();
            
            // Set image info with elevation range and water detection info
            var waterPixelCount = 0;
            for (int x = 0; x < processedMap.Width; x++)
            {
                for (int y = 0; y < processedMap.Height; y++)
                {
                    if (processedMap.WaterMask[x, y]) waterPixelCount++;
                }
            }
            var waterPercentage = (double)waterPixelCount / (processedMap.Width * processedMap.Height) * 100;

            if (world.HeightMapMetadata != null)
            {
                HeightMapInfo = $"Height Map: {world.HeightMapMetadata.ImageWidth} x {world.HeightMapMetadata.ImageHeight} pixels\n" +
                               $"Bounds: {world.HeightMapMetadata.North:F4}°N to {world.HeightMapMetadata.South:F4}°S, " +
                               $"{world.HeightMapMetadata.West:F4}°W to {world.HeightMapMetadata.East:F4}°E\n" +
                               $"Pixel Size: {world.HeightMapMetadata.Width / world.HeightMapMetadata.ImageWidth * 111320:F1}m x " +
                               $"{world.HeightMapMetadata.Height / world.HeightMapMetadata.ImageHeight * 111320:F1}m\n" +
                               $"Elevation: {processedMap.MinElevation}m to {processedMap.MaxElevation}m (Sea Level: {processedMap.SeaLevel}m)\n" +
                               $"Relief: {processedMap.MaxElevation - processedMap.MinElevation}m\n" +
                               $"Water Coverage: {waterPercentage:F1}% ({waterPixelCount:N0} pixels)";
            }
            else
            {
                HeightMapInfo = $"Height Map: {processedMap.Width} x {processedMap.Height} pixels\n" +
                               $"Elevation: {processedMap.MinElevation}m to {processedMap.MaxElevation}m (Sea Level: {processedMap.SeaLevel}m)\n" +
                               $"Water Coverage: {waterPercentage:F1}% ({waterPixelCount:N0} pixels)";
            }
        }
        catch (Exception ex)
        {
            HeightMapInfo = $"Error loading height map: {ex.Message}";
        }
    }


    // OLD METHOD REMOVED: SpawnCharactersFromTrigger
    // Character spawning now handled by CQRS UpdateAvatarPositionCommand via ProcessAvatarMovementAsync

    private static async Task<HeightMapImageData> CreatePlaceholderHeightMapAsync(int width, int height)
    {
        var stride = width * 4; // 4 bytes per pixel for BGRA32

        // Create simple gradient background on background thread
        var pixelData = await Task.Run(() =>
        {
            var data = new byte[height * stride];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Create subtle radial gradient from center (lighter) to edges (darker)
                    var centerX = width / 2.0;
                    var centerY = height / 2.0;
                    var maxDist = Math.Sqrt(centerX * centerX + centerY * centerY);
                    var dist = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
                    var brightness = (byte)(200 - (dist / maxDist * 50)); // 200 at center, 150 at edges

                    var index = y * stride + x * 4;
                    data[index] = brightness;     // Blue
                    data[index + 1] = brightness; // Green
                    data[index + 2] = brightness; // Red
                    data[index + 3] = 255;        // Alpha (fully opaque)
                }
            }

            return data;
        });

        // Create platform-agnostic image data
        return new HeightMapImageData(pixelData, width, height, stride);
    }

    private static async Task<HeightMapImageData> ConvertProcessedMapToImageDataAsync(HeightMapProcessor.ProcessedHeightMap processedMap)
    {
        var width = processedMap.Width;
        var height = processedMap.Height;
        var stride = width * 4; // 4 bytes per pixel for BGRA32

        // Process pixel data on background thread with water-aware color mapping
        var pixelData = await Task.Run(() =>
        {
            var data = new byte[height * stride];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var color = HeightMapProcessor.GetElevationColorWithWater(x, y, processedMap);

                    var index = y * stride + x * 4;
                    data[index] = color.B;     // Blue
                    data[index + 1] = color.G; // Green
                    data[index + 2] = color.R; // Red
                    data[index + 3] = 255;     // Alpha (fully opaque)
                }
            }

            return data;
        });

        // Create platform-agnostic image data
        return new HeightMapImageData(pixelData, width, height, stride);
    }


    [RelayCommand]
    private void ZoomIn()
    {
        ZoomFactor = Clamp(ZoomFactor * Step);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomFactor = Clamp(ZoomFactor / Step);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomFactor = 1.0;
    }

    private double Clamp(double v) => Math.Max(MinimumZoom, Math.Min(MaxZoom, v));

    public void UpdateViewportSize(double width, double height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
        UpdateMinimumZoom();
    }

    private void UpdateMinimumZoom()
    {
        if (_processedHeightMap == null || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            MinimumZoom = 0.1;
            return;
        }

        // Calculate minimum zoom to ensure image always fills viewport
        var imageWidth = _processedHeightMap.Width;
        var imageHeight = _processedHeightMap.Height;
        
        var minZoomForWidth = ViewportWidth / imageWidth;
        var minZoomForHeight = ViewportHeight / imageHeight;
        
        // Use the larger of the two to ensure both dimensions are covered
        var calculatedMinZoom = Math.Max(minZoomForWidth, minZoomForHeight);
        
        // Ensure minimum zoom is at least 0.1 and not too restrictive
        MinimumZoom = Math.Max(0.1, calculatedMinZoom);
        
        // If current zoom is below new minimum, adjust it
        if (ZoomFactor < MinimumZoom)
        {
            ZoomFactor = MinimumZoom;
        }
    }

    private void InitializeWorldDatabase(World world)
    {
        try
        {
            // Dispose existing database if any
            _worldDatabase?.Dispose();

            StatusMessage = "Initializing world database...";

            // Use factory to create all repositories (eliminates Infrastructure imports)
            var repositories = _repositoryFactory.CreateRepositories(
                "Carbon",
                world.WorldConfiguration.RefName,
                world,
                SteamContext.IsSteamInitialized);

            _worldDatabase = repositories.Database;
            _worldRepository = repositories.WorldStateRepository;
            _steamAchievementService = repositories.SteamAchievementService;

            // Initialize CQRS providers
            _worldProvider.SetWorld(world);
            _repositoryProvider.SetRepository(repositories.SagaRepository);
            _avatarRepositoryProvider.SetRepository(repositories.AvatarRepository);
            _worldStateRepositoryProvider.SetRepository(repositories.WorldStateRepository);

            // Inject persistence services into AchievementViewModel
            Achievements?.SetPersistence(_worldRepository, _steamAchievementService);

            // NOTE: Saga instances are created on-demand when first accessed
            // GetOrCreateSinglePlayerInstance will create them as needed

            // NOTE: Old mutable instance collections removed!
            // All character/landmark/structure state now comes from SagaState (event-sourced)
            // State is derived by replaying Saga transactions, not stored separately


            // Replay pending Steam achievements if avatar exists
            if (PlayerAvatar != null)
            {
                var avatarId = PlayerAvatar.AvatarId.ToString();
                _steamAchievementService.ReplayAchievementsToSteam(avatarId);
            }

            StatusMessage = $"Database initialized: {world.WorldConfiguration.RefName}.db";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Database initialization failed: {ex.Message}";
        }
    }

    // NOTE: Character spawning/display methods removed
    // Sandbox now focuses on triggers only
    // Character rendering will be implemented properly when needed using CQRS queries

    private void InitializeAvatarPosition(World world)
    {
        // Clear previous position
        HasAvatarPosition = false;
        AvatarInfo.UpdateAvatarPosition(0, 0, 0, false);

        if (world.HeightMapMetadata == null)
            return;

        try
        {
            // Try to use spawn position from world configuration
            var spawnLat = world.WorldConfiguration.SpawnLatitude;
            var spawnLong = world.WorldConfiguration.SpawnLongitude;

            SetAvatarPosition(spawnLat, spawnLong, world.HeightMapMetadata, centerOnAvatar: true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not set initial avatar position: {ex.Message}";
        }
    }

    public async void SetAvatarPosition(double latitude, double longitude, IHeightMapMetadata metadata, bool centerOnAvatar = false)
    {
        AvatarLatitude = latitude;
        AvatarLongitude = longitude;
        AvatarPixelX = CoordinateConverter.HeightMapLongitudeToPixelX(longitude, metadata);
        AvatarPixelY = CoordinateConverter.HeightMapLatitudeToPixelY(latitude, metadata);

        // Sample elevation from height map
        AvatarElevation = SampleElevation((int)AvatarPixelX, (int)AvatarPixelY);

        HasAvatarPosition = true;

        // Update AvatarInfo with position
        AvatarInfo.UpdateAvatarPosition(AvatarLatitude, AvatarLongitude, AvatarElevation, HasAvatarPosition);

        // Handle trigger enter/exit logic based on new avatar position
        if (CurrentWorld != null)
        {
            var avatarModelX = CoordinateConverter.LongitudeToModelX(AvatarLongitude, CurrentWorld);
            var avatarModelZ = CoordinateConverter.LatitudeToModelZ(AvatarLatitude, CurrentWorld);

            var triggerAtPosition = await FindTriggerAtPoint(avatarModelX, avatarModelZ);

            // Detect trigger changes and trigger OnExit/OnEnter
            if (_previousTrigger != triggerAtPosition)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Trigger changed: {_previousTrigger?.RefName ?? "null"} -> {triggerAtPosition?.RefName ?? "null"}");

                // RESET EVERYTHING on trigger change
                IsSagaInteractionActive = false;
                TriggeredCharacter = null;
                OnCharacterChanged(); // Clear dialogue when character is removed

                // Exit previous trigger
                if (_previousTrigger != null)
                {
                    ActivityLog.Insert(0, $"Exited {_previousTrigger.DisplayName} - No exit action");
                }

                // Enter new SagaTrigger via CQRS
                if (triggerAtPosition != null && PlayerAvatar != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] Calling ProcessAvatarMovementAsync for trigger '{triggerAtPosition.RefName}'");
                    // Send UpdateAvatarPositionCommand via CQRS
                    _ = ProcessAvatarMovementAsync(triggerAtPosition);
                }

                _previousTrigger = triggerAtPosition;
            }
            else if (triggerAtPosition != null)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Already in trigger '{triggerAtPosition.RefName}' - not calling UpdateAvatarPositionCommand");
            }

            // Auto-select the current trigger
            SelectedTrigger = triggerAtPosition;
        }

        if (centerOnAvatar)
        {
            CenterMapOnAvatar();
        }
    }

    /// <summary>
    /// Processes avatar movement using CQRS UpdateAvatarPositionCommand.
    /// Replaces direct calls to SpawnCharactersFromTrigger.
    /// </summary>
    private async Task ProcessAvatarMovementAsync(ProximityTriggerViewModel trigger)
    {
        if (CurrentWorld == null || PlayerAvatar == null)
            return;

        try
        {
            // Get avatar Guid for CQRS
            var avatarIdString = GetAvatarId();
            var avatarGuid = Guid.Parse(avatarIdString);

            var command = new UpdateAvatarPositionCommand
            {
                AvatarId = avatarGuid,
                SagaArcRef = trigger.SagaRefName,
                Latitude = AvatarLatitude,
                Longitude = AvatarLongitude,
                Y = AvatarElevation,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                // Pure CQRS: Command succeeded, no state data returned
                ActivityLog.Insert(0, $"Entered {trigger.DisplayName}");
            }
            else
            {
                ActivityLog.Insert(0, $"Error processing movement: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Insert(0, $"Error processing movement via CQRS: {ex.Message}");
        }
    }

    private void CenterMapOnAvatar()
    {
        // Trigger centering through property change - the view will handle this
        ShouldCenterOnAvatar = true;
        // Reset the flag after a short delay to allow the view to react
        Task.Delay(100).ContinueWith(_ => ShouldCenterOnAvatar = false);
    }

    private void ShowLootForTrigger(ProximityTriggerViewModel triggerViewModel, ref string actionMessage)
    {
        if (CurrentWorld == null || PlayerAvatar == null || _worldRepository == null)
        {
            actionMessage += " - Error: No world or avatar";
            return;
        }

        // Look up Saga to get the entity reference
        var saga = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault(p => p.RefName == triggerViewModel.SagaRefName);
        if (saga == null)
        {
            actionMessage += $" - Error: Saga '{triggerViewModel.SagaRefName}' not found";
            return;
        }

        // saga.SagaFeatureRef contains the entity ref (e.g., "BAMBOO_GROVE", "KINKAKUJI_TEMPLE_HISTORY")
        var entityRef = saga.SagaFeatureRef;

        // Look up the feature to determine type
        var sagaFeature = CurrentWorld.TryGetSagaFeatureByRefName(entityRef);

        // Determine feature type from saga
        FeatureType? featureType = null;
        if (sagaFeature != null)
        {
            if (sagaFeature.Type == SagaFeatureType.Landmark)
                featureType = FeatureType.Landmark;
            else if (sagaFeature.Type == SagaFeatureType.Structure)
                featureType = FeatureType.Structure;
            else if (sagaFeature.Type == SagaFeatureType.Quest)
                featureType = FeatureType.QuestSignpost;
        }

        // Check cooldown/availability before showing loot
        if (featureType != null && !CheckEntityAvailability(featureType.Value, entityRef, ref actionMessage))
        {
            return; // On cooldown - message already set
        }

        // Create a fake "loot container" character to hold the loot
        var lootCharacter = new Character
        {
            RefName = entityRef,
            DisplayName = triggerViewModel.DisplayName,
            Description = $"Available items from {triggerViewModel.DisplayName}"
        };

        ItemCollection? loot = null;
        RewardEffects? effects = null;

        // TODO: This method needs refactoring - proximity triggers don't have types
        // Features (Landmark/Structure) should be handled separately
        // For now, determine feature type from saga
        var saga2 = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault(s => s.RefName == triggerViewModel.SagaRefName);
        if (saga2 != null && !string.IsNullOrEmpty(saga2.SagaFeatureRef))
        {
            var feature = CurrentWorld.TryGetSagaFeatureByRefName(saga2.SagaFeatureRef);
            if (feature != null)
            {
                if (feature.Type == SagaFeatureType.Landmark)
                {
                    // Show lore content
                    if (!string.IsNullOrEmpty(feature.Interactable?.Content))
                    {
                        lootCharacter.Description = feature.Interactable.Content;
                        ActivityLog.Insert(0, $"📜 {feature.Interactable.Content}");
                    }
                    effects = feature.Interactable?.Effects;
                }
                else if (feature.Type == SagaFeatureType.Structure)
                {
                    loot = feature.Interactable?.Loot;
                    effects = feature.Interactable?.Effects;
                }
            }
            else
            {
                actionMessage += $" - Error: Feature '{entityRef}' not found";
                return;
            }
        }

        // Populate the character with loot
        if (loot != null)
        {
            lootCharacter.Capabilities = loot;
        }

        // Apply effects immediately (auto-grant on trigger)
        if (effects != null)
        {
            var effectsList = new List<string>();
            ApplyRewardEffects(effects, effectsList);
            if (effectsList.Count > 0)
            {
                actionMessage += $" - Effects: {string.Join(", ", effectsList)}";
            }
        }

        // Track current entity for trigger recording
        _currentEntityRef = entityRef;
        // TODO: Set _currentEntityType based on saga feature type
        if (sagaFeature != null)
        {
            if (sagaFeature.Type == SagaFeatureType.Landmark)
                _currentEntityType = FeatureType.Landmark;
            else if (sagaFeature.Type == SagaFeatureType.Structure)
                _currentEntityType = FeatureType.Structure;
            else if (sagaFeature.Type == SagaFeatureType.Quest)
                _currentEntityType = FeatureType.QuestSignpost;
        }

        // Set the character and show trade UI
        TriggeredCharacter = lootCharacter;
        // Loot containers are not merchants (only player can sell)
        MerchantTrade.RefreshCategories();
        OnCharacterChanged();

        actionMessage += " - Items available";
    }

    private bool CheckEntityAvailability(FeatureType featureType, string entityRef, ref string actionMessage)
    {
        if (_worldRepository == null) return false;

        switch (featureType)
        {
            case FeatureType.Landmark:
                // TODO: Check landmark state from SagaState (event-sourced)
                // For now, landmarks are always available
                break;

            case FeatureType.Structure:
                // TODO: Check structure state from SagaState (event-sourced)
                // Need to query SagaState.Structures to check IsDestroyed
                // For now, structures are always available
                break;
        }

        return true;
    }

    private async void RecordEntityTrigger()
    {
        if (CurrentWorld == null || PlayerAvatar == null) return;
        if (_currentEntityRef == null || _currentEntityType == null || SelectedTrigger == null) return;

        try
        {
            var command = new InteractWithFeatureCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaArcRef = SelectedTrigger.SagaRefName,
                FeatureRef = _currentEntityRef,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                ActivityLog.Insert(0, $"✨ Feature interaction recorded: {_currentEntityRef} [CQRS]");

                // Update the SagaViewModel to reflect completion
                await UpdateSagaFeatureStatus(SelectedTrigger.SagaRefName);
            }
            else
            {
                ActivityLog.Insert(0, $"⚠️ Feature interaction failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Insert(0, $"Error recording feature interaction: {ex.Message}");
        }

        // Clear current entity
        _currentEntityRef = null;
        _currentEntityType = null;
    }

    /// <summary>
    /// Updates a SagaViewModel's feature status after recording a transaction.
    /// </summary>
    private async Task UpdateSagaFeatureStatus(string sagaRef)
    {
        var sagaVM = Sagas.FirstOrDefault(s => s.RefName == sagaRef);
        if (sagaVM != null && CurrentWorld != null)
        {
            var saga = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault(s => s.RefName == sagaRef);
            if (saga != null)
            {
                // Convert Saga GPS to model coordinates for query
                var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.LongitudeX, CurrentWorld);
                var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.LatitudeZ, CurrentWorld);

                // Re-query feature status via CQRS
                var interactions = await _mediator.Send(new QueryInteractionsAtPositionQuery
                {
                    ModelX = sagaModelX,
                    ModelZ = sagaModelZ,
                    Avatar = PlayerAvatar
                });

                var featureInteraction = interactions.FirstOrDefault(i =>
                    i.Type == SagaInteractionType.Feature &&
                    i.SagaRef == sagaRef);

                if (featureInteraction != null)
                {
                    // Update to pre-calculated color based on new status
                    sagaVM.FeatureDotColor = FeatureColors.GetColor(
                        sagaVM.FeatureType, featureInteraction.Status);
                    sagaVM.FeatureDotOpacity = 1.0;
                }
            }
        }
    }

    private bool PlayerHasItem(string itemRef)
    {
        if (PlayerAvatar == null || PlayerAvatar.Capabilities == null) return false;

        // Check consumables
        if (PlayerAvatar.Capabilities.Consumables != null)
        {
            foreach (var entry in PlayerAvatar.Capabilities.Consumables)
            {
                if (entry.ConsumableRef == itemRef && entry.Quantity > 0)
                    return true;
            }
        }

        // Check equipment
        if (PlayerAvatar.Capabilities.Equipment != null)
        {
            foreach (var entry in PlayerAvatar.Capabilities.Equipment)
            {
                if (entry.EquipmentRef == itemRef)
                    return true;
            }
        }

        // Check tools
        if (PlayerAvatar.Capabilities.Tools != null)
        {
            foreach (var entry in PlayerAvatar.Capabilities.Tools)
            {
                if (entry.ToolRef == itemRef)
                    return true;
            }
        }

        return false;
    }

    private async Task RemoveItemFromInventoryAsync(string itemRef)
    {
        if (PlayerAvatar == null || PlayerAvatar.Capabilities == null) return;

        // Remove from consumables
        if (PlayerAvatar.Capabilities.Consumables != null)
        {
            for (int i = PlayerAvatar.Capabilities.Consumables.Length - 1; i >= 0; i--)
            {
                var entry = PlayerAvatar.Capabilities.Consumables[i];
                if (entry.ConsumableRef == itemRef)
                {
                    entry.Quantity--;
                    if (entry.Quantity <= 0)
                    {
                        // Remove empty entry
                        var list = PlayerAvatar.Capabilities.Consumables.ToList();
                        list.RemoveAt(i);
                        PlayerAvatar.Capabilities.Consumables = list.ToArray();
                    }
                    await SavePlayerAvatarAsync();
                    return;
                }
            }
        }

        // Remove from equipment
        if (PlayerAvatar.Capabilities.Equipment != null)
        {
            for (int i = PlayerAvatar.Capabilities.Equipment.Length - 1; i >= 0; i--)
            {
                var entry = PlayerAvatar.Capabilities.Equipment[i];
                if (entry.EquipmentRef == itemRef)
                {
                    var list = PlayerAvatar.Capabilities.Equipment.ToList();
                    list.RemoveAt(i);
                    PlayerAvatar.Capabilities.Equipment = list.ToArray();
                    await SavePlayerAvatarAsync();
                    return;
                }
            }
        }

        // Remove from tools
        if (PlayerAvatar.Capabilities.Tools != null)
        {
            for (int i = PlayerAvatar.Capabilities.Tools.Length - 1; i >= 0; i--)
            {
                var entry = PlayerAvatar.Capabilities.Tools[i];
                if (entry.ToolRef == itemRef)
                {
                    var list = PlayerAvatar.Capabilities.Tools.ToList();
                    list.RemoveAt(i);
                    PlayerAvatar.Capabilities.Tools = list.ToArray();
                    await SavePlayerAvatarAsync();
                    return;
                }
            }
        }
    }

    private void ApplyRewardEffects(RewardEffects effects, List<string> rewards)
    {
        if (PlayerAvatar == null) return;

        if (effects.Health != 0)
        {
            PlayerAvatar.Stats.Health += effects.Health;
            rewards.Add($"{effects.Health:+0;-0} Health");
        }

        if (effects.Stamina != 0)
        {
            PlayerAvatar.Stats.Stamina += effects.Stamina;
            rewards.Add($"{effects.Stamina:+0;-0} Stamina");
        }

        if (effects.Mana != 0)
        {
            PlayerAvatar.Stats.Mana += effects.Mana;
            rewards.Add($"{effects.Mana:+0;-0} Mana");
        }

        if (effects.Experience != 0)
        {
            PlayerAvatar.Stats.Experience += effects.Experience;
            rewards.Add($"{effects.Experience:+0;-0} XP");
        }

        if (effects.Credits != 0)
        {
            PlayerAvatar.Stats.Credits += effects.Credits;
            rewards.Add($"{effects.Credits:+0;-0} {CurrentWorld?.WorldConfiguration?.CurrencyName ?? "Credits"}");
        }
    }


    /// <summary>
    /// DISABLED: Manual character spawning not supported in event-sourced architecture.
    /// Characters should only spawn via Saga triggers (which create CharacterSpawned transactions).
    /// To enable manual spawning, would need to:
    /// 1. Find nearest Saga to avatar
    /// 2. Create CharacterSpawned transaction in that Saga's transaction log
    /// 3. Query SagaState to get the spawned character
    /// </summary>
    [RelayCommand]
    private void SpawnCharacterAtAvatar(string characterRef)
    {
        StatusMessage = "Manual character spawning disabled - characters spawn via Saga triggers only";
        ActivityLog.Insert(0, "⚠️ Manual spawning not supported in event-sourced architecture");
    }

    // WPF-specific method commented out for class library conversion
    // UI layer should use SetAvatarPositionFromPixel instead
    /*
    [RelayCommand]
    private void SetAvatarPositionFromClick(object? parameter)
    {
        if (CurrentWorld?.HeightMapMetadata == null || parameter is not System.Windows.Point clickPoint)
            return;

        try
        {
            // Convert click position to model coordinates immediately
            var clickLatitude = CoordinateConverter.HeightMapPixelYToLatitude(clickPoint.Y, CurrentWorld.HeightMapMetadata);
            var clickLongitude = CoordinateConverter.HeightMapPixelXToLongitude(clickPoint.X, CurrentWorld.HeightMapMetadata);
            var clickModelX = CoordinateConverter.LongitudeToModelX(clickLongitude, CurrentWorld);
            var clickModelZ = CoordinateConverter.LatitudeToModelZ(clickLatitude, CurrentWorld);

            // NOTE: Character click handling removed - triggers only

            // Move avatar to clicked position
            SetAvatarPosition(clickLatitude, clickLongitude, CurrentWorld.HeightMapMetadata);
            StatusMessage = $"Avatar moved to {clickLatitude:F6}°, {clickLongitude:F6}°";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error setting avatar position: {ex.Message}";
        }
    }
    */

    /// <summary>
    /// Sets avatar position from pixel coordinates (for ImGui/non-WPF callers)
    /// </summary>
    public void SetAvatarPositionFromPixels(double pixelX, double pixelY)
    {
        if (CurrentWorld?.HeightMapMetadata == null)
            return;

        try
        {
            // Convert pixel position to lat/lon
            var clickLatitude = CoordinateConverter.HeightMapPixelYToLatitude(pixelY, CurrentWorld.HeightMapMetadata);
            var clickLongitude = CoordinateConverter.HeightMapPixelXToLongitude(pixelX, CurrentWorld.HeightMapMetadata);

            // Move avatar to clicked position
            SetAvatarPosition(clickLatitude, clickLongitude, CurrentWorld.HeightMapMetadata);
            StatusMessage = $"Avatar moved to {clickLatitude:F6}°, {clickLongitude:F6}°";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error setting avatar position: {ex.Message}";
        }
    }

    private async Task<ProximityTriggerViewModel?> FindTriggerAtPoint(double modelX, double modelZ)
    {
        if (CurrentWorld == null)
            return null;

        // Use CQRS to find all interactions at this position
        var interactions = await _mediator.Send(new QueryInteractionsAtPositionQuery
        {
            ModelX = modelX,
            ModelZ = modelZ,
            Avatar = PlayerAvatar
        });

        // Get all trigger interactions at this position
        var triggerInteractions = interactions
            .Where(i => i.Type == SagaInteractionType.SagaTrigger)
            .ToList();

        ProximityTriggerViewModel? triggeredViewModel = null;
        string? hoveredSagaRef = null;

        if (triggerInteractions.Any())
        {
            // Find the matching ViewModels for all intersected triggers
            var intersectedTriggers = triggerInteractions
                .Select(ti => new
                {
                    Interaction = ti,
                    ViewModel = Sagas
                        .SelectMany(s => s.Triggers)
                        .FirstOrDefault(t => t.RefName == ti.SagaTriggerRef && t.SagaRefName == ti.SagaRef)
                })
                .Where(x => x.ViewModel != null)
                .ToList();

            // Pick the smallest (innermost) ring - that's the most specific/restrictive trigger
            var innermost = intersectedTriggers
                .OrderBy(x => x.ViewModel!.EnterRadius)
                .FirstOrDefault();

            if (innermost?.ViewModel != null)
            {
                triggeredViewModel = innermost.ViewModel;
                hoveredSagaRef = triggeredViewModel.SagaRefName;
                triggeredViewModel.IsHovered = true;
                triggeredViewModel.RingOpacity = 0.3; // Hovered opacity (brighter)
            }
        }

        // Update visibility for all triggers based on which Saga is hovered
        foreach (var saga in Sagas)
        {
            foreach (var trigger in saga.Triggers)
            {
                if (saga.RefName == hoveredSagaRef)
                {
                    // Show all triggers for the hovered Saga
                    trigger.IsVisible = true;

                    if (trigger != triggeredViewModel)
                    {
                        // Not the innermost, so dimmer
                        trigger.IsHovered = false;
                        trigger.RingOpacity = 0.15;
                    }
                }
                else
                {
                    // Hide triggers for all other Sagas
                    trigger.IsVisible = false;
                    trigger.IsHovered = false;
                    trigger.RingOpacity = 0.15;
                }
            }
        }

        return triggeredViewModel;
    }

    public async void UpdateMousePosition(double pixelX, double pixelY)
    {
        if (CurrentWorld?.HeightMapMetadata == null)
        {
            HasMousePosition = false;
            return;
        }

        try
        {
            MousePixelX = pixelX;
            MousePixelY = pixelY;
            MouseLatitude = CoordinateConverter.HeightMapPixelYToLatitude(pixelY, CurrentWorld.HeightMapMetadata);
            MouseLongitude = CoordinateConverter.HeightMapPixelXToLongitude(pixelX, CurrentWorld.HeightMapMetadata);
            MouseElevation = SampleElevation((int)pixelX, (int)pixelY);
            HasMousePosition = true;

            // Convert mouse position to model coordinates for hit detection
            var mouseModelX = CoordinateConverter.LongitudeToModelX(MouseLongitude, CurrentWorld);
            var mouseModelZ = CoordinateConverter.LatitudeToModelZ(MouseLatitude, CurrentWorld);

            // Update trigger hover state based on mouse model position
            await FindTriggerAtPoint(mouseModelX, mouseModelZ);
        }
        catch
        {
            HasMousePosition = false;
        }
    }

    private int SampleElevation(int pixelX, int pixelY)
    {
        if (_processedHeightMap == null)
            return 0;

        // Clamp coordinates to valid range
        pixelX = Math.Max(0, Math.Min(_processedHeightMap.Width - 1, pixelX));
        pixelY = Math.Max(0, Math.Min(_processedHeightMap.Height - 1, pixelY));

        return _processedHeightMap.ElevationData[pixelX, pixelY];
    }

    private async Task LoadOrCreateAvatarAsync(World world)
    {
        if (_worldRepository == null)
            return;

        // Try to load existing avatar from database
        var existingAvatar = await _worldRepository.LoadAvatarAsync();

        if (existingAvatar != null)
        {
            // Avatar exists, load it
            PlayerAvatar = existingAvatar;

            // Debug output to verify what was loaded
            var toolCount = PlayerAvatar.Capabilities?.Tools?.Length ?? 0;
            var equipmentCount = PlayerAvatar.Capabilities?.Equipment?.Length ?? 0;
            var consumableCount = PlayerAvatar.Capabilities?.Consumables?.Length ?? 0;
            var health = PlayerAvatar.Stats?.Health ?? 0;
            System.Diagnostics.Debug.WriteLine($"DEBUG LoadAvatar: Loaded avatar with Tools={toolCount}, Equipment={equipmentCount}, Consumables={consumableCount}, Health={health}");

            AvatarInfo.UpdatePlayerAvatar(PlayerAvatar);

            // Replay pending Steam achievements for this avatar
            if (_steamAchievementService != null)
            {
                _steamAchievementService.ReplayAchievementsToSteam(GetAvatarId());
                StatusMessage = "Steam achievements synced";
            }

            StatusMessage = $"Welcome back! Avatar loaded.";
            return;
        }

        // No avatar exists - show archetype selection
        StatusMessage = "Select your character archetype...";

        // Load available archetypes
        AvailableArchetypes.Clear();
        foreach (var archetype in world.Gameplay.AvatarArchetypes ?? [])
        {
            AvailableArchetypes.Add(archetype);
        }

        // Show archetype selection dialog
        var selectedArchetype = await ShowArchetypeSelectionDialogAsync();

        if (selectedArchetype == null)
        {
            StatusMessage = "Avatar creation cancelled";
            return;
        }

        // Create new avatar from archetype
        PlayerAvatar = CreateAvatarFromArchetype(selectedArchetype, world);
        AvatarInfo.UpdatePlayerAvatar(PlayerAvatar);

        // Save to database
        await SavePlayerAvatarAsync();

        StatusMessage = $"Avatar created: {selectedArchetype.DisplayName}";
    }

    private async Task<AvatarArchetype?> ShowArchetypeSelectionDialogAsync()
    {
        // Use appropriate selector based on current mode
        var selector = _imguiArchetypeSelector;
        var currencyName = CurrentWorld?.WorldConfiguration?.CurrencyName;

        return await selector.SelectArchetypeAsync(AvailableArchetypes, currencyName);
    }

    private AvatarEntity CreateAvatarFromArchetype(AvatarArchetype archetype, World world)
    {
        var avatar = new AvatarEntity
        {
            AvatarId = Guid.NewGuid(), // Generate unique ID for this avatar
            ArchetypeRef = archetype.RefName,
            PlayTimeHours = 0,
            BlocksPlaced = 0,
            BlocksDestroyed = 0,
            DistanceTraveled = 0,
            X = 0,
            Y = 100,
            Z = 0
        };

        // Use AvatarSpawner to initialize from archetype
        AvatarSpawner.SpawnFromModelAvatar(
            avatar,
            archetype);

        return avatar;
    }

    /// <summary>
    /// Gets the current avatar ID as a string.
    /// </summary>
    private string GetAvatarId()
    {
        return PlayerAvatar?.AvatarId.ToString() ?? Guid.Empty.ToString();
    }


    /// <summary>
    /// Saves the player avatar to the database.
    /// </summary>
    public async Task SavePlayerAvatarAsync()
    {
        if (PlayerAvatar == null || _worldRepository == null)
            return;

        try
        {
            // Debug output to verify avatar state before saving
            var toolCount = PlayerAvatar.Capabilities?.Tools?.Length ?? 0;
            var equipmentCount = PlayerAvatar.Capabilities?.Equipment?.Length ?? 0;
            var consumableCount = PlayerAvatar.Capabilities?.Consumables?.Length ?? 0;
            var health = PlayerAvatar.Stats?.Health ?? 0;

            System.Diagnostics.Debug.WriteLine($"DEBUG SavePlayerAvatar: Tools={toolCount}, Equipment={equipmentCount}, Consumables={consumableCount}, Health={health}");

            await _worldRepository.SaveAvatarAsync(PlayerAvatar);

            System.Diagnostics.Debug.WriteLine($"DEBUG SavePlayerAvatar: Avatar saved successfully");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving avatar: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"DEBUG SavePlayerAvatar ERROR: {ex}");
        }
    }

    /// <summary>
    /// Notify UI that PlayerAvatar has changed (for nested property updates like Stats, Capabilities).
    /// </summary>
    public void NotifyPlayerAvatarChanged()
    {
        OnPropertyChanged(nameof(PlayerAvatar));
        AvatarInfo.UpdatePlayerAvatar(PlayerAvatar);
        MerchantTrade?.RefreshCategories();
        QuestLog?.RefreshQuests();
        Achievements?.RefreshAchievements();
    }

    [RelayCommand]
    private void ViewBlocks()
    {
        throw new NotImplementedException();
        //if (CurrentWorld?.Simulation?.Blocks?.BlockList == null || CurrentWorld.Simulation.Blocks.BlockList.Length == 0)
        //{
        //    StatusMessage = "No blocks available";
        //    return;
        //}

        //// Pass Block[] directly to the dialog to show world catalog information
        //var blocks = CurrentWorld.Simulation.Blocks.BlockList
        //    .Where(b => b != null)
        //    .ToArray();

        ////var dialog = new Views.BlocksInventoryDialog(blocks);
        ////dialog.ShowDialog();
    }

    [RelayCommand]
    private void ViewTools()
    {
        if (CurrentWorld?.Gameplay?.Tools == null || CurrentWorld.Gameplay.Tools.Length == 0)
        {
            StatusMessage = "No tools available";
            return;
        }

        //var dialog = new Views.ToolsInventoryDialog(CurrentWorld.Gameplay.Tools);
        //dialog.ShowDialog();
    }

    [RelayCommand]
    private void ViewMaterials()
    {
        if (CurrentWorld?.Gameplay?.BuildingMaterials == null || CurrentWorld.Gameplay.BuildingMaterials.Length == 0)
        {
            StatusMessage = "No materials available";
            return;
        }

        //var dialog = new Views.MaterialsInventoryDialog(CurrentWorld.Gameplay.BuildingMaterials);
        //dialog.ShowDialog();
    }

    [RelayCommand]
    private void ViewEquipment()
    {
        if (CurrentWorld?.Gameplay?.Equipment == null || CurrentWorld.Gameplay.Equipment.Length == 0)
        {
            StatusMessage = "No equipment available";
            return;
        }

        //var dialog = new Views.EquipmentInventoryDialog(CurrentWorld.Gameplay.Equipment);
        //dialog.ShowDialog();
    }

    [RelayCommand]
    private void ViewConsumables()
    {
        if (CurrentWorld?.Gameplay?.Consumables == null || CurrentWorld.Gameplay.Consumables.Length == 0)
        {
            StatusMessage = "No consumables available";
            return;
        }

        //var dialog = new Views.ConsumablesInventoryDialog(CurrentWorld.Gameplay.Consumables);
        //dialog.ShowDialog();
    }

    [RelayCommand]
    private void ViewSpells()
    {
        if (CurrentWorld?.Gameplay?.Spells == null || CurrentWorld.Gameplay.Spells.Length == 0)
        {
            StatusMessage = "No spells available";
            return;
        }

        //var dialog = new Views.SpellsInventoryDialog(CurrentWorld.Gameplay.Spells);
        //dialog.ShowDialog();
    }

    [RelayCommand]
    private void ViewAchievementsCatalog()
    {
        if (CurrentWorld?.Gameplay?.Achievements == null || CurrentWorld.Gameplay.Achievements.Length == 0)
        {
            StatusMessage = "No achievements available";
            return;
        }

        //var dialog = new Views.AchievementsInventoryDialog(CurrentWorld.Gameplay.Achievements);
        //dialog.ShowDialog();
    }

    [RelayCommand]
    private void ViewCharacters()
    {
        if (CurrentWorld?.Gameplay?.Characters == null || CurrentWorld.Gameplay.Characters.Length == 0)
        {
            StatusMessage = "No characters available";
            return;
        }

        //// Show dialog with characters - allow selecting one to spawn
        //var dialog = new Views.CharactersDialog(CurrentWorld.Gameplay.Characters);
        //if (dialog.ShowDialog() == true && dialog.SelectedCharacter != null)
        //{
        //    // Spawn the selected character at avatar position
        //    SpawnCharacterAtAvatar(dialog.SelectedCharacter.RefName);
        //}
    }

    /// <summary>
    /// Handles clicking on a spawned character marker to open interaction window.
    /// </summary>
    [RelayCommand]
    private void InteractWithCharacter(CharacterLocationViewModel? characterVM)
    {
        if (characterVM == null || CurrentWorld == null)
            return;

        // Look up the character template
        var character = CurrentWorld.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == characterVM.RefName);
        if (character == null)
        {
            StatusMessage = $"Character template not found: {characterVM.RefName}";
            return;
        }

        // Set as triggered character
        TriggeredCharacter = character;
        TriggeredCharacterInstanceId = characterVM.CharacterInstanceId;

        // Update merchant trade role
        MerchantTrade.RefreshCategories();

        // Notify character changed
        OnCharacterChanged();

        // WPF WINDOW CODE - TO BE DELETED WITH XAML
        // Open interaction window
        //ShowSagaInteractionWindow();

        ActivityLog.Insert(0, $"💬 Interacting with {character.DisplayName}");
    }

    [RelayCommand]
    private void TestSteamAchievement()
    {
        const string testAchievementId = "ACH_WIN_ONE_GAME";

        if (_steamAchievementService == null)
        {
            StatusMessage = "Steam service not available";
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[Steam Test] ========== TESTING ACHIEVEMENT ==========");
            System.Diagnostics.Debug.WriteLine($"[Steam Test] Achievement ID: {testAchievementId}");

            // Set the achievement
            var (setSuccess, setError) = _steamAchievementService.SetSteamAchievementDirect(testAchievementId);

            if (!setSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"[Steam Test] ❌ FAILED to set achievement: {setError}");
                StatusMessage = $"Failed to set achievement: {setError}";
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Steam Test] ✓ SetAchievement() returned SUCCESS");
            System.Diagnostics.Debug.WriteLine($"[Steam Test] ✓ StoreStats() called - changes committed to Steam");

            // Request fresh stats from Steam to see the updated achievement status
            System.Diagnostics.Debug.WriteLine($"[Steam Test] Requesting fresh stats from Steam...");
            bool requested = Steamworks.SteamUserStats.RequestCurrentStats();
            System.Diagnostics.Debug.WriteLine($"[Steam Test] RequestCurrentStats() returned: {requested}");
            System.Diagnostics.Debug.WriteLine($"[Steam Test] (Watch for [Steam] Stats received callback with updated status)");
            System.Diagnostics.Debug.WriteLine($"[Steam Test] ========================================");

            StatusMessage = $"Achievement {testAchievementId} set - waiting for Steam callback...";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Steam Test] ❌ EXCEPTION: {ex.Message}");
            StatusMessage = $"Test exception: {ex.Message}";
        }
    }

    private void OnCharacterChanged()
    {
        CharacterChanged?.Invoke(this, EventArgs.Empty);
    }

    // WPF WINDOW CODE - TO BE DELETED WITH XAML
    //private SagaInteractionWindow? _interactionWindow;
    //private bool _isInteractionWindowOpen = false;

    private void UpdateSagaInteractionContext()
    {
        if (CurrentWorld == null || PlayerAvatar == null) return;

        // Update the context fields for CQRS commands
        _sagaInteractionContext.World = CurrentWorld;
        _sagaInteractionContext.AvatarEntity = PlayerAvatar;
        _sagaInteractionContext.AvatarId = PlayerAvatar.AvatarId;
        _sagaInteractionContext.ActiveCharacter = TriggeredCharacter;
        _sagaInteractionContext.CurrentSagaRef = _currentSagaRef;
        _sagaInteractionContext.CurrentCharacterInstanceId = TriggeredCharacterInstanceId != Guid.Empty ? TriggeredCharacterInstanceId : null;

        // Refresh merchant trade
        MerchantTrade.RefreshCategories();
    }

    // WPF WINDOW CODE - TO BE DELETED WITH XAML
    /*public async void ShowSagaInteractionWindow()
    {
        // Close existing window if open
        _interactionWindow?.Close();

        // Update merchant trade context with current data
        UpdateSagaInteractionContext();

        // Create ViewModel for the window
        var locationViewModel = new SagaInteractionWindowViewModel(_sagaInteractionContext)
        {
            TriggeredCharacter = TriggeredCharacter,
            ActivityLog = ActivityLog,
            MerchantTradeViewModel = MerchantTrade
        };

        // Create boss battle view model if character is a boss (determined by dialogue tree analysis)
        var isBossCharacter = TriggeredCharacter != null && CurrentWorld != null &&
                              DialogueTreeAnalyzer.GetInteractionType(TriggeredCharacter, CurrentWorld) == DialogueInteractionType.Boss;

        if (isBossCharacter && PlayerAvatar != null && _worldRepository != null)
        {
            var bossBattle = new BossBattleViewModel();

            // NOTE: Character state queries removed - always assume boss is alive for now
            // TODO: Use GetAvailableInteractionsQuery to check character state when implementing proper character rendering
            bool isBossAlive = true;

            bossBattle.Initialize(TriggeredCharacter, PlayerAvatar, isBossAlive, CurrentWorld!);

            // Subscribe to boss defeated event to handle persistence
            bossBattle.BossDefeated += async (sender, args) =>
            {
                // CQRS: Create CharacterDefeated transaction via command
                if (SelectedTrigger != null && PlayerAvatar != null)
                {
                    try
                    {
                        var command = new DefeatCharacterCommand
                        {
                            AvatarId = PlayerAvatar.AvatarId,
                            SagaRef = SelectedTrigger.SagaRefName,
                            CharacterInstanceId = TriggeredCharacterInstanceId
                        };

                        var result = await _mediator.Send(command);

                        if (result.Successful)
                        {
                            ActivityLog.Insert(0, $"⚔️ Boss defeated: {args.BossRefName} [CQRS]");
                        }
                        else
                        {
                            ActivityLog.Insert(0, $"⚠️ Failed to record boss defeat: {result.ErrorMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        ActivityLog.Insert(0, $"Error recording boss defeat: {ex.Message}");
                    }
                }

                // Save avatar (inventory may have changed from loot)
                await SavePlayerAvatarAsync();

                // Trigger UI update to show loot
                locationViewModel.OnBossDefeated();
            };

            locationViewModel.BossBattleViewModel = bossBattle;
        }

        // Create quest signpost view model if this is a quest signpost
        if (SelectedTrigger != null && CurrentWorld != null && PlayerAvatar != null)
        {
            // Look up the Saga to check if it has a QuestSignpost reference
            var saga = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault(p =>
                p.RefName == SelectedTrigger.SagaRefName);

            if (saga != null && saga.ItemElementName == ItemChoiceType2.QuestSignpostRef && !string.IsNullOrEmpty(saga.Item))
            {
                var questSignpost = CurrentWorld.TryGetQuestSignpostByRefName(saga.Item);
                if (questSignpost != null)
                {
                    var questSignpostVM = new QuestSignpostViewModel(_sagaInteractionContext);
                    questSignpostVM.LoadQuestSignpost(questSignpost);

                    // Subscribe to quest accepted event
                    questSignpostVM.QuestAccepted += async (sender, args) =>
                    {
                        // Save avatar with new quest
                        await SavePlayerAvatarAsync();
                        NotifyPlayerAvatarChanged();


                        ActivityLog.Insert(0, $"Quest accepted: {args.QuestRef}");

                        // Refresh quest signpost status
                        locationViewModel.OnQuestAccepted();
                    };

                    locationViewModel.QuestSignpostViewModel = questSignpostVM;
                }
                else
                {
                    ActivityLog.Insert(0, $"Warning: QuestSignpost '{saga.Item}' not found");
                }
            }
        }

        // Determine feature type from Saga (triggers don't have types, only features do)
        if (SelectedTrigger != null && CurrentWorld != null)
        {
            var saga = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault(s => s.RefName == SelectedTrigger.SagaRefName);
            if (saga != null)
            {
                FeatureType? featureType = null;
                if (saga.ItemElementName == ItemChoiceType2.LandmarkRef)
                    featureType = FeatureType.Landmark;
                else if (saga.ItemElementName == ItemChoiceType2.StructureRef)
                    featureType = FeatureType.Structure;
                else if (saga.ItemElementName == ItemChoiceType2.QuestSignpostRef)
                    featureType = FeatureType.QuestSignpost;

                locationViewModel.FeatureType = featureType;
            }
        }

        // Dialogue is automatically initialized by OnTriggeredCharacterChanged in the ViewModel

        // Mark window as open
        _isInteractionWindowOpen = true;

        // Create new window with SagaInteractionWindowViewModel
        _interactionWindow = new SagaInteractionWindow
        {
            DataContext = locationViewModel,
            Owner = System.Windows.Application.Current.MainWindow
        };

        // Handle window closed
        _interactionWindow.Closed += (s, e) =>
        {
            _isInteractionWindowOpen = false;
        };

        // Show as modal dialog
        _interactionWindow.ShowDialog();

        // Clean up reference when closed
        _interactionWindow = null;
        _isInteractionWindowOpen = false;
    }*/

    // WPF WINDOW CODE - TO BE DELETED WITH XAML
    /*[RelayCommand]
    private void OpenEquipmentManager()
    {
        if (PlayerAvatar == null || CurrentWorld == null)
        {
            StatusMessage = "No avatar or world loaded";
            return;
        }

        var equipmentVM = new Ambient.Presentation.WindowsUI.RpgControls.ViewModels.EquipmentManagementViewModel();
        equipmentVM.Initialize(PlayerAvatar, CurrentWorld, _playerEquippedItems);

        // Subscribe to equipment changes
        equipmentVM.EquipmentChanged += OnEquipmentChanged;

        var window = new Ambient.Presentation.WindowsUI.RpgControls.Views.Equipment.EquipmentWindow(equipmentVM)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        window.ShowDialog();

        // Unsubscribe when window closes
        equipmentVM.EquipmentChanged -= OnEquipmentChanged;
    }*/

}