using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Domain.Sampling;
using Ambient.Domain.ValueObjects;
using Ambient.Infrastructure.GameLogic;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Loading;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Domain.Services;
using Ambient.Saga.UI.Components.Panels;
using Ambient.Saga.UI.Models;
using Ambient.Saga.UI.Services;
using Ambient.Saga.UI.ViewModels;
using Ambient.Saga.UI.Components.Overlay;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Input;
using SharpDX;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.ObjectModel;
using static Ambient.Saga.UI.Services.HeightMapProcessor;
using SpawnDevCharacterCommand = Ambient.Saga.Engine.Application.Commands.Saga.SpawnDevCharacterCommand;

namespace Ambient.Saga.Presentation.UI.ViewModels;

public partial class SagaMainViewModel : ObservableObject
{
    private readonly string _dataDirectory;
    private readonly string _schemaDirectory;

    [ObservableProperty]
    private ObservableCollection<IWorldConfiguration> _availableConfigurations = new();

    [ObservableProperty]
    private IWorldConfiguration? _selectedConfiguration;


    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private HeightMapImageData? _heightMapImage;

    [ObservableProperty]
    private GeoTiffMetadata? _heightMapMetadata;

    [ObservableProperty]
    private string? _heightMapInfo;

    [ObservableProperty]
    private double zoomFactor = 1.0;

    [ObservableProperty]
    private ObservableCollection<SagaArcViewModel> _sagas = new();

    [ObservableProperty]
    private ObservableCollection<ProximityTriggerViewModel> _allTriggers = new();

    [ObservableProperty]
    private ObservableCollection<CharacterViewModel> _characters = new();

    /// <summary>
    /// Recent transactions from all active saga instances.
    /// Populated during background processing for Journal History tab.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog.SagaTransaction> _recentTransactions = new();

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
    private bool _isReadyForSagaProcessing;

    [ObservableProperty]
    private bool _shouldCenterOnAvatar;

    /// <summary>
    /// Primary HUD text extension slot (renders in top-right).
    /// Set by game layer - typically navigation, weather, world info.
    /// </summary>
    [ObservableProperty]
    private string _hudText1 = string.Empty;

    /// <summary>
    /// Secondary HUD text extension slot (renders in top-right, below HudText1).
    /// Set by game layer - typically debug info, FPS, metrics.
    /// </summary>
    [ObservableProperty]
    private string _hudText2 = string.Empty;

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
    private bool _hasElevationData;

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

    /// <summary>
    /// Queue of pending toast messages for the message overlay.
    /// GameplayOverlay drains this queue each frame.
    /// </summary>
    private readonly ConcurrentQueue<(string Text, MessageType Type, float Duration)> _pendingToastMessages = new();

    /// <summary>
    /// Add a toast message to be displayed by the message overlay.
    /// </summary>
    /// <param name="text">Message text</param>
    /// <param name="type">Message type for styling (default: Info)</param>
    /// <param name="duration">Display duration in seconds (default: 3)</param>
    public void AddToastMessage(string text, MessageType type = MessageType.Info, float duration = 3f)
    {
        _pendingToastMessages.Enqueue((text, type, duration));
    }

    /// <summary>
    /// Drain all pending toast messages. Called by GameplayOverlay each frame.
    /// Also drains messages from PlayerAvatar.PendingMessages if avatar exists.
    /// </summary>
    /// <returns>List of pending messages (empty if none)</returns>
    public IEnumerable<(string Text, MessageType Type, float Duration)> DrainToastMessages()
    {
        // Drain messages from the local queue (AddToastMessage calls)
        while (_pendingToastMessages.TryDequeue(out var message))
        {
            yield return message;
        }

        // Also drain messages from the avatar's PendingMessages queue
        if (PlayerAvatar?.PendingMessages != null)
        {
            while (PlayerAvatar.PendingMessages.TryTake(out var notification))
            {
                var text = !string.IsNullOrEmpty(notification.SourceDisplayName)
                    ? $"{notification.SourceDisplayName}: {notification.Message}"
                    : notification.Message ?? string.Empty;

                // Map NotificationSeverity to MessageType
                var type = notification.Severity switch
                {
                    NotificationSeverity.Warning => MessageType.Warning,
                    NotificationSeverity.Error => MessageType.Error,
                    NotificationSeverity.Combat => MessageType.Combat,
                    NotificationSeverity.Quest => MessageType.Quest,
                    NotificationSeverity.Loot => MessageType.Loot,
                    _ => MessageType.Info
                };

                yield return (text, type, notification.Duration);
            }
        }
    }

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

    // Event for when quit is requested (e.g., from world selection screen)
    public event Action? RequestQuit;

    // Event for when dialogue should be displayed (for character interactions)
    public event Action<CharacterViewModel>? DialogueRequested;

    // Event for when session is ready (avatar loaded and height map available)
    public event Action<AvatarEntity, ElevationWaterMap?>? SessionReady;

    // Event for when world definition is loaded (before avatar selection)
    // Provides worldRef (for avatar/database lookup) and worldPath (where the XML definition resides on disk)
    public event Action<string, string>? WorldLoaded;

    // Event for when avatar teleport is requested (e.g., map click)
    public event Action<double, double>? AvatarTeleportRequested;

    // Awaitable signal for when world configurations have been discovered
    private readonly TaskCompletionSource<int> _configurationsLoadedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Returns a Task that completes when configurations have been loaded, with the count as result.
    /// Safe to call multiple times; returns the same completed task after first load.
    /// </summary>
    public Task<int> WaitForConfigurationsAsync() => _configurationsLoadedTcs.Task;

    /// <summary>
    /// Requests the application to quit.
    /// Called by modals/screens when user wants to exit without completing mandatory actions.
    /// </summary>
    public void RaiseRequestQuit()
    {
        RequestQuit?.Invoke();
    }

    private ProximityTriggerViewModel? _previousTrigger;
    private IDisposable? _worldDatabase;
    private IWorldStateRepository _worldRepository;
    private ISteamAchievementService? _steamAchievementService;
    private ProcessedHeightMap? _processedHeightMap;

    // Background processing for interaction checks (runs off the UI thread)
    private CancellationTokenSource? _backgroundProcessingCts;
    private Task? _backgroundProcessingTask;
    private const int InteractionCheckIntervalMs = 5000;

    // Track current entity being looted (for recording triggers)
    private string? _currentEntityRef;
    private SagaArcCategory? _currentEntityCategory;

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
    private readonly IContentPathResolver _contentPathResolver;
    private readonly MediatR.IMediator _mediator;
    private readonly IWorldContentGenerator _worldContentGenerator;
    //private readonly Services.IArchetypeSelector _wpfArchetypeSelector;
    private readonly IArchetypeSelector _imguiArchetypeSelector;
    private readonly IAvatarCreationService _avatarCreationService;
    //private bool _useImGuiMode = false;

    // Public accessor for mediator (used by modals)
    public MediatR.IMediator Mediator => _mediator;

    public SagaMainViewModel(
        WorldProvider worldProvider,
        SagaInstanceRepositoryProvider repositoryProvider,
        GameAvatarRepositoryProvider avatarRepositoryProvider,
        WorldStateRepositoryProvider worldStateRepositoryProvider,
        IWorldRepositoryFactory repositoryFactory,
        IContentPathResolver contentPathResolver,
        MediatR.IMediator mediator,
        IWorldContentGenerator worldContentGenerator,
        IAvatarCreationService avatarCreationService,
        [Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute("imgui")] IArchetypeSelector imguiArchetypeSelector)
    {
        _worldProvider = worldProvider ?? throw new ArgumentNullException(nameof(worldProvider));
        _repositoryProvider = repositoryProvider ?? throw new ArgumentNullException(nameof(repositoryProvider));
        _avatarRepositoryProvider = avatarRepositoryProvider ?? throw new ArgumentNullException(nameof(avatarRepositoryProvider));
        _worldStateRepositoryProvider = worldStateRepositoryProvider ?? throw new ArgumentNullException(nameof(worldStateRepositoryProvider));
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _contentPathResolver = contentPathResolver ?? throw new ArgumentNullException(nameof(contentPathResolver));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _worldContentGenerator = worldContentGenerator ?? throw new ArgumentNullException(nameof(worldContentGenerator));
        _avatarCreationService = avatarCreationService ?? throw new ArgumentNullException(nameof(avatarCreationService));
        //_wpfArchetypeSelector = wpfArchetypeSelector ?? throw new ArgumentNullException(nameof(wpfArchetypeSelector));
        _imguiArchetypeSelector = imguiArchetypeSelector ?? throw new ArgumentNullException(nameof(imguiArchetypeSelector));

        // Set up directories similar to the unit tests
        var baseDirectory = AppContext.BaseDirectory;
        _dataDirectory = Path.Combine(baseDirectory, "Content", "Worlds");
        _schemaDirectory = Path.Combine(baseDirectory, "Content", "xsd");

        // Initialize merchant trade view model with the context
        MerchantTrade = new MerchantTradeViewModel(_sagaInteractionContext, _mediator);

        // Subscribe to merchant trade events
        MerchantTrade.StatusMessageChanged += (sender, message) => AddToastMessage(message, MessageType.Info, 3f);
        MerchantTrade.ActivityMessageGenerated += (sender, message) =>
        {
            ActivityLog.Insert(0, message);
            AddToastMessage(message, MessageType.Loot, 3f);
        };

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
    /// Note: Heavy processing like interaction checks run on background thread.
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

        // Note: Interaction checks moved to background thread - see StartBackgroundProcessing()
    }

    private double _respawnCheckAccumulator = 0;

    /// <summary>
    /// Starts background processing for interaction checks.
    /// Call this after world is loaded.
    /// </summary>
    public void StartBackgroundProcessing()
    {
        StopBackgroundProcessing();

        _backgroundProcessingCts = new CancellationTokenSource();
        _backgroundProcessingTask = Task.Run(async () => await BackgroundProcessingLoopAsync(_backgroundProcessingCts.Token));
    }

    /// <summary>
    /// Stops background processing.
    /// Call this when world is unloaded or application is closing.
    /// </summary>
    public void StopBackgroundProcessing()
    {
        if (_backgroundProcessingCts != null)
        {
            _backgroundProcessingCts.Cancel();
            _backgroundProcessingCts.Dispose();
            _backgroundProcessingCts = null;
        }
        _backgroundProcessingTask = null;
    }

    private async Task BackgroundProcessingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAvailableInteractionsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackgroundProcessing] Error: {ex.Message}");
            }

            try
            {
                await Task.Delay(InteractionCheckIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

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
        //return;
        if (PlayerAvatar == null || CurrentWorld == null || !HasAvatarPosition)
            return;

        if (CurrentWorld.HeightMapMetadata == null)
            return; // Can't convert to pixel coords without metadata

        try
        {
            // Clear previous character list
            Characters.Clear();

            // Get avatar model coordinates for proximity filtering
            var avatarModelX = CoordinateConverter.LongitudeToModelX(AvatarLongitude, CurrentWorld);
            var avatarModelZ = CoordinateConverter.LatitudeToModelZ(AvatarLatitude, CurrentWorld);

            // Get nearby SagaArc RefNames for efficient filtering
            var nearbySagaRefs = SagaProximityService.FilterSagaArcsByProximity(
                avatarModelX, avatarModelZ, CurrentWorld)
                .Select(s => s.SagaArc.RefName)
                .ToHashSet();

            //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Found {nearbySagaRefs.Count} nearby Sagas (of {_sagas.Count} total)");

            // Query spawned characters for ALL nearby Sagas (not just visible ones)
            // Characters should always be visible regardless of saga visibility
            foreach (var sagaRef in nearbySagaRefs)
            {
                // Get Saga template for center coordinates
                if (!CurrentWorld.SagaArcLookup.TryGetValue(sagaRef, out var sagaTemplate))
                    continue;

                var query = new GetSpawnedCharactersQuery
                {
                    AvatarId = PlayerAvatar.AvatarId,
                    SagaRef = sagaRef,
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
                        sagaTemplate.Longitude,
                        CurrentWorld);
                    var worldLat = CoordinateConverter.SagaRelativeZToLatitude(
                        characterState.CurrentLatitudeZ,
                        sagaTemplate.Latitude,
                        CurrentWorld);

                    //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Converted to world GPS: ({worldLon:F6}, {worldLat:F6})");

                    // Convert GPS to pixel coordinates (for map display)
                    var pixelX = CoordinateConverter.HeightMapLongitudeToPixelX(
                        worldLon,
                        CurrentWorld.HeightMapMetadata);
                    var pixelY = CoordinateConverter.HeightMapLatitudeToPixelY(
                        worldLat,
                        CurrentWorld.HeightMapMetadata);

                    // Convert GPS to model/world coordinates (for 3D game engines)
                    var modelX = CoordinateConverter.LongitudeToModelX(worldLon, CurrentWorld);
                    var modelZ = CoordinateConverter.LatitudeToModelZ(worldLat, CurrentWorld);
                    var modelY = characterState.CurrentY; // Y elevation from character state

                    //System.Diagnostics.Debug.WriteLine($"[CheckInteractions] Character '{characterTemplate.DisplayName}' pixel coords: ({pixelX:F0}, {pixelY:F0})");

                    var characterVm = new CharacterViewModel
                    {
                        CharacterInstanceId = characterState.CharacterInstanceId,
                        CharacterRef = characterState.CharacterRef,
                        DisplayName = characterTemplate.DisplayName,
                        CharacterType = "Character", // Could determine from context
                        // Map display coordinates
                        PixelX = pixelX,
                        PixelY = pixelY,
                        // GPS world coordinates
                        Latitude = worldLat,
                        Longitude = worldLon,
                        Elevation = characterState.CurrentY,
                        // 3D model coordinates
                        ModelX = modelX,
                        ModelY = modelY,
                        ModelZ = modelZ,
                        // Character state
                        IsAlive = characterState.IsAlive,
                        CanDialogue = true, // Sandbox - assume all interactions available
                        CanTrade = true,
                        CanAttack = characterState.IsAlive,
                        SagaRef = sagaRef
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
                var availability = choice.IsAvailable ? "[x]" : "[ ]";
                System.Diagnostics.Debug.WriteLine($"      [{availability}] {choice.Text} -> {choice.ChoiceId}");
            }

            // Find the matching CharacterViewModel from Characters collection
            var characterVM = Characters.FirstOrDefault(c =>
                c.CharacterInstanceId == characterInstanceId && c.SagaRef == sagaRef);

            if (characterVM != null)
            {
                // Raise event for UI layer to open the dialogue modal
                DialogueRequested?.Invoke(characterVM);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"*** WARNING: CharacterViewModel not found for {characterInstanceId} in Saga {sagaRef}");
            }
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
            AddToastMessage("Loading available configurations...", MessageType.Info);

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

            AddToastMessage($"Loaded {AvailableConfigurations.Count} world configurations", MessageType.Quest);

            _configurationsLoadedTcs.TrySetResult(AvailableConfigurations.Count);
        }
        catch (Exception ex)
        {
            AddToastMessage($"Error loading configurations: {ex.Message}", MessageType.Error, 5f);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the list of available world configurations.
    /// Call this after creating a new world.
    /// </summary>
    public Task RefreshConfigurationsAsync() => LoadAvailableConfigurationsAsync();

    [RelayCommand]
    private async Task LoadSelectedConfigurationAsync()
    {
        if (SelectedConfiguration == null)
        {
            AddToastMessage("Please select a configuration to load.", MessageType.Warning);
            return;
        }

        try
        {
            IsLoading = true;
            AddToastMessage($"Loading world configuration: {SelectedConfiguration.RefName}...", MessageType.Info);

            IWorld world;
            try
            {
                world = await _mediator.Send(new LoadWorldQuery
                {
                    DataDirectory = _dataDirectory,
                    DefinitionDirectory = _schemaDirectory,
                    ConfigurationRefName = SelectedConfiguration.RefName
                });
            }
            catch (Exception loadEx)
            {
                // Loading failed - try regenerating content if generator is available
                if (_worldContentGenerator.IsAvailable && !string.IsNullOrEmpty(SelectedConfiguration.SourceDirectory))
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] World load failed, attempting content regeneration: {loadEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] Using source directory: {SelectedConfiguration.SourceDirectory}");
                    AddToastMessage("Regenerating world content...", MessageType.Info);

                    var generatedFiles = await _worldContentGenerator.GenerateWorldContentAsync(
                        SelectedConfiguration, SelectedConfiguration.SourceDirectory);

                    if (generatedFiles.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] Regenerated {generatedFiles.Count} files, retrying load...");
                        AddToastMessage($"Regenerated {generatedFiles.Count} files, loading...", MessageType.Info);

                        // Retry loading
                        world = await _mediator.Send(new LoadWorldQuery
                        {
                            DataDirectory = _dataDirectory,
                            DefinitionDirectory = _schemaDirectory,
                            ConfigurationRefName = SelectedConfiguration.RefName
                        });
                    }
                    else
                    {
                        throw new InvalidOperationException($"Content regeneration produced no files. Original error: {loadEx.Message}");
                    }
                }
                else
                {
                    throw; // Re-throw if generator not available
                }
            }

            // Initialize world bootstrapper (required when loading via mediator)
            WorldBootstrapper.Initialize(world);

            // Complete initialization with shared logic
            await InitializeWorldCoreAsync(world, _dataDirectory);

            AddToastMessage($"Loaded: {SelectedConfiguration.RefName}", MessageType.Quest);
        }
        catch (Exception ex)
        {
            AddToastMessage($"Error loading configuration: {ex.Message}", MessageType.Error, 5f);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadConfigurationAsync(string configurationRefName)
    {
        try
        {
            IsLoading = true;
            AddToastMessage($"Loading world configuration: {configurationRefName}...", MessageType.Info);

            var world = await _mediator.Send(new LoadWorldQuery
            {
                DataDirectory = _dataDirectory,
                DefinitionDirectory = _schemaDirectory,
                ConfigurationRefName = configurationRefName
            });

            // Initialize world bootstrapper (required when loading via mediator)
            WorldBootstrapper.Initialize(world);

            // Complete initialization with shared logic
            await InitializeWorldCoreAsync(world, _dataDirectory);

            AddToastMessage($"Loaded: {configurationRefName}", MessageType.Quest);
        }
        catch (Exception ex)
        {
            AddToastMessage($"Error loading configuration: {ex.Message}", MessageType.Error, 5f);
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

        // Signal that world definition is loaded (before avatar selection)
        // Provides the RefName (for avatar/database lookup) and the SourceDirectory
        // (where the world's XML definition resides on disk)
        var worldRef = world.WorldConfiguration?.RefName ?? string.Empty;
        var worldPath = world.WorldConfiguration?.SourceDirectory ?? string.Empty;
        WorldLoaded?.Invoke(worldRef, worldPath);


        // Load and display height map if available
        await LoadHeightMapImageInternalAsync(world, dataDirectory);

        // Signal session is ready with avatar and height map (for game initialization)
        // Subscribers must set IsReadyForSagaProcessing = true when ready
        var elevationWaterMap = world.WorldConfiguration?.HeightMapSettings?.ElevationWaterMap;
        SessionReady?.Invoke(PlayerAvatar, elevationWaterMap);

        // Load Sagas and triggers from world with feature status
        var (sagas, triggers) = await SagaArcViewModel.LoadFromWorldAsync(world, PlayerAvatar, _worldRepository);
        Sagas.Clear();
        AllTriggers.Clear();
        foreach (var saga in sagas) Sagas.Add(saga);
        foreach (var trigger in triggers) AllTriggers.Add(trigger);

        // Load recent transactions for History tab
        await LoadRecentTransactionsAsync();

        // Initialize avatar position at spawn if available
        InitializeAvatarPosition(world);

        // Start background processing for interaction checks (runs off UI thread)
        StartBackgroundProcessing();
    }

    /// <summary>
    /// Loads recent transactions from all saga instances for the current avatar.
    /// Called during world load and can be refreshed via RefreshRecentTransactionsAsync().
    /// </summary>
    private async Task LoadRecentTransactionsAsync()
    {
        RecentTransactions.Clear();

        if (PlayerAvatar == null)
            return;

        try
        {
            var repository = _repositoryProvider.Repository;

            // Get all saga instances for this avatar
            var instances = await repository.GetAllInstancesForAvatarAsync(PlayerAvatar.AvatarId);

            // Aggregate all transactions from all instances
            var allTransactions = instances
                .SelectMany(instance => instance.GetCommittedTransactions())
                .OrderByDescending(t => t.GetCanonicalTimestamp())
                .Take(100) // Limit to most recent 100 transactions
                .ToList();

            foreach (var transaction in allTransactions)
            {
                RecentTransactions.Add(transaction);
            }
        }
        catch (InvalidOperationException)
        {
            // Repository not initialized yet - world not fully loaded
            System.Diagnostics.Debug.WriteLine("Skipping transaction load - repository not initialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load recent transactions: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes recent transactions. Call this after significant state changes.
    /// </summary>
    public async Task RefreshRecentTransactionsAsync()
    {
        await LoadRecentTransactionsAsync();
    }

    private async Task LoadHeightMapImageInternalAsync(IWorld world, string dataDirectory)
    {
        // Clear previous image
        HeightMapImage = null;
        HeightMapMetadata = null;
        HeightMapInfo = null;
        AvatarInfo.UpdateHeightMapStatus(false);

        // Check if this world has height map settings
        if (world.IsProcedural)
        {
            // Generate synthetic map from saga arc locations
            await LoadProceduralMapAsync(world);
            return;
        }

        try
        {
            var config = world.WorldConfiguration;
            var heightMapPath = _contentPathResolver.ResolveGeographicDataPath(config.ContentPackLibrary, config.Namespace, config.HeightMapSettings.FileName);

            if (heightMapPath == null)
            {
                HeightMapInfo = $"Height map file not found: {config.HeightMapSettings.FileName}";
                return;
            }

            AddToastMessage("Loading height map image...", MessageType.Info);

            // Load the TIFF using ImageSharp
            using var image = await SixLabors.ImageSharp.Image.LoadAsync<L16>(heightMapPath);
            
            // Preprocess height map with water detection
            AddToastMessage("Processing height map for water detection...", MessageType.Info);

            var verticalShift = (int)world.WorldConfiguration.HeightMapSettings.VerticalShift;

            var flattenLocations = HeightMapProcessor.GetFlattenLocations(world);
            var processedMap = await Task.Run(() => HeightMapProcessor.ProcessHeightMap(image, 40, true, flattenLocations, verticalShift));
            world.WorldConfiguration.HeightMapSettings.ElevationWaterMap = processedMap.ElevationWaterMap;
            _processedHeightMap = processedMap;
            HasElevationData = true;

            // Convert to platform-agnostic image data for display with water-aware coloring
            var imageData = await ConvertProcessedMapToImageDataAsync(processedMap);
            HeightMapImage = imageData;
            HeightMapMetadata = world.HeightMapMetadata;
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

    /// <summary>
    /// Generates a synthetic map for procedural worlds based on saga arc locations.
    /// </summary>
    private async Task LoadProceduralMapAsync(IWorld world)
    {
        try
        {
            var result = await Task.Run(() => ProceduralMapGenerator.CreateFromWorld(world));

            var (metadata, imageData) = result.Value;

            // todo: this doesn't actually 'stick'
            // Set the metadata on the world so coordinate conversions work
            world.HeightMapMetadata = metadata;

            // Set the image for display
            HeightMapImage = imageData;
            HeightMapMetadata = metadata;
            AvatarInfo.UpdateHeightMapStatus(true);

            // Update minimum zoom
            UpdateMinimumZoom();

            // Calculate saga arc count for info display
            var sagaArcCount = world.SagaArcLookup.Count;

            HeightMapInfo = $"Procedural Map: {ProceduralMapGenerator.MapSize} x {ProceduralMapGenerator.MapSize} pixels\n" +
                           $"Bounds: {metadata.North:F4}°N to {metadata.South:F4}°S, " +
                           $"{metadata.West:F4}°W to {metadata.East:F4}°E\n" +
                           $"Coverage: {metadata.Width:F4}° x {metadata.Height:F4}°\n" +
                           $"Saga Arcs: {sagaArcCount}";

            AddToastMessage($"Procedural map generated ({sagaArcCount} saga arcs)", MessageType.Quest);
        }
        catch (Exception ex)
        {
            HeightMapInfo = $"Error generating procedural map: {ex.Message}";
        }
    }

    //// OLD METHOD REMOVED: SpawnCharactersFromTrigger
    //// Character spawning now handled by CQRS UpdateAvatarPositionCommand via ProcessAvatarMovementAsync

    //private static async Task<HeightMapImageData> CreatePlaceholderHeightMapAsync(int width, int height)
    //{
    //    var stride = width * 4; // 4 bytes per pixel for BGRA32

    //    // Create simple gradient background on background thread
    //    var pixelData = await Task.Run(() =>
    //    {
    //        var data = new byte[height * stride];

    //        for (int y = 0; y < height; y++)
    //        {
    //            for (int x = 0; x < width; x++)
    //            {
    //                // Create subtle radial gradient from center (lighter) to edges (darker)
    //                var centerX = width / 2.0;
    //                var centerY = height / 2.0;
    //                var maxDist = Math.Sqrt(centerX * centerX + centerY * centerY);
    //                var dist = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
    //                var brightness = (byte)(200 - (dist / maxDist * 50)); // 200 at center, 150 at edges

    //                var index = y * stride + x * 4;
    //                data[index] = brightness;     // Blue
    //                data[index + 1] = brightness; // Green
    //                data[index + 2] = brightness; // Red
    //                data[index + 3] = 255;        // Alpha (fully opaque)
    //            }
    //        }

    //        return data;
    //    });

    //    // Create platform-agnostic image data
    //    return new HeightMapImageData(pixelData, width, height, stride);
    //}

    private static async Task<HeightMapImageData> ConvertProcessedMapToImageDataAsync(ProcessedHeightMap processedMap)
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
            // Stop background processing if running (before disposing database)
            StopBackgroundProcessing();

            // Dispose existing database if any
            _worldDatabase?.Dispose();

            AddToastMessage("Initializing world database...", MessageType.Info);

            // Use factory to create all repositories (eliminates Infrastructure imports)
            var repositories = _repositoryFactory.CreateRepositories(
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

            AddToastMessage($"Database initialized: {world.WorldConfiguration.RefName}.db", MessageType.Quest);
        }
        catch (Exception ex)
        {
            AddToastMessage($"Database initialization failed: {ex.Message}", MessageType.Error, 5f);
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

            AvatarPixelX = CoordinateConverter.HeightMapLongitudeToPixelX(spawnLong, world.HeightMapMetadata);
            AvatarPixelY = CoordinateConverter.HeightMapLatitudeToPixelY(spawnLat, world.HeightMapMetadata);
            var avatarElevation = SampleElevation((int)AvatarPixelX, (int)AvatarPixelY);

            SetAvatarPosition(spawnLat, spawnLong, avatarElevation, true);
        }
        catch (Exception ex)
        {
            AddToastMessage($"Could not set initial avatar position: {ex.Message}", MessageType.Error, 5f);
        }
    }

    public async void SetAvatarPosition(double latitude, double longitude, double elevation, bool centerOnAvatar = false)
    {
        AvatarLatitude = latitude;
        AvatarLongitude = longitude;

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
        if (CurrentWorld == null || PlayerAvatar == null || !IsReadyForSagaProcessing)
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

                // Refresh trigger status so completed triggers disappear
                await UpdateSagaFeatureStatus(trigger.SagaRefName);
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

 

    /// <summary>
    /// Updates a SagaViewModel's trigger status after recording a transaction.
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
                var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.Longitude, CurrentWorld);
                var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.Latitude, CurrentWorld);

                // Re-query feature status via CQRS
                var interactions = await _mediator.Send(new QueryInteractionsAtPositionQuery
                {
                    ModelX = sagaModelX,
                    ModelZ = sagaModelZ,
                    Avatar = PlayerAvatar
                });

                // Update trigger statuses (so completed triggers disappear)
                foreach (var triggerVM in sagaVM.Triggers)
                {
                    var triggerInteraction = interactions.FirstOrDefault(i =>
                        i.Type == SagaInteractionType.SagaTrigger &&
                        i.SagaRef == sagaRef &&
                        i.SagaTriggerRef == triggerVM.RefName);

                    if (triggerInteraction != null)
                    {
                        triggerVM.Status = triggerInteraction.Status;
                        triggerVM.RingColor = TriggerColors.GetColor(triggerInteraction.Status);
                    }
                }
            }
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
        AddToastMessage("Manual character spawning disabled - characters spawn via Saga triggers only", MessageType.Warning);
        ActivityLog.Insert(0, "[!] Manual spawning not supported in event-sourced architecture");
    }

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

            AvatarPixelX = CoordinateConverter.HeightMapLongitudeToPixelX(clickLatitude, CurrentWorld.HeightMapMetadata);
            AvatarPixelY = CoordinateConverter.HeightMapLatitudeToPixelY(clickLongitude, CurrentWorld.HeightMapMetadata);
            var avatarElevation = SampleElevation((int)AvatarPixelX, (int)AvatarPixelY);

            // Move avatar to clicked position
            SetAvatarPosition(clickLatitude, clickLongitude, avatarElevation);

            // Notify external consumers (e.g., game) to teleport the 3D avatar
            AvatarTeleportRequested?.Invoke(clickLatitude, clickLongitude);

            AddToastMessage($"Avatar moved to {clickLatitude:F6}°, {clickLongitude:F6}°", MessageType.Info);
        }
        catch (Exception ex)
        {
            AddToastMessage($"Error setting avatar position: {ex.Message}", MessageType.Error, 5f);
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
            }
        }

        // Step 1: Reset ALL triggers on ALL sagas
        foreach (var saga in Sagas)
        {
            foreach (var trigger in saga.Triggers)
            {
                trigger.IsVisible = false;
                trigger.IsHovered = false;
            }
        }

        // Step 2: Enable only triggers that are in the query results (mouse is inside them)
        foreach (var ti in triggerInteractions)
        {
            var trigger = Sagas
                .SelectMany(s => s.Triggers)
                .FirstOrDefault(t => t.RefName == ti.SagaTriggerRef && t.SagaRefName == ti.SagaRef);

            // If trigger not found, the saga may be hidden - discover it now
            if (trigger == null && CurrentWorld != null)
            {
                // Try to load the hidden saga and add it to ViewModels (discovery!)
                if (CurrentWorld.SagaArcLookup.TryGetValue(ti.SagaRef, out var sagaArc) &&
                    CurrentWorld.SagaTriggersLookup.TryGetValue(ti.SagaRef, out var sagaTriggers) &&
                    CurrentWorld.HeightMapMetadata != null)
                {
                    var newSagaVM = SagaArcViewModel.FromDomain(
                        sagaArc,
                        sagaTriggers.OrderByDescending(t => t.EnterRadius).ToList(),
                        CurrentWorld.HeightMapMetadata,
                        CurrentWorld);

                    // Set feature dot color based on category (newly discovered = available)
                    newSagaVM.InteractionStatus = InteractionStatus.Available;
                    newSagaVM.FeatureDotColor = SagaColors.GetColor(newSagaVM.Category, InteractionStatus.Available);
                    newSagaVM.FeatureDotOpacity = 1.0;

                    Sagas.Add(newSagaVM);

                    // Add triggers to AllTriggers for rendering
                    foreach (var triggerVM in newSagaVM.Triggers)
                    {
                        triggerVM.SagaRefName = newSagaVM.RefName;
                        AllTriggers.Add(triggerVM);
                    }

                    // Now find the trigger again
                    trigger = newSagaVM.Triggers.FirstOrDefault(t => t.RefName == ti.SagaTriggerRef);

                    System.Diagnostics.Debug.WriteLine($"[Discovery] Saga '{ti.SagaRef}' discovered and added to ViewModels");
                }
            }

            if (trigger == null)
                continue; // Still not found - skip

            // Show if Available or Locked, keep hidden if Complete
            trigger.IsVisible = ti.Status == InteractionStatus.Available ||
                                ti.Status == InteractionStatus.Locked;
            trigger.IsHovered = true; // Mouse is inside this trigger (from CQRS query)
            trigger.Status = ti.Status;
            trigger.RingColor = TriggerColors.GetColor(ti.Status);

            // Debug info - show expected spawn count from trigger definition
            if (System.Diagnostics.Debugger.IsAttached && CurrentWorld != null)
            {
                var saga = CurrentWorld.TryGetSagaArcByRefName(ti.SagaRef);
                var actualTrigger = saga?.SagaTrigger?.FirstOrDefault(t => t.RefName == ti.SagaTriggerRef);
                var expectedSpawnCount = actualTrigger?.Spawn?.Sum(s => s.Count) ?? 0;
                var spawnDetails = actualTrigger?.Spawn?.Select(s => $"  - {s.CharacterRef} (x{s.Count})") ?? Array.Empty<string>();

                trigger.DebugQueryInfo = $"Status: {ti.Status}\n" +
                                         $"Expected spawns: {expectedSpawnCount}\n" +
                                         string.Join("\n", spawnDetails);
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

    public int SampleElevation(int pixelX, int pixelY)
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
                AddToastMessage("Steam achievements synced", MessageType.Quest);
            }

            AddToastMessage("Welcome back! Avatar loaded.", MessageType.Quest);
            AddToastMessage("Welcome back!", MessageType.Info, 3f);
            return;
        }

        // No local avatar — check if one exists on the server (played on another device)
        var recoveredAvatar = await _avatarCreationService.FindExistingAvatarAsync(world);
        if (recoveredAvatar != null)
        {
            PlayerAvatar = recoveredAvatar;
            AvatarInfo.UpdatePlayerAvatar(PlayerAvatar);
            await SavePlayerAvatarAsync();
            AddToastMessage("Avatar recovered from server.", MessageType.Quest);
            return;
        }

        // No avatar exists anywhere - show archetype selection
        AddToastMessage("Select your character archetype...", MessageType.Info);

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
            AddToastMessage("Avatar creation cancelled", MessageType.Warning);
            return;
        }

        // Create new avatar from archetype via injected service (offline or online)
        // Generate the avatar GUID here so it's consistent across local DB and any consumer of saga
        var avatarId = Guid.NewGuid();
        PlayerAvatar = await _avatarCreationService.CreateAvatarAsync(avatarId, selectedArchetype, world);
        AvatarInfo.UpdatePlayerAvatar(PlayerAvatar);

        // Save to database
        await SavePlayerAvatarAsync();

        AddToastMessage($"Avatar created: {selectedArchetype.DisplayName}", MessageType.Quest);
        AddToastMessage($"Welcome, {selectedArchetype.DisplayName}!", MessageType.Quest, 5f);
    }

    private async Task<AvatarArchetype?> ShowArchetypeSelectionDialogAsync()
    {
        // Use appropriate selector based on current mode
        var selector = _imguiArchetypeSelector;
        var currencyName = CurrentWorld?.WorldConfiguration?.CurrencyName;

        return await selector.SelectArchetypeAsync(AvailableArchetypes, currencyName);
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
            AddToastMessage($"Error saving avatar: {ex.Message}", MessageType.Error, 5f);
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

    /// <summary>
    /// Equips or unequips an item outside of battle.
    /// </summary>
    /// <param name="equipmentRef">Equipment reference to equip, or null/empty to unequip</param>
    /// <param name="slotRef">Slot to equip into (if not provided, auto-detected from equipment definition)</param>
    /// <returns>True if equip succeeded</returns>
    public async Task<bool> EquipItemAsync(string? equipmentRef, string? slotRef = null)
    {
        if (PlayerAvatar == null || CurrentWorld == null)
        {
            AddToastMessage("Cannot equip: No avatar or world loaded", MessageType.Error);
            return false;
        }

        try
        {
            // If no slot specified, get it from equipment definition
            if (string.IsNullOrEmpty(slotRef) && !string.IsNullOrEmpty(equipmentRef))
            {
                var equipment = CurrentWorld.GetEquipmentByRefName(equipmentRef);
                if (equipment == null)
                {
                    AddToastMessage($"Equipment '{equipmentRef}' not found", MessageType.Error);
                    return false;
                }
                slotRef = equipment.SlotRef.ToString();
            }

            if (string.IsNullOrEmpty(slotRef))
            {
                AddToastMessage("Cannot equip: No slot specified", MessageType.Error);
                return false;
            }

            // Use first saga in the world for transaction logging, or a default
            var sagaRef = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault()?.RefName ?? "PlayerLife";

            var command = new EquipItemOutsideBattleCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaArcRef = sagaRef,
                EquipmentRef = equipmentRef,
                SlotRef = slotRef,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                var action = string.IsNullOrEmpty(equipmentRef) ? "Unequipped" : "Equipped";
                var itemName = string.IsNullOrEmpty(equipmentRef)
                    ? slotRef
                    : (CurrentWorld.GetEquipmentByRefName(equipmentRef)?.DisplayName ?? equipmentRef);
                AddToastMessage($"{action} {itemName}", MessageType.Info);

                // Update avatar from result if provided
                if (result.UpdatedAvatar != null)
                {
                    // Copy updated state to PlayerAvatar
                    PlayerAvatar.CombatProfile = result.UpdatedAvatar.CombatProfile;
                }

                NotifyPlayerAvatarChanged();
                return true;
            }
            else
            {
                AddToastMessage($"Equip failed: {result.ErrorMessage}", MessageType.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            AddToastMessage($"Error equipping item: {ex.Message}", MessageType.Error);
            System.Diagnostics.Debug.WriteLine($"EquipItemAsync ERROR: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Gets the currently equipped item in a slot.
    /// </summary>
    public string? GetEquippedItemInSlot(string slotRef)
    {
        if (PlayerAvatar?.CombatProfile == null)
            return null;

        PlayerAvatar.CombatProfile.TryGetValue(slotRef, out var equipmentRef);
        return equipmentRef;
    }

    /// <summary>
    /// Checks if an item is currently equipped.
    /// </summary>
    public bool IsItemEquipped(string equipmentRef)
    {
        if (PlayerAvatar?.CombatProfile == null)
            return false;

        return PlayerAvatar.CombatProfile.ContainsValue(equipmentRef);
    }

    /// <summary>
    /// Uses a consumable item from the avatar's inventory.
    /// Applies effects (health/stamina/mana restoration) and decrements quantity.
    /// </summary>
    /// <param name="consumableRef">Consumable reference to use</param>
    /// <returns>True if consumable was used successfully</returns>
    public async Task<bool> UseConsumableAsync(string consumableRef)
    {
        if (PlayerAvatar == null || CurrentWorld == null)
        {
            AddToastMessage("Cannot use consumable: No avatar or world loaded", MessageType.Error);
            return false;
        }

        if (string.IsNullOrEmpty(consumableRef))
        {
            AddToastMessage("Cannot use consumable: No item specified", MessageType.Error);
            return false;
        }

        try
        {
            // Use first saga in the world for transaction logging, or a default
            var sagaRef = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault()?.RefName ?? "PlayerLife";

            var command = new UseConsumableCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaArcRef = sagaRef,
                ConsumableRef = consumableRef,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                var consumable = CurrentWorld.GetConsumableByRefName(consumableRef);
                var itemName = consumable?.DisplayName ?? consumableRef;

                // Build effect message
                var effectsList = result.Data?.TryGetValue("EffectsApplied", out var effects) == true
                    ? effects as List<string>
                    : null;
                var effectMessage = effectsList?.Count > 0
                    ? $" ({string.Join(", ", effectsList)})"
                    : "";

                AddToastMessage($"Used {itemName}{effectMessage}", MessageType.Info);

                // Update avatar from result if provided
                if (result.UpdatedAvatar != null)
                {
                    // Copy updated state to PlayerAvatar
                    PlayerAvatar.Stats = result.UpdatedAvatar.Stats;
                    PlayerAvatar.Capabilities = result.UpdatedAvatar.Capabilities;
                }

                NotifyPlayerAvatarChanged();
                return true;
            }
            else
            {
                AddToastMessage($"Use failed: {result.ErrorMessage}", MessageType.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            AddToastMessage($"Error using consumable: {ex.Message}", MessageType.Error);
            System.Diagnostics.Debug.WriteLine($"UseConsumableAsync ERROR: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Sharpens a tool, restoring its condition to 100%.
    /// Deducts currency from the avatar.
    /// </summary>
    /// <param name="toolRef">Tool reference to sharpen</param>
    /// <param name="cost">Cost in currency</param>
    /// <returns>True if tool was sharpened successfully</returns>
    public async Task<bool> SharpenToolAsync(string toolRef, int cost)
    {
        if (PlayerAvatar == null || CurrentWorld == null)
        {
            AddToastMessage("Cannot sharpen tool: No avatar or world loaded", MessageType.Error);
            return false;
        }

        if (string.IsNullOrEmpty(toolRef))
        {
            AddToastMessage("Cannot sharpen tool: No tool specified", MessageType.Error);
            return false;
        }

        try
        {
            // Use first saga in the world for transaction logging, or a default
            var sagaRef = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault()?.RefName ?? "PlayerLife";

            var command = new SharpenToolCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaArcRef = sagaRef,
                ToolRef = toolRef,
                Cost = cost,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                var tool = CurrentWorld.ToolsLookup?.GetValueOrDefault(toolRef);
                var toolName = tool?.DisplayName ?? toolRef;
                var currencyName = CurrentWorld.WorldConfiguration?.CurrencyName ?? "Credits";

                AddToastMessage($"{toolName} sharpened to full condition (-{cost} {currencyName})", MessageType.Info);

                // Update avatar from result if provided
                if (result.UpdatedAvatar != null)
                {
                    PlayerAvatar.Stats = result.UpdatedAvatar.Stats;
                    PlayerAvatar.Capabilities = result.UpdatedAvatar.Capabilities;
                }

                NotifyPlayerAvatarChanged();
                return true;
            }
            else
            {
                AddToastMessage($"Sharpen failed: {result.ErrorMessage}", MessageType.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            AddToastMessage($"Error sharpening tool: {ex.Message}", MessageType.Error);
            System.Diagnostics.Debug.WriteLine($"SharpenToolAsync ERROR: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Teleports the avatar to a new location.
    /// Deducts currency from the avatar.
    /// </summary>
    /// <param name="destinationLat">Destination latitude</param>
    /// <param name="destinationLon">Destination longitude</param>
    /// <param name="cost">Cost in currency</param>
    /// <returns>True if teleport was successful</returns>
    public async Task<bool> TeleportAvatarAsync(double destinationLat, double destinationLon, int cost)
    {
        if (PlayerAvatar == null || CurrentWorld == null)
        {
            AddToastMessage("Cannot teleport: No avatar or world loaded", MessageType.Error);
            return false;
        }

        try
        {
            // Use first saga in the world for transaction logging, or a default
            var sagaRef = CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault()?.RefName ?? "PlayerLife";

            var command = new TeleportAvatarCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                SagaArcRef = sagaRef,
                DestinationLatitude = destinationLat,
                DestinationLongitude = destinationLon,
                Cost = cost,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                var currencyName = CurrentWorld.WorldConfiguration?.CurrencyName ?? "Credits";
                AddToastMessage($"Teleported! (-{cost} {currencyName})", MessageType.Info);

                // Update avatar from result if provided
                if (result.UpdatedAvatar != null)
                {
                    PlayerAvatar.Stats = result.UpdatedAvatar.Stats;
                    PlayerAvatar.Capabilities = result.UpdatedAvatar.Capabilities;
                }

                NotifyPlayerAvatarChanged();
                return true;
            }
            else
            {
                AddToastMessage($"Teleport failed: {result.ErrorMessage}", MessageType.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            AddToastMessage($"Error teleporting: {ex.Message}", MessageType.Error);
            System.Diagnostics.Debug.WriteLine($"TeleportAvatarAsync ERROR: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Gets the quantity of a consumable in the avatar's inventory.
    /// </summary>
    public int GetConsumableQuantity(string consumableRef)
    {
        var entry = PlayerAvatar?.Capabilities?.Consumables?
            .FirstOrDefault(c => c.ConsumableRef == consumableRef);
        return entry?.Quantity ?? 0;
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
            AddToastMessage("No tools available", MessageType.Warning);
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
            AddToastMessage("No materials available", MessageType.Warning);
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
            AddToastMessage("No equipment available", MessageType.Warning);
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
            AddToastMessage("No consumables available", MessageType.Warning);
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
            AddToastMessage("No spells available", MessageType.Warning);
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
            AddToastMessage("No achievements available", MessageType.Warning);
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
            AddToastMessage("No characters available", MessageType.Warning);
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
            AddToastMessage($"Character template not found: {characterVM.RefName}", MessageType.Warning);
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
            AddToastMessage("Steam service not available", MessageType.Warning);
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
                AddToastMessage($"Failed to set achievement: {setError}", MessageType.Error);
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

            AddToastMessage($"Achievement {testAchievementId} set - waiting for Steam callback...", MessageType.Info);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Steam Test] ❌ EXCEPTION: {ex.Message}");
            AddToastMessage($"Test exception: {ex.Message}", MessageType.Error, 5f);
        }
    }

    private void OnCharacterChanged()
    {
        CharacterChanged?.Invoke(this, EventArgs.Empty);
    }

    #region Dev Tools - Interaction Testing

    /// <summary>
    /// Spawns a test character for dev testing using CQRS command.
    /// Creates proper saga transactions so dialogue/trade/combat work correctly.
    /// </summary>
    public async Task<CharacterViewModel?> SpawnDevCharacterAsync(DevCharacterType characterType)
    {
        if (CurrentWorld == null || PlayerAvatar == null)
        {
            System.Diagnostics.Debug.WriteLine("[DevTools] Cannot spawn - no world or avatar loaded");
            AddToastMessage("Load a world first", MessageType.Warning);
            return null;
        }

        try
        {
            // Find a suitable test character from the world based on type
            var testCharacter = FindTestCharacter(characterType);
            if (testCharacter == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DevTools] No suitable {characterType} character found in world");
                AddToastMessage($"No {characterType} character found in world data", MessageType.Warning);
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[DevTools] Found test character: {testCharacter.DisplayName} ({testCharacter.RefName})");

            // Find a real saga to use
            var sagaRef = FindSagaWithCharacter(testCharacter.RefName)
                ?? CurrentWorld.Gameplay?.SagaArcs?.FirstOrDefault()?.RefName
                ?? "";

            // Use CQRS command to spawn with proper transaction history
            var spawnCommand = new SpawnDevCharacterCommand
            {
                AvatarId = PlayerAvatar.AvatarId,
                CharacterRef = testCharacter.RefName,
                SagaArcRef = sagaRef,
                Avatar = PlayerAvatar
            };

            var result = await _mediator.Send(spawnCommand);

            if (!result.Successful)
            {
                System.Diagnostics.Debug.WriteLine($"[DevTools] Spawn failed: {result.ErrorMessage}");
                AddToastMessage($"Spawn failed: {result.ErrorMessage}", MessageType.Error);
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[DevTools] Spawn succeeded, InstanceId: {result.CharacterInstanceId}");

            // Position near avatar (2m away for immediate interaction)
            var spawnX = PlayerAvatar.X + 2.0;
            var spawnZ = PlayerAvatar.Z;

            // Create character ViewModel and add to collection
            var canDialogue = testCharacter.Interactable?.DialogueTreeRef != null;
            var canTrade = testCharacter.Traits?.Any(t => t.Name == CharacterTraitType.WillTrade) ?? false;
            var isHostile = testCharacter.Traits?.Any(t => t.Name == CharacterTraitType.Hostile) ?? false;
            var isBoss = testCharacter.Traits?.Any(t => t.Name == CharacterTraitType.BossFight) ?? false;

            var characterVm = new CharacterViewModel
            {
                CharacterInstanceId = result.CharacterInstanceId,
                CharacterRef = testCharacter.RefName,
                DisplayName = testCharacter.DisplayName,
                SagaRef = result.SagaRef ?? sagaRef,
                CharacterType = characterType.ToString(),
                ModelX = spawnX,
                ModelZ = spawnZ,
                PixelX = 0,
                PixelY = 0,
                IsAlive = true,
                CanDialogue = canDialogue,
                CanTrade = canTrade,
                CanAttack = isHostile || isBoss,
                MarkerColor = GetDevCharacterColor(characterType)
            };

            Characters.Add(characterVm);
            System.Diagnostics.Debug.WriteLine($"[DevTools] Spawned {testCharacter.DisplayName} at ({spawnX:F2}, {spawnZ:F2})");
            AddToastMessage($"Spawned {testCharacter.DisplayName}", MessageType.Combat);
            AddToastMessage($"{testCharacter.DisplayName} appeared!", MessageType.Combat, 3f);

            return characterVm;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevTools] Error spawning character: {ex.Message}");
            AddToastMessage($"Error: {ex.Message}", MessageType.Error, 5f);
            return null;
        }
    }

    /// <summary>
    /// Finds a suitable test character from world data based on type.
    /// </summary>
    private static readonly Random _devRandom = new();

    private Character? FindTestCharacter(DevCharacterType characterType)
    {
        var characters = CurrentWorld?.Gameplay?.Characters;
        if (characters == null || characters.Length == 0)
            return null;

        return characterType switch
        {
            // NPC: Prioritize NPCs with loot (gift givers), exclude merchants/bosses/hostiles
            // Use random selection for variety
            DevCharacterType.NPC => FindRandomNpc(characters),

            // Merchant: prefer one with BOTH dialogue AND WillTrade, fallback to just WillTrade
            DevCharacterType.Merchant => FindRandomCharacterWithTrait(characters, CharacterTraitType.WillTrade),

            // Boss: prefer one with BOTH dialogue AND BossFight, fallback to just BossFight
            DevCharacterType.Boss => FindRandomCharacterWithTrait(characters, CharacterTraitType.BossFight),

            // Hostile: prefer one with BOTH dialogue AND Hostile, fallback to just Hostile
            DevCharacterType.Hostile => FindRandomCharacterWithTrait(characters, CharacterTraitType.Hostile),

            _ => characters.Length > 0 ? characters[_devRandom.Next(characters.Length)] : null
        };
    }

    /// <summary>
    /// Finds a random character with the specified trait, preferring those with dialogue.
    /// </summary>
    private static Character? FindRandomCharacterWithTrait(Character[] characters, CharacterTraitType trait)
    {
        // Prefer characters with BOTH dialogue AND the trait
        var withDialogue = characters.Where(c =>
            c.Interactable?.DialogueTreeRef != null &&
            (c.Traits?.Any(t => t.Name == trait) ?? false))
            .ToList();

        if (withDialogue.Count > 0)
        {
            var selected = withDialogue[_devRandom.Next(withDialogue.Count)];
            System.Diagnostics.Debug.WriteLine($"[DevTools] Selected random {trait} with dialogue: {selected.DisplayName} ({selected.RefName})");
            return selected;
        }

        // Fallback to any character with the trait
        var withTrait = characters.Where(c =>
            c.Traits?.Any(t => t.Name == trait) ?? false)
            .ToList();

        if (withTrait.Count > 0)
        {
            var selected = withTrait[_devRandom.Next(withTrait.Count)];
            System.Diagnostics.Debug.WriteLine($"[DevTools] Selected random {trait} (no dialogue): {selected.DisplayName} ({selected.RefName})");
            return selected;
        }

        return null;
    }

    /// <summary>
    /// Finds a random NPC for dev testing, prioritizing those with loot (gift givers).
    /// Excludes merchants, bosses, and hostiles.
    /// </summary>
    private static Character? FindRandomNpc(Character[] characters)
    {
        // Filter to friendly NPCs with dialogue (exclude merchants, bosses, hostiles)
        var friendlyNpcs = characters.Where(c =>
            c.Interactable?.DialogueTreeRef != null &&
            !(c.Traits?.Any(t => t.Name == CharacterTraitType.Hostile) ?? false) &&
            !(c.Traits?.Any(t => t.Name == CharacterTraitType.WillTrade) ?? false) &&
            !(c.Traits?.Any(t => t.Name == CharacterTraitType.BossFight) ?? false))
            .ToList();

        if (friendlyNpcs.Count == 0)
            return null;

        // Prioritize NPCs with loot (gift givers) - they have interesting dialogue paths
        var npcsWithLoot = friendlyNpcs.Where(c => c.Interactable?.Loot != null &&
            (c.Interactable.Loot.Equipment?.Length > 0 ||
             c.Interactable.Loot.Consumables?.Length > 0 ||
             c.Interactable.Loot.Spells?.Length > 0))
            .ToList();

        if (npcsWithLoot.Count > 0)
        {
            var selected = npcsWithLoot[_devRandom.Next(npcsWithLoot.Count)];
            System.Diagnostics.Debug.WriteLine($"[DevTools] Selected NPC with loot: {selected.DisplayName} ({selected.RefName})");
            return selected;
        }

        // Fallback to any friendly NPC
        var fallback = friendlyNpcs[_devRandom.Next(friendlyNpcs.Count)];
        System.Diagnostics.Debug.WriteLine($"[DevTools] Selected random NPC: {fallback.DisplayName} ({fallback.RefName})");
        return fallback;
    }

    /// <summary>
    /// Finds a saga that spawns the given character (for dev testing with real saga refs).
    /// </summary>
    private string? FindSagaWithCharacter(string characterRef)
    {
        var sagas = CurrentWorld?.Gameplay?.SagaArcs;
        if (sagas == null)
            return null;

        foreach (var saga in sagas)
        {
            // Check if this saga has triggers that spawn the character
            if (CurrentWorld!.SagaTriggersLookup.TryGetValue(saga.RefName, out var triggers))
            {
                foreach (var trigger in triggers)
                {
                    if (trigger.Spawn != null)
                    {
                        foreach (var spawn in trigger.Spawn)
                        {
                            if (spawn.CharacterRef == characterRef)
                            {
                                return saga.RefName;
                            }
                        }
                    }
                }
            }
        }

        // Fallback: just return first saga (dialogue needs a saga context)
        return sagas.FirstOrDefault()?.RefName;
    }

    /// <summary>
    /// Gets a color for the dev character dot based on type.
    /// </summary>
    private System.Numerics.Vector4 GetDevCharacterColor(DevCharacterType characterType)
    {
        return characterType switch
        {
            DevCharacterType.NPC => new System.Numerics.Vector4(0.3f, 0.8f, 0.3f, 1f),      // Green
            DevCharacterType.Merchant => new System.Numerics.Vector4(1f, 0.84f, 0f, 1f),    // Gold
            DevCharacterType.Boss => new System.Numerics.Vector4(0.8f, 0.2f, 0.8f, 1f),     // Purple
            DevCharacterType.Hostile => new System.Numerics.Vector4(0.9f, 0.2f, 0.2f, 1f),  // Red
            _ => new System.Numerics.Vector4(1f, 1f, 1f, 1f)                                 // White
        };
    }

    #endregion

}