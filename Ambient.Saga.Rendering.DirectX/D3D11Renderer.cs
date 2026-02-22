using System;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.D3DCompiler;
using SharpDX.Mathematics.Interop;
using Device = SharpDX.Direct3D11.Device;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Ambient.Saga.Rendering.DirectX;

/// <summary>
/// Direct3D 11 renderer - renders a spinning colored triangle as a demo/placeholder background.
/// </summary>
public class D3D11Renderer : IDisposable
{
    private Device _device = null!;
    private DeviceContext _context = null!;
    private SwapChain _swapChain = null!;
    private RenderTargetView _renderTargetView = null!;
    private Buffer _vertexBuffer = null!;
    private VertexShader _vertexShader = null!;
    private PixelShader _pixelShader = null!;
    private InputLayout _inputLayout = null!;
    private bool _ownsDevice;

    private float _rotation = 0f;

    public Device Device => _device;

    /// <summary>
    /// Initialize with a new device (creates D3D11 device internally).
    /// </summary>
    public void Initialize(nint windowHandle, int width, int height)
    {
        InitializeWithDevice(null, windowHandle, width, height);
        _ownsDevice = true;
    }

    /// <summary>
    /// Initialize with an existing shared device (for multiple swap chains scenario).
    /// </summary>
    public void InitializeWithSharedDevice(Device sharedDevice, nint windowHandle, int width, int height)
    {
        if (sharedDevice == null) throw new ArgumentNullException(nameof(sharedDevice));
        InitializeWithDevice(sharedDevice, windowHandle, width, height);
        _ownsDevice = false;
    }

    private void InitializeWithDevice(Device? existingDevice, nint windowHandle, int width, int height)
    {
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
            Device.CreateWithSwapChain(
                DriverType.Hardware,
                DeviceCreationFlags.None,
                swapChainDesc,
                out _device,
                out _swapChain);
        }
        else
        {
            _device = existingDevice;
            using (var factory = new Factory1())
            {
                _swapChain = new SwapChain(factory, _device, swapChainDesc);
            }
        }

        _context = _device.ImmediateContext;

        using (var backBuffer = _swapChain.GetBackBuffer<Texture2D>(0))
        {
            _renderTargetView = new RenderTargetView(_device, backBuffer);
        }

        var viewport = new Viewport(0, 0, width, height, 0.0f, 1.0f);
        _context.Rasterizer.SetViewport(viewport);

        var vertices = new[]
        {
            new Vertex { Position = new Vector3(0.0f, 0.5f, 0.0f), Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f) },
            new Vertex { Position = new Vector3(0.5f, -0.5f, 0.0f), Color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f) },
            new Vertex { Position = new Vector3(-0.5f, -0.5f, 0.0f), Color = new Vector4(0.0f, 0.0f, 1.0f, 1.0f) }
        };

        _vertexBuffer = Buffer.Create(_device, BindFlags.VertexBuffer, vertices);

        var vertexShaderByteCode = ShaderBytecode.Compile(VertexShaderSource, "main", "vs_4_0");
        _vertexShader = new VertexShader(_device, vertexShaderByteCode);

        var pixelShaderByteCode = ShaderBytecode.Compile(PixelShaderSource, "main", "ps_4_0");
        _pixelShader = new PixelShader(_device, pixelShaderByteCode);

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
        _rotation += 0.02f;
        if (_rotation > Math.PI * 2) _rotation -= (float)(Math.PI * 2);

        var cos = (float)Math.Cos(_rotation);
        var sin = (float)Math.Sin(_rotation);

        var vertices = new[]
        {
            new Vertex {
                Position = new Vector3(0.0f * cos - 0.5f * sin, 0.0f * sin + 0.5f * cos, 0.0f),
                Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f)
            },
            new Vertex {
                Position = new Vector3(0.5f * cos - (-0.5f) * sin, 0.5f * sin + -0.5f * cos, 0.0f),
                Color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f)
            },
            new Vertex {
                Position = new Vector3(-0.5f * cos - (-0.5f) * sin, -0.5f * sin + -0.5f * cos, 0.0f),
                Color = new Vector4(0.0f, 0.0f, 1.0f, 1.0f)
            }
        };

        _context.UpdateSubresource(vertices, _vertexBuffer);
        _context.ClearRenderTargetView(_renderTargetView, new RawColor4(0.1f, 0.1f, 0.3f, 1.0f));
        _context.OutputMerger.SetRenderTargets(_renderTargetView);
        _context.InputAssembler.InputLayout = _inputLayout;
        _context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        _context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_vertexBuffer, Utilities.SizeOf<Vertex>(), 0));
        _context.VertexShader.Set(_vertexShader);
        _context.PixelShader.Set(_pixelShader);
        _context.Draw(3, 0);
    }

    public void Present()
    {
        _swapChain.Present(1, PresentFlags.None);
    }

    public void Resize(int width, int height)
    {
        if (_swapChain == null) return;

        _renderTargetView?.Dispose();
        _swapChain.ResizeBuffers(1, width, height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);

        using (var backBuffer = _swapChain.GetBackBuffer<Texture2D>(0))
        {
            _renderTargetView = new RenderTargetView(_device, backBuffer);
        }

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

        if (_ownsDevice)
        {
            _context?.Dispose();
            _device?.Dispose();
        }
    }

    private struct Vertex
    {
        public Vector3 Position;
        public Vector4 Color;
    }

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
