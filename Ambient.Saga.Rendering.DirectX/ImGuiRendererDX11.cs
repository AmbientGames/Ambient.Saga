using Ambient.Saga.UI;
using Ambient.Saga.UI.Services;
using ImGuiNET;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Runtime.InteropServices;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace Ambient.Saga.Rendering.DirectX;

/// <summary>
/// ImGui renderer for DirectX 11.
/// Includes keyboard navigation and native Windows clipboard support.
/// </summary>
public class ImGuiRendererDX11 : IDisposable
{
    private readonly Device _device;
    private readonly DeviceContext _deviceContext;
    private Buffer? _vertexBuffer;
    private Buffer? _indexBuffer;
    private Buffer _constantBuffer = null!;
    private VertexShader _vertexShader = null!;
    private PixelShader _pixelShader = null!;
    private InputLayout _inputLayout = null!;
    private BlendState _blendState = null!;
    private RasterizerState _rasterizerState = null!;
    private DepthStencilState _depthStencilState = null!;
    private SamplerState _samplerState = null!;
    private ShaderResourceView _fontTextureView = null!;
    private ShaderResourceView? _boundTextureView;
    private readonly IntPtr _hwnd;

    private int _vertexBufferSize = 10000;
    private int _indexBufferSize = 30000;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private static float GetDpiScaleForWindow(IntPtr hwnd) => GetDpiForWindow(hwnd) / 96f;

    public ImGuiRendererDX11(IntPtr hwnd, Device device, int width, int height)
    {
        _hwnd = hwnd;
        _device = device;
        _deviceContext = device.ImmediateContext;

        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(width, height);
        io.DisplayFramebufferScale = new System.Numerics.Vector2(1, 1);
        io.DeltaTime = 1.0f / 60.0f;

        // Enable keyboard navigation
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        // Set up clipboard callbacks (native Windows clipboard support)
        ImGuiClipboard.Install();

        ImGuiTheme.ApplyTheme(ImGuiTheme.ThemePreset.DarkFantasy);
        UIConstants.LoadFonts(GetDpiScaleForWindow(hwnd));

        CreateDeviceObjects();
        CreateFontsTexture();
    }

    private void CreateDeviceObjects()
    {
        var vertexShaderCode = @"
            cbuffer vertexBuffer : register(b0)
            {
                float4x4 ProjectionMatrix;
            };
            struct VS_INPUT
            {
                float2 pos : POSITION;
                float2 uv  : TEXCOORD0;
                float4 col : COLOR0;
            };
            struct PS_INPUT
            {
                float4 pos : SV_POSITION;
                float4 col : COLOR0;
                float2 uv  : TEXCOORD0;
            };
            PS_INPUT main(VS_INPUT input)
            {
                PS_INPUT output;
                output.pos = mul(ProjectionMatrix, float4(input.pos.xy, 0.f, 1.f));
                output.col = input.col;
                output.uv  = input.uv;
                return output;
            }";

        using (var vertexShaderBlob = ShaderBytecode.Compile(vertexShaderCode, "main", "vs_4_0"))
        {
            _vertexShader = new VertexShader(_device, vertexShaderBlob);
            _inputLayout = new InputLayout(_device, vertexShaderBlob, new[]
            {
                new InputElement("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
                new InputElement("COLOR", 0, Format.R8G8B8A8_UNorm, 16, 0)
            });
        }

        var pixelShaderCode = @"
            struct PS_INPUT
            {
                float4 pos : SV_POSITION;
                float4 col : COLOR0;
                float2 uv  : TEXCOORD0;
            };
            sampler sampler0;
            Texture2D texture0;
            float4 main(PS_INPUT input) : SV_Target
            {
                float4 out_col = input.col * texture0.Sample(sampler0, input.uv);
                return out_col;
            }";

        using (var pixelShaderBlob = ShaderBytecode.Compile(pixelShaderCode, "main", "ps_4_0"))
        {
            _pixelShader = new PixelShader(_device, pixelShaderBlob);
        }

        _constantBuffer = new Buffer(_device, Utilities.SizeOf<Matrix>(), ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);

        var blendDesc = new BlendStateDescription { AlphaToCoverageEnable = false };
        blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            IsBlendEnabled = true,
            SourceBlend = BlendOption.SourceAlpha,
            DestinationBlend = BlendOption.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceAlphaBlend = BlendOption.One,
            DestinationAlphaBlend = BlendOption.InverseSourceAlpha,
            AlphaBlendOperation = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteMaskFlags.All
        };
        _blendState = new BlendState(_device, blendDesc);

        var rasterizerDesc = new RasterizerStateDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            IsScissorEnabled = true,
            IsDepthClipEnabled = true
        };
        _rasterizerState = new RasterizerState(_device, rasterizerDesc);

        var depthStencilDesc = new DepthStencilStateDescription
        {
            IsDepthEnabled = false,
            DepthWriteMask = DepthWriteMask.All,
            DepthComparison = Comparison.Always,
            IsStencilEnabled = false
        };
        _depthStencilState = new DepthStencilState(_device, depthStencilDesc);

        var samplerDesc = new SamplerStateDescription
        {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            MipLodBias = 0f,
            ComparisonFunction = Comparison.Always,
            MinimumLod = 0,
            MaximumLod = 0
        };
        _samplerState = new SamplerState(_device, samplerDesc);
    }

    private void CreateFontsTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out nint pixels, out var width, out var height, out var bytesPerPixel);

        var textureDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None
        };

        using (var texture = new Texture2D(_device, textureDesc, new DataRectangle(pixels, width * bytesPerPixel)))
        {
            _fontTextureView = new ShaderResourceView(_device, texture);
        }

        io.Fonts.SetTexID(_fontTextureView.NativePointer);
    }

    /// <summary>
    /// Rebuilds the fonts and font atlas texture at the window's current DPI.
    /// Call from WM_DPICHANGED / Form.DpiChanged, between frames (not inside NewFrame/Render).
    /// No-op when the effective scale is unchanged.
    /// </summary>
    public void RebuildFontAtlas()
    {
        var dpiScale = GetDpiScaleForWindow(_hwnd);
        if (Math.Abs(dpiScale - UIConstants.DpiScale) < 0.0001f)
            return;

        UIConstants.LoadFonts(dpiScale);
        _fontTextureView.Dispose();
        CreateFontsTexture();
    }

    public void UpdateMousePos(float x, float y)
    {
        var io = ImGui.GetIO();
        io.MousePos = new System.Numerics.Vector2(x, y);
    }

    public void UpdateMouseButton(int button, bool down)
    {
        var io = ImGui.GetIO();
        if (button >= 0 && button < io.MouseDown.Count)
        {
            io.MouseDown[button] = down;
        }
    }

    public void UpdateMouseWheel(float delta)
    {
        var io = ImGui.GetIO();
        io.MouseWheel += delta;
    }

    public void UpdateKeyState(ImGuiKey key, bool down)
    {
        var io = ImGui.GetIO();
        io.AddKeyEvent(key, down);
    }

    public void AddInputCharacter(char c)
    {
        var io = ImGui.GetIO();
        io.AddInputCharacter(c);
    }

    public void NewFrame(float deltaTime, int width, int height)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(width, height);
        io.DeltaTime = deltaTime;
        ImGui.NewFrame();
    }

    public void Render()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0)
            return;

        var oldBlendState = _deviceContext.OutputMerger.GetBlendState(out var oldBlendFactor, out var oldSampleMask);
        var oldDepthStencilState = _deviceContext.OutputMerger.GetDepthStencilState(out var oldStencilRef);
        var oldRasterizerState = _deviceContext.Rasterizer.State;

        if (_vertexBuffer == null || _vertexBufferSize < drawData.TotalVtxCount)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = drawData.TotalVtxCount + 5000;
            _vertexBuffer = new Buffer(_device, _vertexBufferSize * Utilities.SizeOf<ImDrawVert>(), ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
        }

        if (_indexBuffer == null || _indexBufferSize < drawData.TotalIdxCount)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = drawData.TotalIdxCount + 10000;
            _indexBuffer = new Buffer(_device, _indexBufferSize * sizeof(ushort), ResourceUsage.Dynamic, BindFlags.IndexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
        }

        var vtxResource = _deviceContext.MapSubresource(_vertexBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
        var idxResource = _deviceContext.MapSubresource(_indexBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);

        var vtxDst = vtxResource.DataPointer;
        var idxDst = idxResource.DataPointer;

        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            Utilities.CopyMemory(vtxDst, cmdList.VtxBuffer.Data, cmdList.VtxBuffer.Size * Utilities.SizeOf<ImDrawVert>());
            Utilities.CopyMemory(idxDst, cmdList.IdxBuffer.Data, cmdList.IdxBuffer.Size * sizeof(ushort));
            vtxDst += cmdList.VtxBuffer.Size * Utilities.SizeOf<ImDrawVert>();
            idxDst += cmdList.IdxBuffer.Size * sizeof(ushort);
        }

        _deviceContext.UnmapSubresource(_vertexBuffer, 0);
        _deviceContext.UnmapSubresource(_indexBuffer, 0);

        var L = drawData.DisplayPos.X;
        var R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        var T = drawData.DisplayPos.Y;
        var B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        var mvp = new Matrix(
            2.0f / (R - L), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (T - B), 0.0f, 0.0f,
            0.0f, 0.0f, 0.5f, 0.0f,
            (R + L) / (L - R), (T + B) / (B - T), 0.5f, 1.0f);

        _deviceContext.MapSubresource(_constantBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out var mappedResource);
        Utilities.Write(mappedResource.DataPointer, ref mvp);
        _deviceContext.UnmapSubresource(_constantBuffer, 0);

        _deviceContext.InputAssembler.InputLayout = _inputLayout;
        _deviceContext.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_vertexBuffer, Utilities.SizeOf<ImDrawVert>(), 0));
        _deviceContext.InputAssembler.SetIndexBuffer(_indexBuffer, Format.R16_UInt, 0);
        _deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        _deviceContext.VertexShader.Set(_vertexShader);
        _deviceContext.VertexShader.SetConstantBuffer(0, _constantBuffer);
        _deviceContext.PixelShader.Set(_pixelShader);
        _deviceContext.PixelShader.SetSampler(0, _samplerState);
        _deviceContext.OutputMerger.SetBlendState(_blendState);
        _deviceContext.OutputMerger.SetDepthStencilState(_depthStencilState);
        _deviceContext.Rasterizer.State = _rasterizerState;

        var vtxOffset = 0;
        var idxOffset = 0;

        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            for (var i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                var cmd = cmdList.CmdBuffer[i];

                _deviceContext.Rasterizer.SetScissorRectangle(
                    (int)cmd.ClipRect.X,
                    (int)cmd.ClipRect.Y,
                    (int)cmd.ClipRect.Z,
                    (int)cmd.ClipRect.W);

                // Non-owning wrapper reused across commands: constructing from a raw pointer
                // does not AddRef, so it must never be disposed — and allocating one per
                // command per frame churned the GC.
                if (_boundTextureView == null)
                    _boundTextureView = new ShaderResourceView(cmd.TextureId);
                else
                    _boundTextureView.NativePointer = cmd.TextureId;
                _deviceContext.PixelShader.SetShaderResource(0, _boundTextureView);

                _deviceContext.DrawIndexed((int)cmd.ElemCount, (int)(idxOffset + cmd.IdxOffset), (int)(vtxOffset + cmd.VtxOffset));
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }

        _deviceContext.OutputMerger.SetBlendState(oldBlendState, oldBlendFactor, oldSampleMask);
        _deviceContext.OutputMerger.SetDepthStencilState(oldDepthStencilState, oldStencilRef);
        _deviceContext.Rasterizer.State = oldRasterizerState;
        oldBlendState?.Dispose();
        oldDepthStencilState?.Dispose();
        oldRasterizerState?.Dispose();
    }

    public void Dispose()
    {
        ImGuiClipboard.Shutdown();

        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _constantBuffer?.Dispose();
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _inputLayout?.Dispose();
        _blendState?.Dispose();
        _rasterizerState?.Dispose();
        _depthStencilState?.Dispose();
        _samplerState?.Dispose();
        _fontTextureView?.Dispose();

        ImGui.DestroyContext();
    }
}
