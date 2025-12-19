using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components;
using Ambient.Saga.UI.Components.Modals;

namespace Ambient.Saga.UI.Services;

/// <summary>
/// Main ImGui-based World Map UI - coordinates Sandbox-specific and game-reusable components.
///
/// ARCHITECTURE PATTERN:
/// - WorldSelectionScreen: SANDBOX-SPECIFIC (shown at startup)
/// - GameplayOverlay: GAME-REUSABLE (complete gameplay UI for ANY 3D app)
///
/// Flow:
/// 1. Launch → Show WorldSelectionScreen (Sandbox only)
/// 2. Load world → Hide selection screen, show GameplayOverlay
/// 3. GameplayOverlay is ready to drop into the actual game (no world selection)
/// </summary>
public class WorldMapUI
{
    private MainViewModel _viewModel;

    // UI Components
    private GameplayOverlay _gameplayOverlay;

    // Modal system (injected via DI for ImGuiArchetypeSelector)
    private readonly ModalManager _modalManager;

    // Platform-agnostic texture provider (DirectX11, OpenGL, etc.)
    private ITextureProvider? _textureProvider;

    // Heightmap texture resources
    private nint _heightMapTexturePtr = nint.Zero;
    private int _heightMapWidth;
    private int _heightMapHeight;
    private IDisposable[]? _heightMapResources;

    public WorldMapUI(ModalManager modalManager)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
    }

    public void Initialize(MainViewModel viewModel, ITextureProvider textureProvider)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));

        // Initialize components
        _gameplayOverlay = new GameplayOverlay(_modalManager);
        
        // Subscribe to pause menu request from input handler
        _gameplayOverlay.InputHandler.PauseMenuRequested += OnPauseMenuRequested;
        
        // Subscribe to quit request from viewModel (raised by WorldSelectionScreen)
        _viewModel.RequestQuit += OnQuitRequestedFromViewModel;

        // Show world selection at startup (Sandbox-specific flow)
        _modalManager.OpenWorldSelection();

        // Subscribe to heightmap changes
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Convert initial heightmap if available
        UpdateHeightMapTexture();
    }
    
    private void OnPauseMenuRequested()
    {
        // ESC pressed with no panels open - show pause menu
        _modalManager.OpenPauseMenu();
        System.Diagnostics.Debug.WriteLine("Pause menu requested");
    }
    
    private void OnQuitRequestedFromViewModel()
    {
        // Forward quit request from ViewModel to ModalManager
        _modalManager.RequestQuit();
        System.Diagnostics.Debug.WriteLine("Quit requested from world selection");
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HeightMapImage))
        {
            UpdateHeightMapTexture();
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentWorld))
        {
            // World loaded - hide selection screen and show main UI
            if (_viewModel.CurrentWorld != null)
            {
                _modalManager.CloseModal("WorldSelection");
            }
        }
    }

    private void UpdateHeightMapTexture()
    {
        // Dispose old texture
        if (_heightMapResources != null)
        {
            _textureProvider?.DisposeTexture(_heightMapResources);
            _heightMapTexturePtr = nint.Zero;
            _heightMapResources = null;
        }

        // Create new texture from HeightMapImageData
        if (_viewModel?.HeightMapImage != null && _textureProvider != null)
        {
            try
            {
                var (texturePtr, width, height, resources) = _textureProvider.CreateTextureFromImageData(_viewModel.HeightMapImage);
                _heightMapTexturePtr = texturePtr;
                _heightMapWidth = width;
                _heightMapHeight = height;
                _heightMapResources = resources;

                System.Diagnostics.Debug.WriteLine($"Heightmap texture created: {width}x{height}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create heightmap texture: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_heightMapResources != null)
        {
            _textureProvider?.DisposeTexture(_heightMapResources);
            _heightMapTexturePtr = nint.Zero;
            _heightMapResources = null;
        }

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.RequestQuit -= OnQuitRequestedFromViewModel;
        }
        
        if (_gameplayOverlay != null)
        {
            _gameplayOverlay.InputHandler.PauseMenuRequested -= OnPauseMenuRequested;
        }
    }

    // TODO: move this somewhere sensible
    /// <summary>
    /// Check if any gameplay panel is currently open (Map, Character, WorldInfo).
    /// Used by host applications to detect when UI is active and adjust game state accordingly.
    /// </summary>
    public bool IsAnyPanelOpen => _gameplayOverlay?.ActivePanel != ActivePanel.None;

    public void Update(float deltaTime)
    {
        _modalManager.Update(deltaTime);
    }

    public void Render()
    {
        if (_viewModel == null || _gameplayOverlay == null) return;

        // Show world selection screen if no world loaded yet (Sandbox-specific)
        // Note: WorldSelectionScreen is now managed by ModalManager
        if (_modalManager.ShowWorldSelection)
        {
            _modalManager.Render(_viewModel);
            return;
        }

        // Main game UI (game-reusable via GameplayOverlay)
        _gameplayOverlay.Render(_viewModel, _heightMapTexturePtr, _heightMapWidth, _heightMapHeight);
    }
}
