using System;
using System.Runtime.InteropServices;
using ImGuiNET;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.D3DCompiler;
using SharpDX.Mathematics.Interop;
using Device = SharpDX.Direct3D11.Device;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Ambient.Saga.Presentation.UI.Services
{
    /// <summary>
    /// Direct3D 11 renderer - renders a spinning colored triangle.
    /// </summary>
    public class D3D11Renderer : IDisposable
    {
        private Device _device;
        private DeviceContext _context;
        private SwapChain _swapChain;
        private RenderTargetView _renderTargetView;
        private Buffer _vertexBuffer;
        private VertexShader _vertexShader;
        private PixelShader _pixelShader;
        private InputLayout _inputLayout;
        private bool _ownsDevice; // Track if we created the device or it was passed in

        private float _rotation = 0f;

        public Device Device => _device;

        // Initialize with a new device (original behavior)
        public void Initialize(nint windowHandle, int width, int height)
        {
            InitializeWithDevice(null, windowHandle, width, height);
            _ownsDevice = true;
        }

        // Initialize with an existing shared device (new pattern for multiple swap chains)
        public void InitializeWithSharedDevice(Device sharedDevice, nint windowHandle, int width, int height)
        {
            if (sharedDevice == null) throw new ArgumentNullException(nameof(sharedDevice));
            InitializeWithDevice(sharedDevice, windowHandle, width, height);
            _ownsDevice = false;
        }

        private void InitializeWithDevice(Device? existingDevice, nint windowHandle, int width, int height)
        {
            // Swap chain description
            var swapChainDesc = new SwapChainDescription
            {
                BufferCount = 1,
                ModeDescription = new ModeDescription(width, height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                IsWindowed = true,
                OutputHandle = windowHandle,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput
            };

            if (existingDevice == null)
            {
                // Create new device and swap chain together (original behavior)
                Device.CreateWithSwapChain(
                    DriverType.Hardware,
                    DeviceCreationFlags.None,
                    swapChainDesc,
                    out _device,
                    out _swapChain);
            }
            else
            {
                // Use existing device and create only swap chain
                _device = existingDevice;

                using (var factory = new Factory1())
                {
                    _swapChain = new SwapChain(factory, _device, swapChainDesc);
                }
            }

            _context = _device.ImmediateContext;

            // Create render target view
            using (var backBuffer = _swapChain.GetBackBuffer<Texture2D>(0))
            {
                _renderTargetView = new RenderTargetView(_device, backBuffer);
            }

            // Set viewport
            var viewport = new Viewport(0, 0, width, height, 0.0f, 1.0f);
            _context.Rasterizer.SetViewport(viewport);

            // Create vertex buffer (triangle with colors)
            var vertices = new[]
            {
                // Position (X, Y, Z), Color (R, G, B, A)
                new Vertex { Position = new Vector3(0.0f, 0.5f, 0.0f), Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f) },  // Top - Red
                new Vertex { Position = new Vector3(0.5f, -0.5f, 0.0f), Color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f) }, // Right - Green
                new Vertex { Position = new Vector3(-0.5f, -0.5f, 0.0f), Color = new Vector4(0.0f, 0.0f, 1.0f, 1.0f) } // Left - Blue
            };

            _vertexBuffer = Buffer.Create(_device, BindFlags.VertexBuffer, vertices);

            // Compile and create shaders
            var vertexShaderByteCode = ShaderBytecode.Compile(VertexShaderSource, "main", "vs_4_0");
            _vertexShader = new VertexShader(_device, vertexShaderByteCode);

            var pixelShaderByteCode = ShaderBytecode.Compile(PixelShaderSource, "main", "ps_4_0");
            _pixelShader = new PixelShader(_device, pixelShaderByteCode);

            // Create input layout
            _inputLayout = new InputLayout(_device, vertexShaderByteCode, new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("COLOR", 0, Format.R32G32B32A32_Float, 12, 0)
            });

            vertexShaderByteCode.Dispose();
            pixelShaderByteCode.Dispose();
        }

        public void Render()
        {
            // Update rotation
            _rotation += 0.02f;
            if (_rotation > Math.PI * 2) _rotation -= (float)(Math.PI * 2);

            // Recreate vertices with rotation applied (CPU side for simplicity)
            var cos = (float)Math.Cos(_rotation);
            var sin = (float)Math.Sin(_rotation);

            var vertices = new[]
            {
                // Rotate each vertex
                new Vertex {
                    Position = new Vector3(
                        0.0f * cos - 0.5f * sin,
                        0.0f * sin + 0.5f * cos,
                        0.0f),
                    Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f)
                },
                new Vertex {
                    Position = new Vector3(
                        0.5f * cos - (-0.5f) * sin,
                        0.5f * sin + -0.5f * cos,
                        0.0f),
                    Color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f)
                },
                new Vertex {
                    Position = new Vector3(
                        -0.5f * cos - (-0.5f) * sin,
                        -0.5f * sin + -0.5f * cos,
                        0.0f),
                    Color = new Vector4(0.0f, 0.0f, 1.0f, 1.0f)
                }
            };

            // Update vertex buffer
            _context.UpdateSubresource(vertices, _vertexBuffer);

            // Clear render target to dark blue
            _context.ClearRenderTargetView(_renderTargetView, new RawColor4(0.1f, 0.1f, 0.3f, 1.0f));

            // Set render target
            _context.OutputMerger.SetRenderTargets(_renderTargetView);

            // Set input layout
            _context.InputAssembler.InputLayout = _inputLayout;
            _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            _context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_vertexBuffer, Utilities.SizeOf<Vertex>(), 0));

            // Set shaders
            _context.VertexShader.Set(_vertexShader);
            _context.PixelShader.Set(_pixelShader);

            // Draw triangle
            _context.Draw(3, 0);
        }

        public void Present()
        {
            _swapChain.Present(1, PresentFlags.None);
        }

        public void Resize(int width, int height)
        {
            if (_swapChain == null) return;

            // Release old render target
            _renderTargetView?.Dispose();

            // Resize buffers
            _swapChain.ResizeBuffers(1, width, height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);

            // Recreate render target
            using (var backBuffer = _swapChain.GetBackBuffer<Texture2D>(0))
            {
                _renderTargetView = new RenderTargetView(_device, backBuffer);
            }

            // Update viewport
            var viewport = new Viewport(0, 0, width, height, 0.0f, 1.0f);
            _context.Rasterizer.SetViewport(viewport);
        }

        public void Dispose()
        {
            _inputLayout?.Dispose();
            _pixelShader?.Dispose();
            _vertexShader?.Dispose();
            _vertexBuffer?.Dispose();
            _renderTargetView?.Dispose();
            _swapChain?.Dispose();

            // Only dispose device if we created it
            if (_ownsDevice)
            {
                _context?.Dispose();
                _device?.Dispose();
            }
        }

        // Vertex structure
        private struct Vertex
        {
            public Vector3 Position;
            public Vector4 Color;
        }

        // Simple pass-through vertex shader
        private const string VertexShaderSource = @"
            struct VS_INPUT
            {
                float3 pos : POSITION;
                float4 col : COLOR;
            };

            struct PS_INPUT
            {
                float4 pos : SV_POSITION;
                float4 col : COLOR;
            };

            PS_INPUT main(VS_INPUT input)
            {
                PS_INPUT output;
                output.pos = float4(input.pos, 1.0);
                output.col = input.col;
                return output;
            }";

        // Simple pixel shader that outputs vertex colors
        private const string PixelShaderSource = @"
            struct PS_INPUT
            {
                float4 pos : SV_POSITION;
                float4 col : COLOR;
            };

            float4 main(PS_INPUT input) : SV_Target
            {
                return input.col;
            }";
    }
}
