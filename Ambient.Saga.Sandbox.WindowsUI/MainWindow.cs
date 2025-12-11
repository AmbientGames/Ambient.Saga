using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Presentation.UI.Services;
using Ambient.Saga.Sandbox.WindowsUI.Services;
using ImGuiNET;
using Steamworks;

namespace Ambient.Saga.Sandbox.WindowsUI;

public partial class MainWindow : Form
{
    private D3D11Renderer? _renderer;
    private ImGuiRendererDX11? _imguiRenderer;
    private WorldMapUI? _worldMapUI;
    private bool _isRendering = false;
    private DateTime _lastFrameTime = DateTime.Now;
    private MainViewModel _viewModel;
    private Panel _mainPanel;

    public MainWindow(MainViewModel viewModel, WorldMapUI worldMapUI)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _worldMapUI = worldMapUI ?? throw new ArgumentNullException(nameof(worldMapUI));

        InitializeComponent();

        // Set window properties
        this.Text = "Ambient Game Sandbox";
        this.ClientSize = new System.Drawing.Size(1400, 900);
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
        _imguiRenderer = new ImGuiRendererDX11(_renderer.Device, _mainPanel.ClientSize.Width, _mainPanel.ClientSize.Height);

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
        this.KeyDown += (s, e) =>
        {
            var imguiKey = MapKeyToImGui(e.KeyCode);
            if (imguiKey != ImGuiNET.ImGuiKey.None)
                _imguiRenderer?.UpdateKeyState(imguiKey, true);

            // Handle modifier keys
            if (e.Control) _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModCtrl, true);
            if (e.Shift) _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModShift, true);
            if (e.Alt) _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModAlt, true);
        };
        this.KeyUp += (s, e) =>
        {
            var imguiKey = MapKeyToImGui(e.KeyCode);
            if (imguiKey != ImGuiNET.ImGuiKey.None)
                _imguiRenderer?.UpdateKeyState(imguiKey, false);

            // Handle modifier keys
            if (!e.Control) _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModCtrl, false);
            if (!e.Shift) _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModShift, false);
            if (!e.Alt) _imguiRenderer?.UpdateKeyState(ImGuiNET.ImGuiKey.ModAlt, false);
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

        // Start render loop
        _isRendering = true;
        System.Windows.Forms.Application.Idle += OnApplicationIdle;

        // Clean up on close
        this.FormClosing += OnFormClosing;
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

                // Update ViewModel (updates character positions, Sagas, etc.)
                _viewModel.Update(deltaTime);

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

    private static ImGuiKey MapKeyToImGui(Keys key) => key switch
    {
        Keys.Tab => ImGuiKey.Tab,
        Keys.Left => ImGuiKey.LeftArrow,
        Keys.Right => ImGuiKey.RightArrow,
        Keys.Up => ImGuiKey.UpArrow,
        Keys.Down => ImGuiKey.DownArrow,
        Keys.PageUp => ImGuiKey.PageUp,
        Keys.PageDown => ImGuiKey.PageDown,
        Keys.Home => ImGuiKey.Home,
        Keys.End => ImGuiKey.End,
        Keys.Insert => ImGuiKey.Insert,
        Keys.Delete => ImGuiKey.Delete,
        Keys.Back => ImGuiKey.Backspace,
        Keys.Space => ImGuiKey.Space,
        Keys.Enter => ImGuiKey.Enter,
        Keys.Escape => ImGuiKey.Escape,
        // Text editing keys
        Keys.A => ImGuiKey.A,
        Keys.C => ImGuiKey.C,
        Keys.V => ImGuiKey.V,
        Keys.X => ImGuiKey.X,
        Keys.Y => ImGuiKey.Y,
        Keys.Z => ImGuiKey.Z,
        // Panel hotkeys (M=Map, J=Journal)
        Keys.M => ImGuiKey.M,
        Keys.J => ImGuiKey.J,
        _ => ImGuiKey.None
    };
}
