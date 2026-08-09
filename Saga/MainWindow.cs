using Ambient.Rpg.Presentation.UI.ViewModels;
using Ambient.Rpg.Ui.ViewModels;
using ImGuiNET;
using Steamworks;
using Ambient.Rpg.Ui.Services;
using Ambient.Saga.Services;
using Ambient.Rpg.Rendering.DirectX;
using Ambient.Rpg.Ui.Components.Modals;

namespace Ambient.Saga.WindowsUI;

public partial class MainWindow : Form
{
    private D3D11Renderer? _renderer;
    private ImGuiRendererDX11? _imguiRenderer;
    private WorldMapUI? _worldMapUI;
    private bool _isRendering = false;
    private DateTime _lastFrameTime = DateTime.Now;
    private RpgMainViewModel _viewModel;
    private Panel _mainPanel;
    private ModalManager _modalManager;

    public MainWindow(RpgMainViewModel viewModel, WorldMapUI worldMapUI, ModalManager modalManager)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _worldMapUI = worldMapUI ?? throw new ArgumentNullException(nameof(worldMapUI));
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));

        InitializeComponent();

        // Set window properties - 75% of screen size, centered
        this.Text = "Ambient Saga Sandbox";
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        this.ClientSize = new System.Drawing.Size(
            (int)(workingArea.Width * 0.75),
            (int)(workingArea.Height * 0.75));
        this.StartPosition = FormStartPosition.CenterScreen;

        // Create main panel for 3D rendering with ImGui overlay
        _mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = System.Drawing.Color.DarkSlateGray
        };
        this.Controls.Add(_mainPanel);

        // Initialize D3D11 renderer
        _renderer = new D3D11Renderer();
        _renderer.Initialize(_mainPanel.Handle, _mainPanel.ClientSize.Width, _mainPanel.ClientSize.Height);

        // Initialize ImGui renderer
        _imguiRenderer = new ImGuiRendererDX11(this.Handle, _renderer.Device, _mainPanel.ClientSize.Width, _mainPanel.ClientSize.Height);

        // Wire up mouse events for ImGui input
        _mainPanel.MouseMove += (s, e) => _imguiRenderer?.UpdateMousePos(e.X, e.Y);
        _mainPanel.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) _imguiRenderer?.UpdateMouseButton(0, true);
            else if (e.Button == MouseButtons.Right) _imguiRenderer?.UpdateMouseButton(1, true);
            else if (e.Button == MouseButtons.Middle) _imguiRenderer?.UpdateMouseButton(2, true);
        };
        _mainPanel.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) _imguiRenderer?.UpdateMouseButton(0, false);
            else if (e.Button == MouseButtons.Right) _imguiRenderer?.UpdateMouseButton(1, false);
            else if (e.Button == MouseButtons.Middle) _imguiRenderer?.UpdateMouseButton(2, false);
        };
        _mainPanel.MouseWheel += (s, e) => _imguiRenderer?.UpdateMouseWheel(e.Delta / 120.0f);

        // Wire up keyboard events for ImGui input
        this.KeyPreview = true;
        var pressedKeys = new HashSet<Keys>();
        this.KeyDown += (s, e) =>
        {
            // Ignore key repeats - only send first press
            if (pressedKeys.Contains(e.KeyCode))
                return;
            pressedKeys.Add(e.KeyCode);

            // Send modifier keys first
            _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModCtrl, e.Control);
            _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModShift, e.Shift);
            _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModAlt, e.Alt);

            // Send the actual key
            var imguiKey = WinFormsKeyMapper.MapKeyToImGui(e.KeyCode);
            if (imguiKey != ImGuiNET.ImGuiKey.None)
                _imguiRenderer?.UpdateKeyState(imguiKey, true);
        };
        this.KeyUp += (s, e) =>
        {
            pressedKeys.Remove(e.KeyCode);

            // Send modifier key states
            _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModCtrl, e.Control);
            _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModShift, e.Shift);
            _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModAlt, e.Alt);

            // Send the actual key release
            var imguiKey = WinFormsKeyMapper.MapKeyToImGui(e.KeyCode);
            if (imguiKey != ImGuiNET.ImGuiKey.None)
                _imguiRenderer?.UpdateKeyState(imguiKey, false);
        };
        this.KeyPress += (s, e) =>
        {
            if (e.KeyChar >= 32) // Printable characters
                _imguiRenderer?.AddInputCharacter(e.KeyChar);
        };

        // Handle resize
        _mainPanel.Resize += (s, e) =>
        {
            if (_mainPanel.ClientSize.Width > 0 && _mainPanel.ClientSize.Height > 0)
            {
                _renderer?.Resize(_mainPanel.ClientSize.Width, _mainPanel.ClientSize.Height);
            }
        };

        // Initialize World Map UI (without loading a world - WorldSelectionScreen will appear first)
        var textureProvider = new D3D11TextureProvider(_renderer.Device);
        _worldMapUI?.Initialize(_viewModel, textureProvider);

        // Subscribe to quit request from pause menu
        _modalManager.QuitRequested += OnQuitRequested;

        // Subscribe to dialogue requests from MainViewModel
        _viewModel.DialogueRequested += OnDialogueRequested;

        // Subscribe to proximity assaults: a hostile (not Disengaged) character
        // initiates battle when the avatar lands inside its ApproachRadius
        _viewModel.AssaultRequested += OnAssaultRequested;

        // Offline sandbox — no server pull needed, enable saga processing immediately on session ready
        _viewModel.SessionReady += (_, _) => _viewModel.IsReadyForArcProcessing = true;

        // Open world selection screen at startup
        _modalManager.OpenWorldSelection();

        // Start render loop
        _isRendering = true;
        System.Windows.Forms.Application.Idle += OnApplicationIdle;

        // Clean up on close
        this.FormClosing += OnFormClosing;
    }
    
    private void OnQuitRequested()
    {
        // Close the form - this triggers FormClosing cleanup
        this.Close();
    }

    private void OnDialogueRequested(CharacterViewModel character)
    {
        // Open the dialogue modal for this character
        _modalManager.OpenCharacterInteraction(character, _viewModel);
    }

    private void OnAssaultRequested(CharacterViewModel character)
    {
        // Straight into battle, same as clicking Attack on the character (menace
        // speech is the battle_opening dialogue trigger). Declined while another
        // modal is open — the view model re-raises on its next 1 s check.
        _modalManager.TryOpenAssault(character, _viewModel);
    }

    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        if (!_isRendering) return;

        // Render continuously when idle
        while (IsApplicationIdle())
        {
            if (_renderer != null && _imguiRenderer != null)
            {
                // Calculate delta time
                var now = DateTime.Now;
                var deltaTime = (float)(now - _lastFrameTime).TotalSeconds;
                _lastFrameTime = now;

                // Run Steam callbacks every frame
                if (ServiceProviderSetup.IsSteamInitialized)
                {
                    SteamAPI.RunCallbacks();
                }

                // Update World Map UI (battle logic, modals, etc.)
                _worldMapUI?.Update(deltaTime);

                // Render 3D scene (spinning triangle background)
                _renderer.Render();

                // Start ImGui frame
                _imguiRenderer.NewFrame(deltaTime, _mainPanel.ClientSize.Width, _mainPanel.ClientSize.Height);

                // Render World Map UI components
                _worldMapUI?.Render();

                // Finish ImGui rendering
                _imguiRenderer.Render();

                // Present
                _renderer.Present();
            }
        }
    }

    private bool IsApplicationIdle()
    {
        NativeMessage msg;
        return !PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _isRendering = false;
        System.Windows.Forms.Application.Idle -= OnApplicationIdle;

        // Dispose WorldMapUI (releases heightmap textures)
        _worldMapUI?.Dispose();

        // Dispose renderers
        _imguiRenderer?.Dispose();
        _renderer?.Dispose();
    }

    // P/Invoke for message pump
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Handle;
        public uint Message;
        public IntPtr WParameter;
        public IntPtr LParameter;
        public uint Time;
        public System.Drawing.Point Location;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);
}
