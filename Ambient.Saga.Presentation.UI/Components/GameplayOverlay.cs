using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Presentation.UI.Components.Modals;
using Ambient.Saga.Presentation.UI.Components.Panels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components;

/// <summary>
/// GAME-REUSABLE: Main ImGui gameplay overlay for the 3D world.
///
/// This component displays the tactical world view including:
/// - Three-panel layout (WorldInfo | MapView | AvatarActions)
/// - Status bar
/// - All interactive modals (Dialogue, Battle, Trade, Loot, Quest, etc.)
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

    // Window state
    private bool _showWindow = true;

    public GameplayOverlay(ModalManager modalManager)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));

        // Initialize panels
        _worldInfoPanel = new WorldInfoPanel();
        _mapViewPanel = new MapViewPanel();
        _avatarActionsPanel = new AvatarActionsPanel();
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
        if (viewModel == null || !_showWindow)
            return;

        // Main game UI window
        ImGui.SetNextWindowSize(new Vector2(1400, 900), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new Vector2(20, 20), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("World Map", ref _showWindow, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        // Main three-column layout
        if (ImGui.BeginTable("WorldMapLayout", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("WorldInfo", ImGuiTableColumnFlags.WidthFixed, 250);
            ImGui.TableSetupColumn("MapView", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("AvatarActions", ImGuiTableColumnFlags.WidthFixed, 250);

            ImGui.TableNextRow();

            // Left column: World info
            ImGui.TableNextColumn();
            _worldInfoPanel.Render(viewModel);

            // Center column: Map view
            ImGui.TableNextColumn();
            _mapViewPanel.Render(viewModel, heightMapTexturePtr, heightMapWidth, heightMapHeight, _modalManager);

            // Right column: Avatar actions
            ImGui.TableNextColumn();
            _avatarActionsPanel.Render(viewModel, _modalManager);

            ImGui.EndTable();
        }

        // Bottom status bar
        ImGui.Separator();
        RenderStatusBar(viewModel);

        ImGui.End();

        // Render all modals
        _modalManager.Render(viewModel);
    }

    private void RenderStatusBar(MainViewModel viewModel)
    {
        ImGui.Text(viewModel.StatusMessage);

        if (viewModel.IsLoading)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Loading...");
        }
    }
}
