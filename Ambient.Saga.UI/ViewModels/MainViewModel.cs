using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Infrastructure.GameLogic;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Loading;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Domain.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.ObjectModel;
using Ambient.Saga.UI.Models;
using Ambient.Saga.UI.Services;
using Ambient.Saga.UI.ViewModels;

namespace Ambient.Saga.Presentation.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly string _dataDirectory;
    private readonly string _schemaDirectory;

    [ObservableProperty]
    private ObservableCollection<IWorldConfiguration> _availableConfigurations = new();

    [ObservableProperty]
    private IWorldConfiguration? _selectedConfiguration;

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
    private IWorld _currentWorld;

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
    private IWorldStateRepository _worldRepository;
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
        _dataDirectory = Path.Combine(baseDirectory, "Content", "Worlds");
        _schemaDirectory = Path.Combine(baseDirectory, "Content", "Schemas");

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
        Interactable? interactable = null;

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

 

    private DialogueViewModel? _currentDialogueViewModel;

    

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

            // Initialize world bootstrapper (required when loading via mediator)
            WorldBootstrapper.Initialize(world);

            // Complete initialization with shared logic
            await InitializeWorldCoreAsync(world, _dataDirectory);

            StatusMessage = $"Loaded: {SelectedConfiguration.RefName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading configuration: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

        /// <summary>
    /// Shared initialization logic for both LoadSelectedConfigurationAsync and InitializeWithExternalWorldAsync.
    /// Sets up world, database, avatar, height map, Sagas, and triggers.
    /// </summary>
    private async Task InitializeWorldCoreAsync(IWorld world, string dataDirectory)
    {
        // Set the world
        CurrentWorld = world;

        // Update AvatarInfo with world data
        AvatarInfo.UpdateWorld(world);

        // Initialize LiteDB persistence and CQRS providers for this world
        InitializeWorldDatabase(world);

        // Load or create avatar (shows archetype selection dialog for new worlds)
        await LoadOrCreateAvatarAsync(world);

        // Load and display height map if available
        await LoadHeightMapImageInternalAsync(world, dataDirectory);

        // Load Sagas and triggers from world with feature status
        var (sagas, triggers) = SagaViewModel.LoadFromWorld(world, PlayerAvatar, _worldRepository);
        Sagas.Clear();
        AllTriggers.Clear();
        foreach (var saga in sagas) Sagas.Add(saga);
        foreach (var trigger in triggers) AllTriggers.Add(trigger);

        // Initialize avatar position at spawn if available
        InitializeAvatarPosition(world);
    }

    /// <summary>
    /// Initializes MainViewModel with an externally-loaded world.
    /// Use this when the world has already been loaded by the game (e.g., via WorldRepository)
    /// instead of through LoadSelectedConfigurationAsync.
    /// Avatar is loaded from database or created via archetype selection dialog.
    /// </summary>
    /// <param name="world">The already-loaded world instance (WorldBootstrapper.Initialize should already have been called)</param>
    /// <param name="dataDirectory">Base directory containing world definition files (for height map loading)</param>
    public async Task InitializeWithExternalWorldAsync(IWorld world, string dataDirectory)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));

        try
        {
            IsLoading = true;
            StatusMessage = $"Initializing world: {world.WorldConfiguration?.RefName ?? "Unknown"}...";

            // Complete initialization with shared logic
            await InitializeWorldCoreAsync(world, dataDirectory);

            StatusMessage = $"Initialized: {world.WorldConfiguration?.RefName ?? "World"}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error initializing world: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] InitializeWithExternalWorldAsync error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadHeightMapImageInternalAsync(IWorld world, string dataDirectory)
    {
        // Clear previous image
        HeightMapImage = null;
        HeightMapInfo = null;
        AvatarInfo.UpdateHeightMapStatus(false);

        // Check if this world has height map settings
        if (world.IsProcedural)
        {
            // Procedural worlds don't have height maps
            HeightMapInfo = "This world uses procedural settings (no height map)";
            return;
        }

        try
        {
            var heightMapPath = Path.Combine(dataDirectory, world.WorldConfiguration.HeightMapSettings.RelativePath);
            
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

    private void InitializeWorldDatabase(IWorld world)
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

    private void InitializeAvatarPosition(IWorld world)
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
            featureType = sagaFeature.Type switch
            {
                SagaFeatureType.Landmark => FeatureType.Landmark,
                SagaFeatureType.Structure => FeatureType.Structure,
                SagaFeatureType.Quest => FeatureType.QuestSignpost,
                SagaFeatureType.ResourceNode => FeatureType.ResourceNode,
                SagaFeatureType.Teleporter => FeatureType.Teleporter,
                SagaFeatureType.Vendor => FeatureType.Vendor,
                SagaFeatureType.CraftingStation => FeatureType.CraftingStation,
                _ => FeatureType.Structure
            };
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
                switch (feature.Type)
                {
                    case SagaFeatureType.Landmark:
                        // Show lore content
                        if (!string.IsNullOrEmpty(feature.Interactable?.Content))
                        {
                            lootCharacter.Description = feature.Interactable.Content;
                            ActivityLog.Insert(0, $"📜 {feature.Interactable.Content}");
                        }
                        effects = feature.Interactable?.Effects;
                        break;

                    case SagaFeatureType.Structure:
                    case SagaFeatureType.ResourceNode:
                        // Structures and resource nodes can have loot
                        loot = feature.Interactable?.Loot;
                        effects = feature.Interactable?.Effects;
                        break;

                    case SagaFeatureType.Vendor:
                        // Vendors have items to trade
                        loot = feature.Interactable?.Loot;
                        effects = feature.Interactable?.Effects;
                        break;

                    case SagaFeatureType.CraftingStation:
                        // Crafting stations show available recipes
                        if (!string.IsNullOrEmpty(feature.Interactable?.Content))
                        {
                            lootCharacter.Description = feature.Interactable.Content;
                        }
                        effects = feature.Interactable?.Effects;
                        break;

                    case SagaFeatureType.Teleporter:
                        // Teleporters show destination info
                        if (!string.IsNullOrEmpty(feature.Interactable?.Content))
                        {
                            lootCharacter.Description = feature.Interactable.Content;
                        }
                        effects = feature.Interactable?.Effects;
                        break;

                    default:
                        // Default: treat like structure
                        loot = feature.Interactable?.Loot;
                        effects = feature.Interactable?.Effects;
                        break;
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
        // Set _currentEntityType based on saga feature type
        if (sagaFeature != null)
        {
            _currentEntityType = sagaFeature.Type switch
            {
                SagaFeatureType.Landmark => FeatureType.Landmark,
                SagaFeatureType.Structure => FeatureType.Structure,
                SagaFeatureType.Quest => FeatureType.QuestSignpost,
                SagaFeatureType.ResourceNode => FeatureType.ResourceNode,
                SagaFeatureType.Teleporter => FeatureType.Teleporter,
                SagaFeatureType.Vendor => FeatureType.Vendor,
                SagaFeatureType.CraftingStation => FeatureType.CraftingStation,
                _ => FeatureType.Structure
            };
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

    private async Task LoadOrCreateAvatarAsync(IWorld world)
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

    private AvatarEntity CreateAvatarFromArchetype(AvatarArchetype archetype, IWorld world)
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


}