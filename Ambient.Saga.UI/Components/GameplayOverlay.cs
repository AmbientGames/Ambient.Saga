using Ambient.Domain.Entities;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Panels;
using Ambient.Saga.UI.Components.Modals;
using Ambient.Saga.UI.Components.Input;
using Ambient.Saga.UI.Components.Rendering;
using Ambient.Saga.UI.Components.Overlay;

namespace Ambient.Saga.UI.Components;

using Ambient.Saga.UI;

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
    /// <summary>Character panel (press C) - shows avatar stats, affinities, party</summary>
    Character,
    /// <summary>Inventory panel (press I) - shows equipment, items, spells</summary>
    Inventory,
    /// <summary>Journal panel (press J) - RPG journal with quests, bestiary, world info</summary>
    Journal,
    /// <summary>World Info panel (press F1) - shows world catalog (debugger only)</summary>
    WorldInfo,
    /// <summary>Dev Tools panel (press F12) - only available when debugger attached</summary>
    DevTools
}

/// <summary>
/// GAME-REUSABLE: Main ImGui gameplay overlay for the 3D world.
//>
/// This component displays the tactical world view including:
/// - Toggle-based panel system (M=Map, C=Character, I=Inventory, J=Journal)
/// - Status bar with hotkey hints
/// - All interactive modals (Dialogue, Battle, Trade, Loot, Quest, etc.)
///
/// EXTENSIBILITY:
/// The overlay now supports custom input handling and HUD rendering through:
/// - IInputHandler: Customize keyboard/mouse controls (default: M/C/I/J/ESC)
/// - IHudRenderer: Customize the always-visible HUD bar (default: bottom bar with hotkeys)
///
/// KEYBOARD CONTROLS (Default):
/// - M: Toggle Map panel (quarter-screen map with click-to-teleport)
/// - C: Toggle Character panel (avatar stats, affinities, party)
/// - I: Toggle Inventory panel (equipment, items, spells)
/// - J: Toggle Journal panel (quests, bestiary, achievements)
/// - F1/F12: Developer tools (debugger only)
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
    private readonly CharacterPanel _characterPanel;
    private readonly InventoryPanel _inventoryPanel;
    private readonly DevToolsPanel _devToolsPanel;
    private readonly JournalPanel _journalPanel;

    // Modal system
    private readonly ModalManager _modalManager;

    // Extensibility components
    private readonly IInputHandler _inputHandler;
    private readonly IHudRenderer _hudRenderer;

    // Message overlay for floating toast-style notifications
    private readonly MessageOverlay _messageOverlay;

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
    /// Fired each frame when the map panel is being rendered.
    /// Use this to update procedural map data when the map is visible.
    /// </summary>
    public event Action? MapPanelRendering;

    /// <summary>
    /// Gets the input handler used by this overlay.
    /// Use this to subscribe to events like PauseMenuRequested or check WasPauseMenuRequested.
    /// </summary>
    public IInputHandler InputHandler => _inputHandler;

    /// <summary>
    /// Gets the message overlay for adding floating toast-style notifications.
    /// Use AddMessage() to display messages that stack from bottom-right and fade out.
    /// </summary>
    public MessageOverlay MessageOverlay => _messageOverlay;

    /// <summary>
    /// Create a GameplayOverlay with default input and HUD rendering.
    /// Uses SectionedHudRenderer for modular, extensible HUD.
    /// </summary>
    public GameplayOverlay(ModalManager modalManager)
        : this(modalManager, new DefaultInputHandler(), new SectionedHudRenderer())
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
        _messageOverlay = new MessageOverlay();

        // Initialize panels
        _worldInfoPanel = new WorldInfoPanel();
        _mapViewPanel = new MapViewPanel();
        _characterPanel = new CharacterPanel();
        _inventoryPanel = new InventoryPanel();
        _devToolsPanel = new DevToolsPanel();
        _journalPanel = new JournalPanel();
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
    public void Render(SagaMainViewModel viewModel, nint heightMapTexturePtr, int heightMapWidth, int heightMapHeight)
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
        // SectionedHudRenderer handles all 5 regions: TopLeft, TopRight, BottomLeft, BottomCenter, BottomRight
        var io = ImGui.GetIO();
        _hudRenderer.Render(viewModel, _activePanel, io.DisplaySize);

        // Drain any pending toast messages from the viewModel
        foreach (var (text, type, duration) in viewModel.DrainToastMessages())
        {
            _messageOverlay.AddMessage(text, type, duration);
        }

        // Render message overlay (floating toasts above HUD)
        // Calculate HUD height for positioning (matches DefaultHudRenderer calculation)
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;
        _messageOverlay.Render(io.DisplaySize, hudHeight);

        // Render the active panel (if any)
        switch (_activePanel)
        {
            case ActivePanel.Map:
                RenderMapPanel(viewModel, heightMapTexturePtr, heightMapWidth, heightMapHeight);
                break;
            case ActivePanel.Character:
                RenderCharacterPanel(viewModel);
                break;
            case ActivePanel.Inventory:
                RenderInventoryPanel(viewModel);
                break;
            case ActivePanel.WorldInfo:
                RenderWorldInfoPanel(viewModel);
                break;
            case ActivePanel.Journal:
                RenderJournalPanel(viewModel);
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
    private void RenderMapPanel(SagaMainViewModel viewModel, nint heightMapTexturePtr, int heightMapWidth, int heightMapHeight)
    {
        // Fire event for procedural map updates
        MapPanelRendering?.Invoke();

        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;
        var scale = UIConstants.DpiScale;

        // Full screen with consistent margins (leaving room for HUD bar at bottom)
        var margin = 10f * scale;
        // Calculate HUD height dynamically (same as DefaultHudRenderer)
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;
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
    /// Render the Character panel (full screen with consistent margins).
    /// </summary>
    private void RenderCharacterPanel(SagaMainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;
        var scale = UIConstants.DpiScale;

        // Full screen with consistent margins (leaving room for HUD bar at bottom)
        var margin = 10f * scale;
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;
        var panelX = margin;
        var panelY = margin;
        var panelWidth = displaySize.X - (margin * 2);
        var panelHeight = displaySize.Y - hudHeight - (margin * 3);

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("Character [C]", windowFlags))
        {
            _characterPanel.Render(viewModel, _modalManager);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Render the Inventory panel (full screen with consistent margins).
    /// </summary>
    private void RenderInventoryPanel(SagaMainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;
        var scale = UIConstants.DpiScale;

        // Full screen with consistent margins (leaving room for HUD bar at bottom)
        var margin = 10f * scale;
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;
        var panelX = margin;
        var panelY = margin;
        var panelWidth = displaySize.X - (margin * 2);
        var panelHeight = displaySize.Y - hudHeight - (margin * 3);

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("Inventory [I]", windowFlags))
        {
            _inventoryPanel.Render(viewModel);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Render the World Info panel (full screen with consistent margins).
    /// </summary>
    private void RenderWorldInfoPanel(SagaMainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;
        var scale = UIConstants.DpiScale;

        // Full screen with consistent margins (leaving room for HUD bar at bottom)
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;

        var margin = 10f * scale;
        var panelWidth = displaySize.X - (margin * 2);
        var panelHeight = displaySize.Y - hudHeight - (margin * 3); // Leave room for HUD bar + margins
        var panelX = margin;
        var panelY = margin;

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("World Info [F1]", windowFlags))
        {
            _worldInfoPanel.Render(viewModel);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Render the Dev Tools panel (full screen with consistent margins).
    /// Only available when debugger is attached.
    /// </summary>
    private void RenderDevToolsPanel(SagaMainViewModel viewModel)
    {
        // Double-check debugger is attached (safety check)
        if (!DevToolsPanel.IsAvailable)
        {
            _activePanel = ActivePanel.None;
            return;
        }

        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;
        var scale = UIConstants.DpiScale;

        // Full screen with consistent margins (leaving room for HUD bar at bottom)
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;

        var margin = 10f * scale;
        var panelWidth = displaySize.X - (margin * 2);
        var panelHeight = displaySize.Y - hudHeight - (margin * 3); // Leave room for HUD bar + margins
        var panelX = margin;
        var panelY = margin;

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        // Dev tools has a distinct orange-tinted background
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.08f, 0.06f, 0.95f));

        if (ImGui.Begin("Dev Tools [F12]", windowFlags))
        {
            _devToolsPanel.Render(viewModel, _modalManager);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Render the Journal panel (full screen with consistent margins).
    /// Shows quests, bestiary, atlas, history, and achievements in columns.
    /// </summary>
    private void RenderJournalPanel(SagaMainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;
        var scale = UIConstants.DpiScale;

        // Full screen with consistent margins (leaving room for HUD bar at bottom)
        var margin = 10f * scale;
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;
        var panelX = margin;
        var panelY = margin;
        var panelWidth = displaySize.X - (margin * 2);
        var panelHeight = displaySize.Y - hudHeight - (margin * 3);

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("Journal [J]", windowFlags))
        {
            _journalPanel.Render(viewModel, _modalManager);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

}
