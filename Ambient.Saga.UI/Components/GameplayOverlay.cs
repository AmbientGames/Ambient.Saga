using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Panels;
using Ambient.Saga.UI.Components.Modals;
using Ambient.Saga.UI.Components.Input;
using Ambient.Saga.UI.Components.Rendering;

namespace Ambient.Saga.UI.Components;

/// <summary>
/// Which panel is currently active in game mode.
/// In game mode, only one panel is shown at a time (press key to toggle).
/// </summary>
public enum ActivePanel
{
    /// <summary>No panel open - just the 3D world view with minimal HUD</summary>
    None,
    /// <summary>Map panel (press M) - shows world map with click-to-teleport</summary>
    Map,
    /// <summary>Character panel (press C) - shows avatar stats, inventory, quests</summary>
    Character,
    /// <summary>World Info panel (press I) - shows world catalog</summary>
    WorldInfo,
    /// <summary>Dev Tools panel (press Insert) - only available when debugger attached</summary>
    DevTools
}

/// <summary>
/// GAME-REUSABLE: Main ImGui gameplay overlay for the 3D world.
//>
/// This component displays the tactical world view including:
/// - Toggle-based panel system (M=Map, C=Character, I=World Info)
/// - Status bar with hotkey hints
/// - All interactive modals (Dialogue, Battle, Trade, Loot, Quest, etc.)
///
/// EXTENSIBILITY:
/// The overlay now supports custom input handling and HUD rendering through:
/// - IInputHandler: Customize keyboard/mouse controls (default: M/C/I/ESC)
/// - IHudRenderer: Customize the always-visible HUD bar (default: bottom bar with hotkeys)
///
/// KEYBOARD CONTROLS (Default):
/// - M: Toggle Map panel (quarter-screen map with click-to-teleport)
/// - C: Toggle Character panel (avatar stats, inventory, quests, achievements)
/// - I: Toggle World Info panel (world catalog, debug info)
/// - ESC: Close current panel
///
/// ARCHITECTURE:
/// This is the complete game UI that ANY 3D application can use to interact with the Ambient world.
/// It provides view/interaction layer over the world, but is NOT the world itself.
///
/// INTEGRATION PATTERN:
/// 1. Create GameplayOverlay instance with optional custom handlers
/// 2. Call Render() in your game's ImGui frame
///
/// Example:
/// <code>
/// // Use default handlers
/// var gameplayOverlay = new GameplayOverlay(modalManager);
///
/// // Or inject custom handlers
/// var customInput = new MyCustomInputHandler();
/// var customHud = new MyCustomHudRenderer();
/// var gameplayOverlay = new GameplayOverlay(modalManager, customInput, customHud);
///
/// // In render loop:
/// imguiRenderer.NewFrame(deltaTime, width, height);
/// gameplayOverlay.Render(viewModel, heightMapTexturePtr, heightMapWidth, heightMapHeight);
/// imguiRenderer.Render();
/// </code>
/// </summary>
public class GameplayOverlay
{
    // UI Components (panels)
    private readonly WorldInfoPanel _worldInfoPanel;
    private readonly MapViewPanel _mapViewPanel;
    private readonly AvatarActionsPanel _avatarActionsPanel;
    private readonly DevToolsPanel _devToolsPanel;

    // Modal system
    private readonly ModalManager _modalManager;

    // Extensibility components
    private readonly IInputHandler _inputHandler;
    private readonly IHudRenderer _hudRenderer;

    // Panel state - which panel is currently shown (game mode: one at a time)
    private ActivePanel _activePanel = ActivePanel.None;

    /// <summary>
    /// Gets or sets which panel is currently active.
    /// Setting to the same value toggles it off.
    /// </summary>
    public ActivePanel ActivePanel
    {
        get => _activePanel;
        set => _activePanel = value;
    }

    /// <summary>
    /// Gets the input handler used by this overlay.
    /// Use this to subscribe to events like PauseMenuRequested or check WasPauseMenuRequested.
    /// </summary>
    public IInputHandler InputHandler => _inputHandler;

    /// <summary>
    /// Create a GameplayOverlay with default input and HUD rendering.
    /// </summary>
    public GameplayOverlay(ModalManager modalManager)
        : this(modalManager, new DefaultInputHandler(), new DefaultHudRenderer())
    {
    }

    /// <summary>
    /// Create a GameplayOverlay with custom input and HUD rendering.
    /// This constructor enables full extensibility of the overlay's behavior.
    /// </summary>
    /// <param name="modalManager">Modal manager for handling dialogs</param>
    /// <param name="inputHandler">Custom input handler (null = use default)</param>
    /// <param name="hudRenderer">Custom HUD renderer (null = use default)</param>
    public GameplayOverlay(
        ModalManager modalManager,
        IInputHandler? inputHandler = null,
        IHudRenderer? hudRenderer = null)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
        _inputHandler = inputHandler ?? new DefaultInputHandler();
        _hudRenderer = hudRenderer ?? new DefaultHudRenderer();

        // Initialize panels
        _worldInfoPanel = new WorldInfoPanel();
        _mapViewPanel = new MapViewPanel();
        _avatarActionsPanel = new AvatarActionsPanel();
        _devToolsPanel = new DevToolsPanel();
    }

    /// <summary>
    /// Toggle a panel on/off. If it's already active, close it. Otherwise open it.
    /// </summary>
    public void TogglePanel(ActivePanel panel)
    {
        if (_activePanel == panel)
            _activePanel = ActivePanel.None;
        else
            _activePanel = panel;
    }

    /// <summary>
    /// Close all panels.
    /// </summary>
    public void CloseAllPanels()
    {
        _activePanel = ActivePanel.None;
    }

    /// <summary>
    /// Render the gameplay overlay.
    /// Call this during your ImGui frame (between NewFrame and Render).
    /// </summary>
    /// <param name="viewModel">Main view model with world/avatar state</param>
    /// <param name="heightMapTexturePtr">DirectX texture pointer for heightmap</param>
    /// <param name="heightMapWidth">Heightmap width in pixels</param>
    /// <param name="heightMapHeight">Heightmap height in pixels</param>
    public void Render(MainViewModel viewModel, nint heightMapTexturePtr, int heightMapWidth, int heightMapHeight)
    {
        if (viewModel == null)
            return;

        // Process input through the injected handler
        var inputContext = new InputContext
        {
            IsModalActive = _modalManager.HasActiveModal(),
            IsTextInputActive = ImGui.GetIO().WantTextInput,
            ActivePanel = _activePanel,
            HasMap = viewModel.HeightMapImage != null,
            TogglePanelAction = TogglePanel,
            CloseAllPanelsAction = CloseAllPanels
        };
        _inputHandler.ProcessInput(inputContext);

        // Render the HUD through the injected renderer
        var io = ImGui.GetIO();
        _hudRenderer.Render(viewModel, _activePanel, io.DisplaySize);

        // Render the active panel (if any)
        switch (_activePanel)
        {
            case ActivePanel.Map:
                RenderMapPanel(viewModel, heightMapTexturePtr, heightMapWidth, heightMapHeight);
                break;
            case ActivePanel.Character:
                RenderCharacterPanel(viewModel);
                break;
            case ActivePanel.WorldInfo:
                RenderWorldInfoPanel(viewModel);
                break;
            case ActivePanel.DevTools:
                RenderDevToolsPanel(viewModel);
                break;
            case ActivePanel.None:
            default:
                // No panel open - just the 3D world
                break;
        }

        // Render all modals (always on top)
        _modalManager.Render(viewModel);
    }

    /// <summary>
    /// Render the Map panel (full screen with consistent margins).
    /// </summary>
    private void RenderMapPanel(MainViewModel viewModel, nint heightMapTexturePtr, int heightMapWidth, int heightMapHeight)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Full screen with consistent 10px margins (leaving room for HUD bar at bottom)
        var margin = 10f;
        var hudHeight = 40f;
        var panelX = margin;
        var panelY = margin;
        var panelWidth = displaySize.X - (margin * 2);
        var panelHeight = displaySize.Y - hudHeight - (margin * 3); // top margin + bottom margin + margin above HUD

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("Map [M]", windowFlags))
        {
            _mapViewPanel.Render(viewModel, heightMapTexturePtr, heightMapWidth, heightMapHeight, _modalManager);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Render the Character panel (top-left, full height).
    /// </summary>
    private void RenderCharacterPanel(MainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Panel top-left, full height
        var panelWidth = 350f;
        var panelHeight = displaySize.Y - 60; // Leave room for HUD bar + margin
        var panelX = 10f;
        var panelY = 10f;

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("Character [C]", windowFlags))
        {
            _avatarActionsPanel.Render(viewModel, _modalManager);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Render the World Info panel (top-left, full height).
    /// </summary>
    private void RenderWorldInfoPanel(MainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Panel top-left, full height
        var panelWidth = 350f;
        var panelHeight = displaySize.Y - 60; // Leave room for HUD bar + margin
        var panelX = 10f;
        var panelY = 10f;

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("World Info [I]", windowFlags))
        {
            _worldInfoPanel.Render(viewModel);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Render the Dev Tools panel (top-left, full height).
    /// Only available when debugger is attached.
    /// </summary>
    private void RenderDevToolsPanel(MainViewModel viewModel)
    {
        // Double-check debugger is attached (safety check)
        if (!DevToolsPanel.IsAvailable)
        {
            _activePanel = ActivePanel.None;
            return;
        }

        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Panel top-left, full height
        var panelWidth = 350f;
        var panelHeight = displaySize.Y - 60; // Leave room for HUD bar + margin
        var panelX = 10f;
        var panelY = 10f;

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        // Dev tools has a distinct orange-tinted background
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.08f, 0.06f, 0.95f));

        if (ImGui.Begin("Dev Tools [Ins]", windowFlags))
        {
            _devToolsPanel.Render(viewModel, _modalManager);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

}
