using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Presentation.UI.Components.Modals;
using Ambient.Saga.Presentation.UI.Components.Panels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components;

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
    /// <summary>Journal panel (press J) - shows world info, catalog, legends</summary>
    Journal
}

/// <summary>
/// GAME-REUSABLE: Main ImGui gameplay overlay for the 3D world.
///
/// This component displays the tactical world view including:
/// - Toggle-based panel system (M=Map, C=Character, J=Journal)
/// - Status bar with hotkey hints
/// - All interactive modals (Dialogue, Battle, Trade, Loot, Quest, etc.)
///
/// KEYBOARD CONTROLS:
/// - M: Toggle Map panel (full-screen map with click-to-teleport)
/// - C: Toggle Character panel (avatar stats, inventory, quests, achievements)
/// - J: Toggle Journal panel (world info, legends, catalog)
/// - ESC: Close current panel
///
/// ARCHITECTURE:
/// This is the complete game UI that ANY 3D application can use to interact with the Ambient world.
/// It provides view/interaction layer over the world, but is NOT the world itself.
///
/// INTEGRATION PATTERN:
/// 1. Create GameplayOverlay instance
/// 2. Initialize with ViewModel and DirectX device
/// 3. Call Render() in your game's ImGui frame
///
/// Example:
/// <code>
/// var gameplayOverlay = new GameplayOverlay(modalManager);
/// gameplayOverlay.Initialize(viewModel, device);
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

    // Modal system
    private readonly ModalManager _modalManager;

    // Panel state - which panel is currently shown (game mode: one at a time)
    private ActivePanel _activePanel = ActivePanel.None;

    // Track key states to detect press (not hold)
    private bool _mKeyWasPressed = false;
    private bool _cKeyWasPressed = false;
    private bool _jKeyWasPressed = false;
    private bool _escKeyWasPressed = false;

    /// <summary>
    /// Gets or sets which panel is currently active.
    /// Setting to the same value toggles it off.
    /// </summary>
    public ActivePanel ActivePanel
    {
        get => _activePanel;
        set => _activePanel = value;
    }

    public GameplayOverlay(ModalManager modalManager)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));

        // Initialize panels
        _worldInfoPanel = new WorldInfoPanel();
        _mapViewPanel = new MapViewPanel();
        _avatarActionsPanel = new AvatarActionsPanel();
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

        // Handle keyboard input for panel toggles (only when no modal is active and no text input focused)
        if (!_modalManager.HasActiveModal() && !ImGui.GetIO().WantTextInput)
        {
            HandlePanelHotkeys();
        }

        // Always render the HUD bar at the bottom of the screen
        RenderHudBar(viewModel);

        // Render the active panel (if any)
        switch (_activePanel)
        {
            case ActivePanel.Map:
                RenderMapPanel(viewModel, heightMapTexturePtr, heightMapWidth, heightMapHeight);
                break;
            case ActivePanel.Character:
                RenderCharacterPanel(viewModel);
                break;
            case ActivePanel.Journal:
                RenderJournalPanel(viewModel);
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
    /// Handle keyboard hotkeys for panel toggling.
    /// Uses edge detection (press, not hold) to prevent rapid toggling.
    /// </summary>
    private void HandlePanelHotkeys()
    {
        // M key - Map
        bool mKeyDown = ImGui.IsKeyDown(ImGuiKey.M);
        if (mKeyDown && !_mKeyWasPressed)
        {
            TogglePanel(ActivePanel.Map);
        }
        _mKeyWasPressed = mKeyDown;

        // C key - Character
        bool cKeyDown = ImGui.IsKeyDown(ImGuiKey.C);
        if (cKeyDown && !_cKeyWasPressed)
        {
            TogglePanel(ActivePanel.Character);
        }
        _cKeyWasPressed = cKeyDown;

        // J key - Journal
        bool jKeyDown = ImGui.IsKeyDown(ImGuiKey.J);
        if (jKeyDown && !_jKeyWasPressed)
        {
            TogglePanel(ActivePanel.Journal);
        }
        _jKeyWasPressed = jKeyDown;

        // ESC key - Close current panel
        bool escKeyDown = ImGui.IsKeyDown(ImGuiKey.Escape);
        if (escKeyDown && !_escKeyWasPressed && _activePanel != ActivePanel.None)
        {
            _activePanel = ActivePanel.None;
        }
        _escKeyWasPressed = escKeyDown;
    }

    /// <summary>
    /// Render the always-visible HUD bar at the bottom of the screen.
    /// Shows hotkey hints and basic status info.
    /// </summary>
    private void RenderHudBar(MainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Position at bottom of screen
        var hudHeight = 40f;
        ImGui.SetNextWindowPos(new Vector2(0, displaySize.Y - hudHeight));
        ImGui.SetNextWindowSize(new Vector2(displaySize.X, hudHeight));

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse |
                          ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.15f, 0.9f));

        if (ImGui.Begin("##HudBar", windowFlags))
        {
            // Left side: Hotkey hints
            RenderHotkeyHint("M", "Map", _activePanel == ActivePanel.Map);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1), "|");
            ImGui.SameLine();
            RenderHotkeyHint("C", "Character", _activePanel == ActivePanel.Character);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1), "|");
            ImGui.SameLine();
            RenderHotkeyHint("J", "Journal", _activePanel == ActivePanel.Journal);

            // Center: Status message
            if (!string.IsNullOrEmpty(viewModel.StatusMessage))
            {
                ImGui.SameLine(displaySize.X / 2 - 100);
                ImGui.Text(viewModel.StatusMessage);
            }

            // Right side: Avatar position (if available)
            if (viewModel.HasAvatarPosition)
            {
                var posText = $"({viewModel.AvatarLatitude:F2}, {viewModel.AvatarLongitude:F2})";
                var textWidth = ImGui.CalcTextSize(posText).X;
                ImGui.SameLine(displaySize.X - textWidth - 20);
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1), posText);
            }

            if (viewModel.IsLoading)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Loading...");
            }
        }
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Render a hotkey hint in the HUD bar.
    /// </summary>
    private void RenderHotkeyHint(string key, string label, bool isActive)
    {
        // Key box
        var keyColor = isActive
            ? new Vector4(0.3f, 0.7f, 0.3f, 1f)  // Green when active
            : new Vector4(0.3f, 0.3f, 0.3f, 1f); // Gray when inactive

        var textColor = isActive
            ? new Vector4(1f, 1f, 1f, 1f)        // White when active
            : new Vector4(0.7f, 0.7f, 0.7f, 1f); // Light gray when inactive

        ImGui.PushStyleColor(ImGuiCol.Button, keyColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, keyColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, keyColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));

        ImGui.Button(key, new Vector2(22, 22));

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        ImGui.TextColored(textColor, label);
    }

    /// <summary>
    /// Render the Map panel (full screen overlay).
    /// </summary>
    private void RenderMapPanel(MainViewModel viewModel, nint heightMapTexturePtr, int heightMapWidth, int heightMapHeight)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Large centered window for map
        var panelWidth = displaySize.X * 0.85f;
        var panelHeight = displaySize.Y * 0.85f - 40; // Leave room for HUD bar
        var panelX = (displaySize.X - panelWidth) / 2;
        var panelY = (displaySize.Y - 40 - panelHeight) / 2;

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
    /// Render the Character panel (slide from right).
    /// </summary>
    private void RenderCharacterPanel(MainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Panel on right side
        var panelWidth = 350f;
        var panelHeight = displaySize.Y - 60; // Leave room for HUD bar + margin
        var panelX = displaySize.X - panelWidth - 10;
        var panelY = 10;

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
    /// Render the Journal panel (slide from left).
    /// </summary>
    private void RenderJournalPanel(MainViewModel viewModel)
    {
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        // Panel on left side
        var panelWidth = 350f;
        var panelHeight = displaySize.Y - 60; // Leave room for HUD bar + margin
        var panelX = 10;
        var panelY = 10;

        ImGui.SetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("Journal [J]", windowFlags))
        {
            _worldInfoPanel.Render(viewModel);
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

}
