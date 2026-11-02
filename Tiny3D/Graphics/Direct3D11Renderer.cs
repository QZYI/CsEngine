using System.Runtime.InteropServices;
using Tiny3D.Math;
using Tiny3D.Scene;
using Tiny3D.Tools;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using GameScene = Tiny3D.Scene.Scene;
using static Vortice.Direct3D11.D3D11;

namespace Tiny3D.Graphics;

internal sealed class Direct3D11Renderer : IDisposable, IScenePicker
{
    private const int VertexFloatCount = 17;
    private const int VertexStride = sizeof(float) * VertexFloatCount;
    private const string ShaderSource = """
        struct VertexInput
        {
            float3 position : POSITION;
            float4 color : COLOR;
            float3 shadowCoordinate : TEXCOORD0;
            float shadowBias : TEXCOORD1;
            float2 lighting : TEXCOORD2;
            float inverseCameraDepth : TEXCOORD3;
            float3 effectData : TEXCOORD4;
        };

        struct VertexOutput
        {
            float4 position : SV_POSITION;
            float4 color : COLOR;
            float3 shadowCoordinate : TEXCOORD0;
            float shadowBias : TEXCOORD1;
            float2 lighting : TEXCOORD2;
            float inverseCameraDepth : TEXCOORD3;
            float3 effectData : TEXCOORD4;
        };

        VertexOutput VSMain(VertexInput input)
        {
            VertexOutput output;
            output.position = float4(input.position, 1.0);
            output.color = input.color;
            output.shadowCoordinate = input.shadowCoordinate;
            output.shadowBias = input.shadowBias;
            output.lighting = input.lighting;
            output.inverseCameraDepth = input.inverseCameraDepth;
            output.effectData = input.effectData;
            return output;
        }

        float4 PSMain(VertexOutput input) : SV_TARGET
        {
            return input.color;
        }

        float4 PSShadowMain(VertexOutput input) : SV_TARGET
        {
            return input.position.zzzz;
        }

        struct UiVertexInput
        {
            float3 position : POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float glyph : TEXCOORD1;
        };

        struct UiVertexOutput
        {
            float4 position : SV_POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float glyph : TEXCOORD1;
        };

        UiVertexOutput VSUiMain(UiVertexInput input)
        {
            UiVertexOutput output;
            output.position = float4(input.position, 1);
            output.color = input.color;
            output.uv = input.uv;
            output.glyph = input.glyph;
            return output;
        }

        Texture2D<float> FontTexture : register(t0);
        SamplerState FontSampler : register(s0);

        float4 PSUiMain(UiVertexOutput input) : SV_TARGET
        {
            float coverage = input.glyph > 0.5
                ? FontTexture.Sample(FontSampler, input.uv)
                : 1;
            float alpha = input.color.a * coverage;
            return float4(input.color.rgb * alpha, alpha);
        }

        Texture2D<float> ShadowTexture : register(t1);
        SamplerComparisonState ShadowSampler : register(s0);

        float4 PSLitMain(VertexOutput input) : SV_TARGET
        {
            float visibility = 1;
            float3 shadowCoordinate = input.shadowCoordinate / input.inverseCameraDepth;
            if (input.lighting.y > 0 && input.shadowBias >= 0 &&
                all(shadowCoordinate.xy >= 0) &&
                all(shadowCoordinate.xy <= 1))
            {
                float bias = input.shadowBias *
                    (1 + 4 * (1 - saturate(input.lighting.y)));
                uint shadowWidth;
                uint shadowHeight;
                ShadowTexture.GetDimensions(shadowWidth, shadowHeight);
                float2 shadowTexel = shadowCoordinate.xy *
                    (float2(shadowWidth, shadowHeight) - 1);
                float2 shadowUv = (shadowTexel + 0.5) /
                    float2(shadowWidth, shadowHeight);
                float2 texelOffset = 1.0 / float2(shadowWidth, shadowHeight);
                visibility = (
                    ShadowTexture.SampleCmpLevelZero(
                        ShadowSampler, shadowUv + texelOffset * float2(-0.5, -0.5), shadowCoordinate.z + bias) +
                    ShadowTexture.SampleCmpLevelZero(
                        ShadowSampler, shadowUv + texelOffset * float2(0.5, -0.5), shadowCoordinate.z + bias) +
                    ShadowTexture.SampleCmpLevelZero(
                        ShadowSampler, shadowUv + texelOffset * float2(-0.5, 0.5), shadowCoordinate.z + bias) +
                    ShadowTexture.SampleCmpLevelZero(
                        ShadowSampler, shadowUv + texelOffset * float2(0.5, 0.5), shadowCoordinate.z + bias)) * 0.25;
            }

            float3 color = input.color.rgb *
                (input.lighting.x + input.lighting.y * visibility);
            if (input.effectData.x > 0)
            {
                float depth = rcp(max(input.inverseCameraDepth, 0.0001));
                float fade = saturate(input.effectData.x) *
                    saturate((input.effectData.y + input.effectData.z - depth) /
                        max(input.effectData.z, 0.0001));
                float dither[16] = {
                    0, 8, 2, 10, 12, 4, 14, 6,
                    3, 11, 1, 9, 15, 7, 13, 5
                };
                int2 patternPosition = int2(input.position.xy) & 3;
                float threshold = dither[patternPosition.y * 4 + patternPosition.x] / 16;
                clip(threshold - fade);
            }
            return float4(color, input.color.a);
        }

        Texture2D<float4> SceneTexture : register(t0);

        float4 PSPostProcess(VertexOutput input) : SV_TARGET
        {
            int2 pixel = int2(input.position.xy);
            uint textureWidth;
            uint textureHeight;
            SceneTexture.GetDimensions(textureWidth, textureHeight);
            float4 color = SceneTexture.Load(int3(pixel, 0));

            if (input.color.a > 0.5)
            {
                float3 bloom = 0;
                const float kernel[5] = { 1, 2, 3, 2, 1 };
                float threshold = max(input.color.b,
                    dot(input.effectData, float3(0.2126, 0.7152, 0.0722)) + 0.04);
                for (int y = -2; y <= 2; y++)
                {
                    for (int x = -2; x <= 2; x++)
                    {
                        int2 samplePosition = clamp(pixel + int2(x, y) * 4,
                            int2(0, 0), int2(textureWidth - 1, textureHeight - 1));
                        float3 sampleColor = SceneTexture.Load(int3(samplePosition, 0)).rgb;
                        float brightness = max(sampleColor.r, max(sampleColor.g, sampleColor.b));
                        bool isBackground = distance(sampleColor, input.effectData) < 0.04;
                        if (!isBackground && brightness > threshold)
                        {
                            bloom += sampleColor * ((brightness - threshold) / max(brightness, 0.001))
                                * kernel[x + 2] * kernel[y + 2];
                        }
                    }
                }

                color.rgb += bloom * (input.color.g * 3 / 81);
            }

            if (input.lighting.y > 0.5 && input.shadowBias > 0)
            {
                float2 sunPosition = input.shadowCoordinate.xy * float2(textureWidth, textureHeight);
                float sunRadius = input.shadowCoordinate.z * textureHeight;
                float2 pixelPosition = float2(pixel) + 0.5;
                float2 screenCenter = float2(textureWidth, textureHeight) * 0.5;
                float2 sunDelta = sunPosition - screenCenter;
                float sunDistance = length(sunPosition - pixelPosition);
                float flareStrength = input.shadowBias;
                float spread = max(input.lighting.x, 0.05);
                float glow = exp(-sunDistance / max(sunRadius * (1 + spread * 8), 1));
                float halo = exp(-abs(sunDistance - sunRadius * (2 + spread * 8)) /
                    max(sunRadius * spread * 2, 1));
                float2 flareAxis = normalize(sunDelta + float2(0.0001, 0));
                float2 ghostA = screenCenter - sunDelta * 0.45;
                float2 ghostB = screenCenter - sunDelta * 0.15;
                float ghost = exp(-dot(pixelPosition - ghostA, pixelPosition - ghostA) /
                        max(sunRadius * sunRadius * 36, 1)) * 0.5
                    + exp(-dot(pixelPosition - ghostB, pixelPosition - ghostB) /
                        max(sunRadius * sunRadius * 16, 1)) * 0.35;
                float streakDistance = abs(dot(pixelPosition - sunPosition, float2(-flareAxis.y, flareAxis.x)));
                float streak = exp(-streakDistance / max(sunRadius * spread * 2, 1))
                    * exp(-sunDistance / max(sunRadius * (5 + spread * 10), 1)) * 0.18;
                color.rgb += float3(1, 0.77, 0.43) *
                    ((glow * 0.18 + halo * 0.16 + ghost + streak) * flareStrength);
            }

            return color;
        }
        """;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11PixelShader _shadowPixelShader;
    private readonly ID3D11VertexShader _uiVertexShader;
    private readonly ID3D11PixelShader _uiPixelShader;
    private readonly ID3D11PixelShader _litPixelShader;
    private readonly ID3D11PixelShader _postProcessPixelShader;
    private readonly ID3D11InputLayout _inputLayout;
    private ID3D11Buffer _vertexBuffer;
    private readonly ID3D11RasterizerState _rasterizerState;
    private readonly ID3D11DepthStencilState _depthState;
    private readonly ID3D11DepthStencilState _noDepthState;
    private readonly ID3D11DepthStencilState _shadowDepthState;
    private readonly ID3D11BlendState _blendState;
    private readonly ID3D11SamplerState _shadowSampler;
    private readonly ID3D11InputLayout _uiInputLayout;
    private readonly ID3D11SamplerState _uiSampler;
    private readonly List<float> _vertices = [];
    private readonly List<DebugLine> _gizmoLines = [];
    private readonly Dictionary<(Vector3 First, Vector3 Second), OutlineEdge> _outlineEdges = [];
    private float[] _uploadBuffer = [];
    private readonly long _memoryAllocationLimit;
    private readonly int _sampleCount;
    private ID3D11RenderTargetView? _renderTarget;
    private ID3D11RenderTargetView? _backBufferView;
    private ID3D11Texture2D? _multisampleTexture;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11Texture2D? _postProcessTexture;
    private ID3D11ShaderResourceView? _postProcessResource;
    private ID3D11Texture2D? _shadowTexture;
    private ID3D11ShaderResourceView? _shadowResource;
    private ID3D11RenderTargetView? _shadowRenderTarget;
    private ID3D11Texture2D? _shadowDepthTexture;
    private ID3D11DepthStencilView? _shadowDepthView;
    private ID3D11Texture2D? _fontTexture;
    private ID3D11ShaderResourceView? _fontResource;
    private int _fontAtlasResolution;
    private float[] _uiUploadBuffer = [];
    private ID3D11Texture2D? _depthTexture;
    private ID3D11DepthStencilView? _depthView;
    private int _bufferCapacity;
    private int _renderWidth;
    private int _renderHeight;
    private int _clientWidth;
    private int _clientHeight;
    private GameScene? _pickScene;
    private Camera? _pickCamera;
    private int _pickWidth;
    private int _pickHeight;
    private long _renderedShadowMapVersion = long.MinValue;

    public Direct3D11Renderer(
        IntPtr windowHandle,
        int renderWidth,
        int renderHeight,
        int clientWidth,
        int clientHeight,
        long memoryAllocationLimit,
        float antialiasingStrength = 1f)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        ValidateDimensions(renderWidth, renderHeight);
        ValidateDimensions(clientWidth, clientHeight);
        if (memoryAllocationLimit < 4096 ||
            memoryAllocationLimit > int.MaxValue ||
            memoryAllocationLimit % sizeof(float) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryAllocationLimit));
        }
        if (antialiasingStrength is < 0f or > 1f || !float.IsFinite(antialiasingStrength))
        {
            throw new ArgumentOutOfRangeException(nameof(antialiasingStrength));
        }

        _memoryAllocationLimit = memoryAllocationLimit;
        _sampleCount = antialiasingStrength <= 0 ? 1 : antialiasingStrength < 0.5f ? 2 : 4;
        _renderWidth = renderWidth;
        _renderHeight = renderHeight;
        _clientWidth = clientWidth;
        _clientHeight = clientHeight;

        var featureLevels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_0 };
        var createDeviceResult = D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out _device,
            out _,
            out _context);
        createDeviceResult.CheckError();

        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false)
            ?? throw new InvalidOperationException("Could not create the Direct3D graphics factory.");
        var swapChainDescription = new SwapChainDescription1
        {
            Width = (uint)clientWidth,
            Height = (uint)clientHeight,
            Format = Format.R8G8B8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore
        };
        _swapChain = factory.CreateSwapChainForHwnd(_device, windowHandle, swapChainDescription);

        var vertexShaderBytecode = CompileShader(ShaderSource, "VSMain", "vs_4_0");
        var pixelShaderBytecode = CompileShader(ShaderSource, "PSMain", "ps_4_0");
        var shadowPixelShaderBytecode = CompileShader(ShaderSource, "PSShadowMain", "ps_4_0");
        var uiVertexShaderBytecode = CompileShader(ShaderSource, "VSUiMain", "vs_4_0");
        var uiPixelShaderBytecode = CompileShader(ShaderSource, "PSUiMain", "ps_4_0");
        var litPixelShaderBytecode = CompileShader(ShaderSource, "PSLitMain", "ps_4_0");
        var postProcessPixelShaderBytecode = CompileShader(ShaderSource, "PSPostProcess", "ps_4_0");
        _vertexShader = _device.CreateVertexShader(vertexShaderBytecode.Span);
        _pixelShader = _device.CreatePixelShader(pixelShaderBytecode.Span);
        _shadowPixelShader = _device.CreatePixelShader(shadowPixelShaderBytecode.Span);
        _uiVertexShader = _device.CreateVertexShader(uiVertexShaderBytecode.Span);
        _uiPixelShader = _device.CreatePixelShader(uiPixelShaderBytecode.Span);
        _litPixelShader = _device.CreatePixelShader(litPixelShaderBytecode.Span);
        _postProcessPixelShader = _device.CreatePixelShader(postProcessPixelShaderBytecode.Span);
        _inputLayout = _device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, sizeof(float) * 3, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32B32_Float, sizeof(float) * 7, 0),
            new InputElementDescription("TEXCOORD", 1, Format.R32_Float, sizeof(float) * 10, 0),
            new InputElementDescription("TEXCOORD", 2, Format.R32G32_Float, sizeof(float) * 11, 0),
            new InputElementDescription("TEXCOORD", 3, Format.R32_Float, sizeof(float) * 13, 0),
            new InputElementDescription("TEXCOORD", 4, Format.R32G32B32_Float, sizeof(float) * 14, 0)
        ],
        vertexShaderBytecode.Span);
        _uiInputLayout = _device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, sizeof(float) * 3, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, sizeof(float) * 7, 0),
            new InputElementDescription("TEXCOORD", 1, Format.R32_Float, sizeof(float) * 9, 0)
        ],
        uiVertexShaderBytecode.Span);

        _vertexBuffer = _device.CreateBuffer(
            4096,
            BindFlags.VertexBuffer,
            ResourceUsage.Dynamic,
            CpuAccessFlags.Write,
            ResourceOptionFlags.None,
            0);
        _bufferCapacity = 4096;
        _rasterizerState = _device.CreateRasterizerState(new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            DepthClipEnable = true
        });
        _depthState = _device.CreateDepthStencilState(DepthStencilDescription.Default);
        _noDepthState = _device.CreateDepthStencilState(DepthStencilDescription.None);
        _shadowDepthState = _device.CreateDepthStencilState(new DepthStencilDescription(
            true,
            DepthWriteMask.All,
            ComparisonFunction.Greater));
        _blendState = _device.CreateBlendState(BlendDescription.AlphaBlend);
        _shadowSampler = _device.CreateSamplerState(new SamplerDescription(
            Filter.ComparisonMinMagLinearMipPoint,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            0,
            1,
            ComparisonFunction.GreaterEqual,
            new Color4(0, 0, 0, 0),
            0,
            0));
        _uiSampler = _device.CreateSamplerState(new SamplerDescription(
            Filter.MinMagLinearMipPoint,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            TextureAddressMode.Clamp,
            0,
            1,
            ComparisonFunction.Never,
            new Color4(0, 0, 0, 0),
            0,
            0));
        CreateRenderTargets();
    }

    public int LastDrawCallCount { get; private set; }

    public void SetSize(int renderWidth, int renderHeight, int clientWidth, int clientHeight)
    {
        ValidateDimensions(renderWidth, renderHeight);
        ValidateDimensions(clientWidth, clientHeight);
        _renderWidth = renderWidth;
        _renderHeight = renderHeight;

        if (_clientWidth == clientWidth && _clientHeight == clientHeight)
        {
            return;
        }

        _context.ClearState();
        ReleaseRenderTargets();
        ReleaseShadowTargets();
        _swapChain.ResizeBuffers(0, (uint)clientWidth, (uint)clientHeight, Format.Unknown, SwapChainFlags.None);
        _clientWidth = clientWidth;
        _clientHeight = clientHeight;
        CreateRenderTargets();
    }

    public void SetPickContext(GameScene scene, Camera camera, int width, int height)
    {
        _pickScene = scene ?? throw new ArgumentNullException(nameof(scene));
        _pickCamera = camera ?? throw new ArgumentNullException(nameof(camera));
        _pickWidth = width;
        _pickHeight = height;
    }

    public void Render(
        GameScene scene,
        Camera camera,
        Construct? selected,
        Construct? hovered,
        DebugDraw debugDraw,
        UiDrawList ui,
        float ditherFade = 0,
        TransformGizmoMode gizmoMode = TransformGizmoMode.Position)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(ui);
        ValidateCamera(camera);
        SetPickContext(scene, camera, _renderWidth, _renderHeight);

        _vertices.Clear();
        LastDrawCallCount = 0;
        var entities = scene.Entities;
        var direction = camera.Target - camera.Position;
        var forward = direction.Normalized();
        var right = Vector3.Cross(Vector3.Up, forward).Normalized();
        var up = Vector3.Cross(forward, right).Normalized();
        var focalLength = _renderHeight / (2 * MathF.Tan(camera.FieldOfView * MathF.PI / 360));
        var selectedEntity = selected as Entity;
        var hoveredEntity = hovered as Entity;
        Span<ScreenVertex> projected = stackalloc ScreenVertex[6];
        scene.Lighting.PrepareShadowProjection(entities);
        RenderShadowMap(entities, scene.Lighting);
        _vertices.Clear();
        var sunCenter = default(ScreenVertex);
        var celestialPositionVisible = TryGetCelestialScreenPosition(
            scene.Lighting, camera, right, up, forward, focalLength, out sunCenter);
        var sunVisible = celestialPositionVisible && scene.Lighting.CelestialBodiesShown;
        var sunRadius = celestialPositionVisible ? MathF.Max(8, _renderHeight * 0.015f) : 0;
        if (sunVisible)
        {
            AddSunDisc(sunCenter, sunRadius);
        }

        foreach (var entity in entities)
        {
            if (!entity.IsVisibleInTree)
            {
                continue;
            }

            var vertices = entity.Mesh.Vertices;
            var indices = entity.Mesh.Indices;
            for (var index = 0; index < indices.Length; index += 3)
            {
                var worldA = entity.TransformPoint(vertices[indices[index]]);
                var worldB = entity.TransformPoint(vertices[indices[index + 1]]);
                var worldC = entity.TransformPoint(vertices[indices[index + 2]]);
                var normal = Vector3.Cross(worldB - worldA, worldC - worldA).Normalized();
                var diffuse = scene.Lighting.CalculateDiffuse(normal);
                var projectedCount = CameraProjection.ClipAndProjectTriangle(
                    worldA, worldB, worldC, camera.Position, right, up, forward, focalLength,
                    camera.NearPlane, _renderWidth, _renderHeight, scene.Lighting, projected);
                for (var vertex = 0; vertex < projectedCount; vertex += 3)
                {
                    AddLitTriangle(
                        projected[vertex],
                        projected[vertex + 1],
                        projected[vertex + 2],
                        entity.Color,
                        scene.Lighting.AmbientIntensity,
                        diffuse,
                        scene.Lighting.ShadowMapReady ? scene.Lighting.ShadowBias : -1,
                        ditherFade,
                        camera.NearPlane);
                    LastDrawCallCount++;
                }
            }
        }

        if (selectedEntity is not null)
        {
            AddSelectionOutline(ui, selectedEntity, camera, right, up, forward, focalLength,
                new Color32(255, 202, 92));
        }
        if (hoveredEntity is not null && !ReferenceEquals(hoveredEntity, selectedEntity))
        {
            AddSelectionOutline(ui, hoveredEntity, camera, right, up, forward, focalLength,
                new Color32(92, 190, 245));
        }
        AddDebugDraw(ui, debugDraw, camera, right, up, forward, focalLength);
        if (selected is ITransformableConstruct)
        {
            AddTransformGizmo(ui, selected, gizmoMode, camera, right, up, forward, focalLength);
        }
        var sceneVertexCount = GetVertexCount(_vertices.Count);
        _context.OMSetRenderTargets(_renderTarget!, _depthView);
        var sky = scene.Lighting.SkyboxTint;
        _context.ClearRenderTargetView(_renderTarget!, new Color4(
            sky.R / 255f, sky.G / 255f, sky.B / 255f, sky.A / 255f));
        _context.ClearDepthStencilView(_depthView!, DepthStencilClearFlags.Depth, 1, 0);
        _context.RSSetViewports([new Viewport(0, 0, _clientWidth, _clientHeight)]);
        _context.RSSetState(_rasterizerState);
        _context.OMSetBlendState(_blendState);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_litPixelShader);
        _context.PSSetShaderResource(1, _shadowResource!);
        _context.PSSetSampler(0, _shadowSampler);

        if (sceneVertexCount > 0)
        {
            UploadVertices();
            _context.IASetVertexBuffer(0, _vertexBuffer, VertexStride, 0);
            _context.OMSetDepthStencilState(_depthState, 0);
            _context.Draw((uint)sceneVertexCount, 0);
        }

        _context.PSSetShaderResource(1, null!);
        ResolveSceneColor();
        var bloomEnabled = scene.Lighting.RenderBloom && scene.Lighting.BloomStrength > 0;
        var lensFlareEnabled = celestialPositionVisible && scene.Lighting.RenderLensFlare &&
            scene.Lighting.LensFlareStrength > 0;
        if (bloomEnabled || lensFlareEnabled)
        {
            DrawPostProcess(
                scene.Lighting,
                sunCenter,
                sunRadius,
                lensFlareEnabled);
        }
        else
        {
            _context.CopyResource(_backBuffer!, _postProcessTexture!);
        }
        DrawUi(ui);
        _swapChain.Present(0, PresentFlags.None);
    }

    public bool TryPick(int x, int y, out Entity? entity)
    {
        var scene = _pickScene;
        var camera = _pickCamera;
        if (scene is null || camera is null ||
            (uint)x >= (uint)_pickWidth || (uint)y >= (uint)_pickHeight ||
            camera.NearPlane <= 0 || camera.FieldOfView <= 0 || camera.FieldOfView >= 180)
        {
            entity = null;
            return false;
        }

        var direction = camera.Target - camera.Position;
        if (direction.Length < 0.0001f)
        {
            entity = null;
            return false;
        }

        var forward = direction.Normalized();
        var right = Vector3.Cross(Vector3.Up, forward).Normalized();
        var up = Vector3.Cross(forward, right).Normalized();
        var focalLength = _pickHeight / (2 * MathF.Tan(camera.FieldOfView * MathF.PI / 360));
        var rayDirection = (
            forward +
            right * ((x + 0.5f - _pickWidth / 2f) / focalLength) +
            up * ((_pickHeight / 2f - y - 0.5f) / focalLength)).Normalized();
        var nearestDistance = float.PositiveInfinity;
        Entity? nearestEntity = null;

        foreach (var candidate in scene.Entities)
        {
            if (!candidate.IsVisibleInTree)
            {
                continue;
            }

            var vertices = candidate.Mesh.Vertices;
            var indices = candidate.Mesh.Indices;
            for (var index = 0; index < indices.Length; index += 3)
            {
                var a = candidate.TransformPoint(vertices[indices[index]]);
                var b = candidate.TransformPoint(vertices[indices[index + 1]]);
                var c = candidate.TransformPoint(vertices[indices[index + 2]]);
                if (TryIntersectRay(camera.Position, rayDirection, a, b, c, out var distance) &&
                    distance < nearestDistance &&
                    Vector3.Dot(camera.Position + rayDirection * distance - camera.Position, forward) >= camera.NearPlane)
                {
                    nearestDistance = distance;
                    nearestEntity = candidate;
                }
            }
        }

        entity = nearestEntity;
        return entity is not null;
    }

    public void Dispose()
    {
        _context.ClearState();
        ReleaseRenderTargets();
        ReleaseShadowTargets();
        _fontResource?.Dispose();
        _fontTexture?.Dispose();
        _shadowSampler.Dispose();
        _uiSampler.Dispose();
        _uiInputLayout.Dispose();
        _blendState.Dispose();
        _shadowDepthState.Dispose();
        _noDepthState.Dispose();
        _depthState.Dispose();
        _rasterizerState.Dispose();
        _vertexBuffer.Dispose();
        _inputLayout.Dispose();
        _postProcessPixelShader.Dispose();
        _shadowPixelShader.Dispose();
        _uiPixelShader.Dispose();
        _uiVertexShader.Dispose();
        _litPixelShader.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
        _swapChain.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Render dimensions must be positive.");
        }
    }

    private static void ValidateCamera(Camera camera)
    {
        if (camera.NearPlane <= 0 || !float.IsFinite(camera.NearPlane))
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "The camera near plane must be positive.");
        }

        if (camera.FieldOfView <= 0 || camera.FieldOfView >= 180 || !float.IsFinite(camera.FieldOfView))
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "The camera field of view must be between 0 and 180 degrees.");
        }

        if ((camera.Target - camera.Position).Length < 0.0001f)
        {
            throw new ArgumentException("The camera position and target must differ.", nameof(camera));
        }
    }

    private bool TryProject(
        Vector3 point,
        Camera camera,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float focalLength,
        out ScreenVertex vertex)
    {
        var relative = point - camera.Position;
        var viewDepth = Vector3.Dot(relative, forward);
        if (!float.IsFinite(viewDepth) || viewDepth < camera.NearPlane)
        {
            vertex = default;
            return false;
        }

        var x = _screenCenterX + Vector3.Dot(relative, right) * focalLength / viewDepth;
        var y = _screenCenterY - Vector3.Dot(relative, up) * focalLength / viewDepth;
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            vertex = default;
            return false;
        }

        vertex = new ScreenVertex(x, y, viewDepth);
        return true;
    }

    private float _screenCenterX => _renderWidth / 2f;
    private float _screenCenterY => _renderHeight / 2f;

    private void AddTriangle(ScreenVertex a, ScreenVertex b, ScreenVertex c, Color32 color)
    {
        AddVertex(a, color);
        AddVertex(b, color);
        AddVertex(c, color);
    }

    private void AddLitTriangle(
        ScreenVertex a,
        ScreenVertex b,
        ScreenVertex c,
        Color32 color,
        float ambient,
        float diffuse,
        float shadowBias,
        float ditherFade,
        float nearPlane)
    {
        AddLitVertex(a, color, ambient, diffuse, shadowBias, ditherFade, nearPlane);
        AddLitVertex(b, color, ambient, diffuse, shadowBias, ditherFade, nearPlane);
        AddLitVertex(c, color, ambient, diffuse, shadowBias, ditherFade, nearPlane);
    }

    internal static int GetVertexCount(int floatCount) =>
        floatCount / VertexFloatCount;

    private void AddLitVertex(
        ScreenVertex vertex,
        Color32 color,
        float ambient,
        float diffuse,
        float shadowBias,
        float ditherFade,
        float nearPlane)
    {
        _vertices.Add(vertex.X / _renderWidth * 2 - 1);
        _vertices.Add(1 - vertex.Y / _renderHeight * 2);
        _vertices.Add(1 - _pickCamera!.NearPlane / vertex.Z);
        AddColor(color);
        var inverseDepth = 1f / vertex.Z;
        _vertices.Add(vertex.ShadowX * inverseDepth);
        _vertices.Add(vertex.ShadowY * inverseDepth);
        _vertices.Add(vertex.ShadowDepth * inverseDepth);
        _vertices.Add(shadowBias);
        _vertices.Add(ambient);
        _vertices.Add(diffuse);
        _vertices.Add(inverseDepth);
        _vertices.Add(ditherFade);
        _vertices.Add(nearPlane);
        _vertices.Add(1);
    }

    private void AddVertex(ScreenVertex vertex, Color32 color)
    {
        _vertices.Add(vertex.X / _renderWidth * 2 - 1);
        _vertices.Add(1 - vertex.Y / _renderHeight * 2);
        _vertices.Add(1 - _pickCamera!.NearPlane / vertex.Z);
        AddColor(color);
        _vertices.Add(-1);
        _vertices.Add(-1);
        _vertices.Add(0);
        _vertices.Add(0);
        _vertices.Add(1);
        _vertices.Add(0);
        _vertices.Add(1);
        _vertices.Add(0);
        _vertices.Add(0);
        _vertices.Add(0);
    }

    private void AddColor(Color32 color)
    {
        _vertices.Add(color.R / 255f);
        _vertices.Add(color.G / 255f);
        _vertices.Add(color.B / 255f);
        _vertices.Add(color.A / 255f);
    }

    private void AddSelectionOutline(
        UiDrawList ui,
        Entity entity,
        Camera camera,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float focalLength,
        Color32 color)
    {
        var edges = _outlineEdges;
        edges.Clear();
        var vertices = entity.Mesh.Vertices;
        var indices = entity.Mesh.Indices;
        for (var index = 0; index < indices.Length; index += 3)
        {
            var a = entity.TransformPoint(vertices[indices[index]]);
            var b = entity.TransformPoint(vertices[indices[index + 1]]);
            var c = entity.TransformPoint(vertices[indices[index + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            var isFrontFace = Vector3.Dot(normal, camera.Position - (a + b + c) / 3) > 0;
            AddOutlineEdge(edges, a, b, isFrontFace);
            AddOutlineEdge(edges, b, c, isFrontFace);
            AddOutlineEdge(edges, c, a, isFrontFace);
        }

        foreach (var edge in edges.Values)
        {
            if (edge.FrontFaces == 0 ||
                (edge.FrontFaces == edge.TotalFaces && edge.TotalFaces > 1) ||
                !TryProject(edge.Start, camera, right, up, forward, focalLength, out var start) ||
                !TryProject(edge.End, camera, right, up, forward, focalLength, out var end) ||
                !ClipLine(ref start, ref end, _renderWidth, _renderHeight))
            {
                continue;
            }

            ui.DrawLine(
                (int)MathF.Round(start.X),
                (int)MathF.Round(start.Y),
                (int)MathF.Round(end.X),
                (int)MathF.Round(end.Y),
                color);
        }
    }

    private static void AddOutlineEdge(
        Dictionary<(Vector3 First, Vector3 Second), OutlineEdge> edges,
        Vector3 start,
        Vector3 end,
        bool isFrontFace)
    {
        var key = ComparePositions(start, end) <= 0 ? (start, end) : (end, start);
        if (edges.TryGetValue(key, out var edge))
        {
            edges[key] = edge with
            {
                TotalFaces = edge.TotalFaces + 1,
                FrontFaces = edge.FrontFaces + (isFrontFace ? 1 : 0)
            };
        }
        else
        {
            edges.Add(key, new OutlineEdge(
                key.Item1,
                key.Item2,
                1,
                isFrontFace ? 1 : 0));
        }
    }

    private static int ComparePositions(Vector3 first, Vector3 second)
    {
        var comparison = first.X.CompareTo(second.X);
        if (comparison == 0)
        {
            comparison = first.Y.CompareTo(second.Y);
        }
        return comparison == 0 ? first.Z.CompareTo(second.Z) : comparison;
    }

    private void AddDebugDraw(
        UiDrawList ui,
        DebugDraw debugDraw,
        Camera camera,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float focalLength)
    {
        foreach (var line in debugDraw.Lines)
        {
            if (!TryProject(line.Start, camera, right, up, forward, focalLength, out var start) ||
                !TryProject(line.End, camera, right, up, forward, focalLength, out var end) ||
                !ClipLine(ref start, ref end, _renderWidth, _renderHeight))
            {
                continue;
            }

            ui.DrawLine((int)MathF.Round(start.X), (int)MathF.Round(start.Y),
                (int)MathF.Round(end.X), (int)MathF.Round(end.Y), line.Color);
        }

        foreach (var text in debugDraw.Texts)
        {
            if (TryProject(text.Position, camera, right, up, forward, focalLength, out var position) &&
                position.X >= 0 && position.X < _renderWidth &&
                position.Y >= 0 && position.Y < _renderHeight)
            {
                ui.DrawText((int)position.X, (int)position.Y, text.Text, text.Color);
            }
        }
    }

    private void AddTransformGizmo(
        UiDrawList ui,
        Construct construct,
        TransformGizmoMode mode,
        Camera camera,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float focalLength)
    {
        _gizmoLines.Clear();
        var origin = TransformGizmoGeometry.AddLines(construct, mode, camera.Position, _gizmoLines);
        foreach (var line in _gizmoLines)
        {
            if (TryProject(line.Start, camera, right, up, forward, focalLength, out var start) &&
                TryProject(line.End, camera, right, up, forward, focalLength, out var end) &&
                ClipLine(ref start, ref end, _renderWidth, _renderHeight))
            {
                ui.DrawLine((int)MathF.Round(start.X), (int)MathF.Round(start.Y),
                    (int)MathF.Round(end.X), (int)MathF.Round(end.Y), line.Color);
            }
        }

        if (TryProject(origin, camera, right, up, forward, focalLength, out var pivot))
        {
            var x = (int)MathF.Round(pivot.X);
            var y = (int)MathF.Round(pivot.Y);
            var marker = new Color32(255, 232, 160);
            ui.DrawLine(x - 5, y, x + 5, y, marker);
            ui.DrawLine(x, y - 5, x, y + 5, marker);
        }
    }

    private static bool ClipLine(ref ScreenVertex start, ref ScreenVertex end, int width, int height)
    {
        var x0 = (double)start.X;
        var y0 = (double)start.Y;
        var deltaX = (double)end.X - x0;
        var deltaY = (double)end.Y - y0;
        var startRatio = 0d;
        var endRatio = 1d;
        if (!Clip(-deltaX, x0, ref startRatio, ref endRatio) ||
            !Clip(deltaX, width - 1 - x0, ref startRatio, ref endRatio) ||
            !Clip(-deltaY, y0, ref startRatio, ref endRatio) ||
            !Clip(deltaY, height - 1 - y0, ref startRatio, ref endRatio))
        {
            return false;
        }

        var startDepth = start.Z;
        var endDepth = end.Z;
        start = new ScreenVertex(
            (float)(x0 + startRatio * deltaX),
            (float)(y0 + startRatio * deltaY),
            startDepth + (endDepth - startDepth) * (float)startRatio);
        end = new ScreenVertex(
            (float)(x0 + endRatio * deltaX),
            (float)(y0 + endRatio * deltaY),
            startDepth + (endDepth - startDepth) * (float)endRatio);
        return true;
    }

    private static bool Clip(double p, double q, ref double startRatio, ref double endRatio)
    {
        if (p == 0)
        {
            return q >= 0;
        }

        var ratio = q / p;
        if (p < 0)
        {
            if (ratio > endRatio)
            {
                return false;
            }
            startRatio = System.Math.Max(startRatio, ratio);
        }
        else
        {
            if (ratio < startRatio)
            {
                return false;
            }
            endRatio = System.Math.Min(endRatio, ratio);
        }

        return true;
    }

    private static bool TryIntersectRay(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance)
    {
        var edge1 = b - a;
        var edge2 = c - a;
        var cross = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, cross);
        if (MathF.Abs(determinant) < 0.000001f)
        {
            distance = 0;
            return false;
        }

        var inverseDeterminant = 1 / determinant;
        var fromA = origin - a;
        var u = Vector3.Dot(fromA, cross) * inverseDeterminant;
        if (u < 0 || u > 1)
        {
            distance = 0;
            return false;
        }

        var secondCross = Vector3.Cross(fromA, edge1);
        var v = Vector3.Dot(direction, secondCross) * inverseDeterminant;
        if (v < 0 || u + v > 1)
        {
            distance = 0;
            return false;
        }

        distance = Vector3.Dot(edge2, secondCross) * inverseDeterminant;
        return distance >= 0;
    }

    private void UploadVertices()
    {
        var requiredSize = checked((uint)(_vertices.Count * sizeof(float)));
        EnsureVertexBuffer(requiredSize);

        var mapped = _context.Map(_vertexBuffer, MapMode.WriteDiscard);
        try
        {
            if (_uploadBuffer.Length < _vertices.Count)
            {
                var maximumCapacity = checked((int)(_memoryAllocationLimit / sizeof(float)));
                var capacity = System.Math.Min(
                    maximumCapacity,
                    System.Math.Max(_vertices.Count, _uploadBuffer.Length * 2));
                Array.Resize(ref _uploadBuffer, capacity);
            }

            _vertices.CopyTo(_uploadBuffer, 0);
            Marshal.Copy(_uploadBuffer, 0, mapped.DataPointer, _vertices.Count);
        }
        finally
        {
            _context.Unmap(_vertexBuffer, 0);
        }
    }

    private void UploadUiVertices(IReadOnlyList<UiVertex> vertices, int width, int height)
    {
        var floatCount = checked(vertices.Count * 10);
        var requiredSize = checked((uint)(floatCount * sizeof(float)));
        EnsureVertexBuffer(requiredSize);
        if (_uiUploadBuffer.Length < floatCount)
        {
            var maximumCapacity = checked((int)(_memoryAllocationLimit / sizeof(float)));
            var capacity = System.Math.Min(maximumCapacity, System.Math.Max(floatCount, _uiUploadBuffer.Length * 2));
            Array.Resize(ref _uiUploadBuffer, capacity);
        }

        var output = 0;
        foreach (var vertex in vertices)
        {
            _uiUploadBuffer[output++] = vertex.X * (2f / width) - 1f;
            _uiUploadBuffer[output++] = 1f - vertex.Y * (2f / height);
            _uiUploadBuffer[output++] = vertex.Z;
            _uiUploadBuffer[output++] = vertex.R;
            _uiUploadBuffer[output++] = vertex.G;
            _uiUploadBuffer[output++] = vertex.B;
            _uiUploadBuffer[output++] = vertex.A;
            _uiUploadBuffer[output++] = vertex.U;
            _uiUploadBuffer[output++] = vertex.V;
            _uiUploadBuffer[output++] = vertex.Glyph;
        }

        var mapped = _context.Map(_vertexBuffer, MapMode.WriteDiscard);
        try
        {
            Marshal.Copy(_uiUploadBuffer, 0, mapped.DataPointer, floatCount);
        }
        finally
        {
            _context.Unmap(_vertexBuffer, 0);
        }
    }

    private void EnsureVertexBuffer(uint requiredSize)
    {
        if (requiredSize > _memoryAllocationLimit)
        {
            throw new InvalidOperationException(
                $"The frame requires {requiredSize} bytes for renderer vertices, exceeding the configured " +
                $"{_memoryAllocationLimit}-byte memory allocation limit.");
        }

        if (requiredSize > _bufferCapacity)
        {
            _vertexBuffer.Dispose();
            _bufferCapacity = (int)System.Math.Min(
                _memoryAllocationLimit,
                System.Math.Max(requiredSize, _bufferCapacity * 2L));
            _vertexBuffer = _device.CreateBuffer(
                (uint)_bufferCapacity,
                BindFlags.VertexBuffer,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                0);
        }
    }

    private void RenderShadowMap(IReadOnlyList<Entity> entities, Lighting lighting)
    {
        if (!lighting.RenderShadows || !lighting.ShadowMapReady ||
            _renderedShadowMapVersion == lighting.ShadowMapVersion)
        {
            return;
        }

        EnsureShadowTargets(lighting.ShadowResolution);
        _vertices.Clear();
        foreach (var entity in entities)
        {
            if (!entity.IsVisibleInTree)
            {
                continue;
            }

            var vertices = entity.Mesh.Vertices;
            var indices = entity.Mesh.Indices;
            for (var index = 0; index < indices.Length; index += 3)
            {
                AddShadowVertex(lighting.GetShadowCoordinate(entity.TransformPoint(vertices[indices[index]])));
                AddShadowVertex(lighting.GetShadowCoordinate(entity.TransformPoint(vertices[indices[index + 1]])));
                AddShadowVertex(lighting.GetShadowCoordinate(entity.TransformPoint(vertices[indices[index + 2]])));
            }
        }

        _context.OMSetRenderTargets(_shadowRenderTarget!, _shadowDepthView);
        _context.ClearRenderTargetView(_shadowRenderTarget!, new Color4(0, 0, 0, 0));
        _context.ClearDepthStencilView(_shadowDepthView!, DepthStencilClearFlags.Depth, 0, 0);
        _context.RSSetViewports([new Viewport(0, 0, lighting.ShadowResolution, lighting.ShadowResolution)]);
        _context.RSSetState(_rasterizerState);
        _context.OMSetBlendState(null!);
        _context.OMSetDepthStencilState(_shadowDepthState, 0);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_shadowPixelShader);
        if (_vertices.Count > 0)
        {
            UploadVertices();
            _context.IASetVertexBuffer(0, _vertexBuffer, VertexStride, 0);
            _context.Draw((uint)GetVertexCount(_vertices.Count), 0);
        }

        _context.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>(), null);
        _renderedShadowMapVersion = lighting.ShadowMapVersion;
    }

    private void EnsureShadowTargets(int resolution)
    {
        if (_shadowTexture is not null)
        {
            return;
        }

        _shadowTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)resolution,
            Height = (uint)resolution,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R32_Float,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        });
        _shadowRenderTarget = _device.CreateRenderTargetView(_shadowTexture);
        _shadowResource = _device.CreateShaderResourceView(_shadowTexture);
        _shadowDepthTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)resolution,
            Height = (uint)resolution,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D32_Float,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        });
        _shadowDepthView = _device.CreateDepthStencilView(_shadowDepthTexture);
    }

    private void EnsureFontTexture()
    {
        var atlas = UiFont.GetGlyphAtlas();
        if (_fontAtlasResolution == atlas.Resolution && _fontResource is not null)
        {
            return;
        }

        _fontResource?.Dispose();
        _fontTexture?.Dispose();
        _fontTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)atlas.Width,
            Height = (uint)atlas.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None
        });
        _context.Map(
            _fontTexture,
            0,
            MapMode.WriteDiscard,
            Vortice.Direct3D11.MapFlags.None,
            out var mapped);
        try
        {
            if (mapped.RowPitch == atlas.Width)
            {
                Marshal.Copy(atlas.Coverage, 0, mapped.DataPointer, atlas.Coverage.Length);
            }
            else
            {
                for (var row = 0; row < atlas.Height; row++)
                {
                    Marshal.Copy(
                        atlas.Coverage,
                        row * atlas.Width,
                        IntPtr.Add(mapped.DataPointer, checked(row * (int)mapped.RowPitch)),
                        atlas.Width);
                }
            }
        }
        finally
        {
            _context.Unmap(_fontTexture, 0);
        }

        _fontResource = _device.CreateShaderResourceView(_fontTexture);
        _fontAtlasResolution = atlas.Resolution;
    }

    private void ReleaseShadowTargets()
    {
        _shadowRenderTarget?.Dispose();
        _shadowRenderTarget = null;
        _shadowResource?.Dispose();
        _shadowResource = null;
        _shadowTexture?.Dispose();
        _shadowTexture = null;
        _shadowDepthView?.Dispose();
        _shadowDepthView = null;
        _shadowDepthTexture?.Dispose();
        _shadowDepthTexture = null;
        _renderedShadowMapVersion = long.MinValue;
    }

    private void AddShadowVertex(Lighting.ShadowCoordinate coordinate)
    {
        _vertices.Add(coordinate.X * 2 - 1);
        _vertices.Add(1 - coordinate.Y * 2);
        _vertices.Add(coordinate.Depth);
        _vertices.Add(1);
        _vertices.Add(1);
        _vertices.Add(1);
        _vertices.Add(1);
        _vertices.Add(-1);
        _vertices.Add(-1);
        _vertices.Add(0);
        _vertices.Add(0);
        _vertices.Add(1);
        _vertices.Add(1);
        _vertices.Add(1);
        _vertices.Add(0);
        _vertices.Add(0);
        _vertices.Add(0);
    }

    private bool TryGetCelestialScreenPosition(
        Lighting lighting,
        Camera camera,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float focalLength,
        out ScreenVertex screenPosition)
    {
        var direction = -lighting.DirectionalLightVector3;
        if (direction.Length <= 0.0001f)
        {
            screenPosition = default;
            return false;
        }

        direction = direction.Normalized();
        var depth = Vector3.Dot(direction, forward);
        if (depth <= 0.0001f)
        {
            screenPosition = default;
            return false;
        }

        var centerX = _renderWidth / 2f + Vector3.Dot(direction, right) * focalLength / depth;
        var centerY = _renderHeight / 2f - Vector3.Dot(direction, up) * focalLength / depth;
        var radius = MathF.Max(8, _renderHeight * 0.015f);
        if (centerX < -radius || centerX >= _renderWidth + radius ||
            centerY < -radius || centerY >= _renderHeight + radius)
        {
            screenPosition = default;
            return false;
        }

        screenPosition = new ScreenVertex(centerX, centerY, MathF.Max(10_000, camera.NearPlane + 1));
        return true;
    }

    private void AddSunDisc(ScreenVertex center, float radius)
    {
        const int segmentCount = 32;
        var color = new Color32(255, 244, 208);
        var previous = new ScreenVertex(center.X + radius, center.Y, center.Z);
        for (var index = 1; index <= segmentCount; index++)
        {
            var angle = index * (2 * MathF.PI / segmentCount);
            var current = new ScreenVertex(
                center.X + MathF.Cos(angle) * radius,
                center.Y + MathF.Sin(angle) * radius,
                center.Z);
            AddTriangle(center, previous, current, color);
            LastDrawCallCount++;
            previous = current;
        }
    }

    private void DrawPostProcess(
        Lighting lighting,
        ScreenVertex sunCenter,
        float sunRadius,
        bool lensFlareEnabled)
    {
        _vertices.Clear();
        var bloomStrength = lighting.RenderBloom
            ? System.Math.Clamp(lighting.BloomStrength, 0f, 1f)
            : 0f;
        var bloomThreshold = System.Math.Clamp(lighting.BloomThreshold, 0f, 1f);
        var sunX = lensFlareEnabled ? sunCenter.X / _renderWidth : 0;
        var sunY = lensFlareEnabled ? sunCenter.Y / _renderHeight : 0;
        var sunRadiusNormalized = lensFlareEnabled ? sunRadius / _renderHeight : 0;
        var flareStrength = lensFlareEnabled
            ? System.Math.Clamp(lighting.LensFlareStrength, 0f, 1f)
            : 0;
        var flareSpread = System.Math.Clamp(lighting.LensFlareSpread, 0f, 1f);
        AddFullscreenVertex(-1, -1, bloomStrength, bloomThreshold, bloomStrength > 0 ? 1 : 0,
            sunX, sunY, sunRadiusNormalized, flareStrength, flareSpread, lensFlareEnabled ? 1 : 0,
            lighting.SkyboxTint);
        AddFullscreenVertex(-1, 3, bloomStrength, bloomThreshold, bloomStrength > 0 ? 1 : 0,
            sunX, sunY, sunRadiusNormalized, flareStrength, flareSpread, lensFlareEnabled ? 1 : 0,
            lighting.SkyboxTint);
        AddFullscreenVertex(3, -1, bloomStrength, bloomThreshold, bloomStrength > 0 ? 1 : 0,
            sunX, sunY, sunRadiusNormalized, flareStrength, flareSpread, lensFlareEnabled ? 1 : 0,
            lighting.SkyboxTint);
        UploadVertices();

        _context.OMSetRenderTargets(_backBufferView!, null);
        _context.RSSetViewports([new Viewport(0, 0, _clientWidth, _clientHeight)]);
        _context.OMSetDepthStencilState(_noDepthState, 0);
        _context.IASetVertexBuffer(0, _vertexBuffer, VertexStride, 0);
        _context.PSSetShader(_postProcessPixelShader);
        _context.PSSetShaderResource(0, _postProcessResource!);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, null!);
        _context.PSSetShader(_pixelShader);
    }

    private void DrawUi(UiDrawList ui)
    {
        var uiVertices = ui.Vertices;
        if (uiVertices.Count == 0)
        {
            return;
        }

        var uiVertexCount = uiVertices.Count;
        EnsureFontTexture();
        UploadUiVertices(uiVertices, ui.Width, ui.Height);
        _context.OMSetRenderTargets(_backBufferView!, null);
        _context.RSSetViewports([new Viewport(0, 0, _clientWidth, _clientHeight)]);
        _context.OMSetDepthStencilState(_noDepthState, 0);
        _context.IASetInputLayout(_uiInputLayout);
        _context.IASetVertexBuffer(0, _vertexBuffer, sizeof(float) * 10, 0);
        _context.VSSetShader(_uiVertexShader);
        _context.PSSetShader(_uiPixelShader);
        _context.PSSetShaderResource(0, _fontResource!);
        _context.PSSetSampler(0, _uiSampler);
        _context.Draw((uint)uiVertexCount, 0);
        _context.PSSetShaderResource(0, null!);
    }

    private void AddFullscreenVertex(
        float x,
        float y,
        float bloomStrength,
        float bloomThreshold,
        float bloomEnabled,
        float sunX,
        float sunY,
        float sunRadius,
        float flareStrength,
        float flareSpread,
        float flareEnabled,
        Color32 bloomBackground)
    {
        _vertices.Add(x);
        _vertices.Add(y);
        _vertices.Add(0);
        _vertices.Add(0);
        _vertices.Add(bloomStrength);
        _vertices.Add(bloomThreshold);
        _vertices.Add(bloomEnabled);
        _vertices.Add(sunX);
        _vertices.Add(sunY);
        _vertices.Add(sunRadius);
        _vertices.Add(flareStrength);
        _vertices.Add(flareSpread);
        _vertices.Add(flareEnabled);
        _vertices.Add(1);
        _vertices.Add(bloomBackground.R / 255f);
        _vertices.Add(bloomBackground.G / 255f);
        _vertices.Add(bloomBackground.B / 255f);
    }

    private void CreateRenderTargets()
    {
        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferView = _device.CreateRenderTargetView(_backBuffer);
        _multisampleTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_clientWidth,
            Height = (uint)_clientHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription((uint)_sampleCount, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        });
        _renderTarget = _device.CreateRenderTargetView(_multisampleTexture);
        _postProcessTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_clientWidth,
            Height = (uint)_clientHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        });
        _postProcessResource = _device.CreateShaderResourceView(_postProcessTexture);
        var depthDescription = new Texture2DDescription
        {
            Width = (uint)_clientWidth,
            Height = (uint)_clientHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D32_Float,
            SampleDescription = new SampleDescription((uint)_sampleCount, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };
        _depthTexture = _device.CreateTexture2D(depthDescription);
        _depthView = _device.CreateDepthStencilView(_depthTexture);
    }

    private void ResolveSceneColor()
    {
        _context.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>(), null);
        if (_sampleCount == 1)
        {
            _context.CopyResource(_postProcessTexture!, _multisampleTexture!);
        }
        else
        {
            _context.ResolveSubresource(_postProcessTexture!, 0, _multisampleTexture!, 0, Format.R8G8B8A8_UNorm);
        }
    }

    private void ReleaseRenderTargets()
    {
        _depthView?.Dispose();
        _depthView = null;
        _depthTexture?.Dispose();
        _depthTexture = null;
        _postProcessResource?.Dispose();
        _postProcessResource = null;
        _postProcessTexture?.Dispose();
        _postProcessTexture = null;
        _backBufferView?.Dispose();
        _backBufferView = null;
        _renderTarget?.Dispose();
        _renderTarget = null;
        _multisampleTexture?.Dispose();
        _multisampleTexture = null;
        _backBuffer?.Dispose();
        _backBuffer = null;
    }

    private static ReadOnlyMemory<byte> CompileShader(string source, string entryPoint, string target) =>
        Compiler.Compile(source, entryPoint, "Tiny3D.hlsl", target);

    private readonly record struct OutlineEdge(Vector3 Start, Vector3 End, int TotalFaces, int FrontFaces);
}
