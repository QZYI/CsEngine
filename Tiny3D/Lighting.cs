using Tiny3D.Graphics;
using Tiny3D.Math;
using Tiny3D.Scene;
using Tiny3D.Tools;

namespace Tiny3D;

/// <summary>Global scene lighting settings and shadow computations for the Tiny3D renderer.</summary>
public sealed class Lighting : Construct
{
    internal override bool IsNameReadOnly => true;

    internal readonly record struct ShadowCoordinate(float X, float Y, float Depth);
    private const int ShadowMapResolution = 1024;
    private readonly float[] _shadowDepth = new float[ShadowMapResolution * ShadowMapResolution];
    private readonly List<ShadowSnapshot> _shadowSnapshot = [];
    private Vector3 _shadowRight;
    private Vector3 _shadowUp;
    private Vector3 _shadowDirection;
    private float _shadowMinimumX;
    private float _shadowMinimumY;
    private float _shadowMinimumDepth;
    private float _shadowExtentX = 1f;
    private float _shadowExtentY = 1f;
    private float _shadowExtentDepth = 1f;
    private float _shadowBias = 0.01f;
    private bool _shadowMapReady;
    private bool _cpuShadowMapReady;
    private Vector3 _preparedLightDirection;
    private long _shadowMapVersion;

    public Lighting() => Name = "Lighting";

    [Inspect("Sky")]
    public Color32 SkyboxTint { get; set; } = new(38, 90, 160);
    [Inspect("Sky")]
    [Range(0, 1)]
    public float SkyboxHaze { get; set; } = 0.18f;
    [Inspect("Shadows")]
    public bool RenderShadows { get; set; } = true;
    [Inspect("Bloom")]
    public bool RenderBloom { get; set; }
    [Inspect("Bloom")]
    [Range(0, 1)]
    public float BloomStrength { get; set; } = 0.25f;
    [Inspect("Bloom")]
    [Range(0, 1)]
    public float BloomThreshold { get; set; } = 0.35f;
    [Inspect("Celestial Bodies")]
    public bool CelestialBodiesShown { get; set; } = true;
    [Inspect("Celestial Bodies")]
    public bool RenderLensFlare { get; set; } = true;
    [Inspect("Celestial Bodies")]
    [Range(0, 1)]
    public float LensFlareStrength { get; set; } = 0.5f;
    [Inspect("Celestial Bodies")]
    [Range(0, 1)]
    public float LensFlareSpread { get; set; } = 0.5f;
    [Inspect("Lighting")]
    [Range(0, 1)]
    public float AmbientLighting { get; set; } = 0.3f;
    [Inspect("Lighting")]
    public Vector3 DirectionalLightVector3 { get; set; } = new(-0.4f, -0.7f, -1f);

    public float CalculateIntensity(Vector3 normal) =>
        CalculateIntensity(normal, Vector3.Zero, Array.Empty<Entity>());

    internal float CalculateDiffuse(Vector3 normal)
    {
        var direction = DirectionalLightVector3;
        if (direction.Length <= 0.0001f || normal.Length <= 0.0001f)
        {
            return 0f;
        }

        return MathF.Max(0f, Vector3.Dot(normal.Normalized(), -direction.Normalized()));
    }

    internal float AmbientIntensity => System.Math.Clamp(AmbientLighting, 0f, 1f);
    internal int ShadowResolution => ShadowMapResolution;
    internal float[] ShadowDepth => _shadowDepth;
    internal float ShadowBias => _shadowBias;
    internal bool ShadowMapReady => _shadowMapReady;
    internal long ShadowMapVersion => _shadowMapVersion;

    internal ShadowCoordinate GetShadowCoordinate(Vector3 position)
    {
        if (!_shadowMapReady)
        {
            return new ShadowCoordinate(-1, -1, 0);
        }

        return new ShadowCoordinate(
            (Vector3.Dot(position, _shadowRight) - _shadowMinimumX) / _shadowExtentX,
            1f - (Vector3.Dot(position, _shadowUp) - _shadowMinimumY) / _shadowExtentY,
            (Vector3.Dot(position, _shadowDirection) - _shadowMinimumDepth) / _shadowExtentDepth);
    }

    public float CalculateIntensity(
        Vector3 normal,
        Vector3 position,
        IReadOnlyList<Entity> sceneEntities,
        Entity? receivingEntity = null)
    {
        if (RenderShadows)
        {
            PrepareShadows(sceneEntities);
        }

        var direction = DirectionalLightVector3;
        if (direction.Length <= 0.0001f)
        {
            return System.Math.Clamp(AmbientLighting, 0f, 1f);
        }

        var lightDirection = direction.Normalized();
        var normalizedNormal = normal.Length <= 0.0001f ? Vector3.Zero : normal.Normalized();
        var toLight = -lightDirection;
        var diffuse = MathF.Max(0f, Vector3.Dot(normalizedNormal, toLight));
        if (diffuse <= 0f)
        {
            return System.Math.Clamp(AmbientLighting, 0f, 1f);
        }

        var visibility = RenderShadows && _shadowMapReady
            ? SampleShadowVisibility(position)
            : 1f;

        return System.Math.Clamp(AmbientLighting + diffuse * visibility, 0f, 1f);
    }

    public Color32 MixSkyboxColor(Color32 color, float exposure = 1f)
    {
        var haze = System.Math.Clamp(SkyboxHaze, 0f, 1f);
        var contribution = 1f - haze;
        var source = color;
        var sky = SkyboxTint;
        return new Color32(
            (byte)System.Math.Clamp((source.R * contribution) + (sky.R * haze) * exposure, 0, 255),
            (byte)System.Math.Clamp((source.G * contribution) + (sky.G * haze) * exposure, 0, 255),
            (byte)System.Math.Clamp((source.B * contribution) + (sky.B * haze) * exposure, 0, 255),
            source.A);
    }

    protected override Construct CreateDuplicate() =>
        throw new NotSupportedException("The Lighting service cannot be duplicated.");

    internal void PrepareShadows(IReadOnlyList<Entity> entities) =>
        PrepareShadows(entities, rasterize: true);

    internal void PrepareShadowProjection(IReadOnlyList<Entity> entities) =>
        PrepareShadows(entities, rasterize: false);

    private void PrepareShadows(IReadOnlyList<Entity> entities, bool rasterize)
    {
        if (!RenderShadows || DirectionalLightVector3.Length <= 0.0001f)
        {
            _shadowMapReady = false;
            _cpuShadowMapReady = false;
            return;
        }

        if (_shadowMapReady && _preparedLightDirection.Equals(DirectionalLightVector3) &&
            MatchesShadowSnapshot(entities))
        {
            if (rasterize && !_cpuShadowMapReady)
            {
                RasterizeShadowMap(entities);
            }
            return;
        }

        _shadowMapReady = false;
        _cpuShadowMapReady = false;
        _shadowDirection = -DirectionalLightVector3.Normalized();
        var referenceUp = MathF.Abs(Vector3.Dot(_shadowDirection, Vector3.Up)) > 0.98f
            ? new Vector3(0, 0, 1)
            : Vector3.Up;
        _shadowRight = Vector3.Cross(referenceUp, _shadowDirection).Normalized();
        _shadowUp = Vector3.Cross(_shadowDirection, _shadowRight).Normalized();

        var minimumX = float.PositiveInfinity;
        var maximumX = float.NegativeInfinity;
        var minimumY = float.PositiveInfinity;
        var maximumY = float.NegativeInfinity;
        var minimumDepth = float.PositiveInfinity;
        var maximumDepth = float.NegativeInfinity;
        foreach (var entity in entities)
        {
            if (!entity.IsVisibleInTree)
            {
                continue;
            }

            foreach (var vertex in entity.Mesh.Vertices)
            {
                var world = entity.TransformPoint(vertex);
                var x = Vector3.Dot(world, _shadowRight);
                var y = Vector3.Dot(world, _shadowUp);
                var depth = Vector3.Dot(world, _shadowDirection);
                minimumX = MathF.Min(minimumX, x);
                maximumX = MathF.Max(maximumX, x);
                minimumY = MathF.Min(minimumY, y);
                maximumY = MathF.Max(maximumY, y);
                minimumDepth = MathF.Min(minimumDepth, depth);
                maximumDepth = MathF.Max(maximumDepth, depth);
            }
        }

        if (!float.IsFinite(minimumX) || !float.IsFinite(minimumY))
        {
            _shadowMinimumDepth = 0;
            _shadowExtentDepth = 1;
            CaptureShadowSnapshot(entities);
            _preparedLightDirection = DirectionalLightVector3;
            _shadowMapReady = true;
            _shadowMapVersion++;
            if (rasterize)
            {
                Array.Fill(_shadowDepth, float.NegativeInfinity);
                _cpuShadowMapReady = true;
            }
            return;
        }

        var paddingX = MathF.Max((maximumX - minimumX) * 0.02f, 0.01f);
        var paddingY = MathF.Max((maximumY - minimumY) * 0.02f, 0.01f);
        var paddingDepth = MathF.Max((maximumDepth - minimumDepth) * 0.02f, 0.01f);
        _shadowMinimumX = minimumX - paddingX;
        _shadowMinimumY = minimumY - paddingY;
        _shadowMinimumDepth = minimumDepth - paddingDepth;
        _shadowExtentX = MathF.Max(maximumX - minimumX + 2 * paddingX, 0.01f);
        _shadowExtentY = MathF.Max(maximumY - minimumY + 2 * paddingY, 0.01f);
        _shadowExtentDepth = MathF.Max(maximumDepth - minimumDepth + 2 * paddingDepth, 0.01f);
        _shadowBias = 2f / ShadowMapResolution;

        CaptureShadowSnapshot(entities);
        _preparedLightDirection = DirectionalLightVector3;
        _shadowMapReady = true;
        _shadowMapVersion++;
        if (rasterize)
        {
            RasterizeShadowMap(entities);
        }
    }

    private void RasterizeShadowMap(IReadOnlyList<Entity> entities)
    {
        Array.Fill(_shadowDepth, float.NegativeInfinity);
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
                RasterizeShadowTriangle(
                    entity.TransformPoint(vertices[indices[index]]),
                    entity.TransformPoint(vertices[indices[index + 1]]),
                    entity.TransformPoint(vertices[indices[index + 2]]));
            }
        }

        _cpuShadowMapReady = true;
    }

    private bool MatchesShadowSnapshot(IReadOnlyList<Entity> entities)
    {
        if (entities.Count != _shadowSnapshot.Count)
        {
            return false;
        }

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var cached = _shadowSnapshot[index];
            if (!ReferenceEquals(entity, cached.Entity) ||
                !ReferenceEquals(entity.Mesh, cached.Mesh) ||
                entity.TransformRevision != cached.TransformRevision ||
                entity.IsVisibleInTree != cached.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private void CaptureShadowSnapshot(IReadOnlyList<Entity> entities)
    {
        _shadowSnapshot.Clear();
        foreach (var entity in entities)
        {
            _shadowSnapshot.Add(new ShadowSnapshot(
                entity,
                entity.Mesh,
                entity.TransformRevision,
                entity.IsVisibleInTree));
        }
    }

    private float SampleShadowVisibility(Vector3 position)
    {
        var coordinate = GetShadowCoordinate(position);
        var x = coordinate.X * ShadowMapResolution;
        var y = coordinate.Y * ShadowMapResolution;
        if (x < 0 || y < 0 || x >= ShadowMapResolution || y >= ShadowMapResolution)
        {
            return 1f;
        }

        var pixelX = System.Math.Clamp((int)x, 0, ShadowMapResolution - 1);
        var pixelY = System.Math.Clamp((int)y, 0, ShadowMapResolution - 1);
        var visibleSamples = 0;
        var sampleCount = 0;
        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            var sampleY = pixelY + offsetY;
            if ((uint)sampleY >= ShadowMapResolution)
            {
                continue;
            }

            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                var sampleX = pixelX + offsetX;
                if ((uint)sampleX >= ShadowMapResolution)
                {
                    continue;
                }

                var shadowDepth = _shadowDepth[sampleY * ShadowMapResolution + sampleX];
                if (shadowDepth <= coordinate.Depth + _shadowBias * 4)
                {
                    visibleSamples++;
                }
                sampleCount++;
            }
        }

        return sampleCount == 0 ? 1f : visibleSamples / (float)sampleCount;
    }

    private void RasterizeShadowTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c)
    {
        var screenA = ToShadowScreen(a);
        var screenB = ToShadowScreen(b);
        var screenC = ToShadowScreen(c);
        var area = ShadowEdge(screenA.X, screenA.Y, screenB.X, screenB.Y, screenC.X, screenC.Y);
        if (MathF.Abs(area) < 0.000001f)
        {
            return;
        }

        if (area < 0)
        {
            (screenB, screenC) = (screenC, screenB);
            area = -area;
        }

        var triangleMinimumX = MathF.Min(screenA.X, MathF.Min(screenB.X, screenC.X));
        var triangleMaximumX = MathF.Max(screenA.X, MathF.Max(screenB.X, screenC.X));
        var triangleMinimumY = MathF.Min(screenA.Y, MathF.Min(screenB.Y, screenC.Y));
        var triangleMaximumY = MathF.Max(screenA.Y, MathF.Max(screenB.Y, screenC.Y));
        if (triangleMaximumX < 0 || triangleMaximumY < 0 ||
            triangleMinimumX >= ShadowMapResolution || triangleMinimumY >= ShadowMapResolution)
        {
            return;
        }

        var minimumX = System.Math.Clamp((int)MathF.Floor(triangleMinimumX), 0, ShadowMapResolution - 1);
        var maximumX = System.Math.Clamp((int)MathF.Ceiling(triangleMaximumX), 0, ShadowMapResolution - 1);
        var minimumY = System.Math.Clamp((int)MathF.Floor(triangleMinimumY), 0, ShadowMapResolution - 1);
        var maximumY = System.Math.Clamp((int)MathF.Ceiling(triangleMaximumY), 0, ShadowMapResolution - 1);
        var inverseArea = 1f / area;
        var edgeAX = screenC.Y - screenB.Y;
        var edgeBX = screenA.Y - screenC.Y;
        var edgeCX = screenB.Y - screenA.Y;
        for (var y = minimumY; y <= maximumY; y++)
        {
            var sampleY = y + 0.5f;
            var sampleX = minimumX + 0.5f;
            var weightA = ShadowEdge(screenB.X, screenB.Y, screenC.X, screenC.Y, sampleX, sampleY);
            var weightB = ShadowEdge(screenC.X, screenC.Y, screenA.X, screenA.Y, sampleX, sampleY);
            var weightC = ShadowEdge(screenA.X, screenA.Y, screenB.X, screenB.Y, sampleX, sampleY);
            var depth = (
                weightA * screenA.Depth +
                weightB * screenB.Depth +
                weightC * screenC.Depth) * inverseArea;
            for (var x = minimumX; x <= maximumX; x++)
            {
                if (weightA >= 0 && weightB >= 0 && weightC >= 0)
                {
                    var pixel = y * ShadowMapResolution + x;
                    _shadowDepth[pixel] = MathF.Max(_shadowDepth[pixel], depth);
                }

                weightA += edgeAX;
                weightB += edgeBX;
                weightC += edgeCX;
                depth += (
                    edgeAX * screenA.Depth +
                    edgeBX * screenB.Depth +
                    edgeCX * screenC.Depth) * inverseArea;
            }
        }
    }

    private ShadowScreenVertex ToShadowScreen(Vector3 world)
    {
        var lightX = Vector3.Dot(world, _shadowRight);
        var lightY = Vector3.Dot(world, _shadowUp);
        return new ShadowScreenVertex(
            (lightX - _shadowMinimumX) / _shadowExtentX * (ShadowMapResolution - 1),
            (1f - (lightY - _shadowMinimumY) / _shadowExtentY) * (ShadowMapResolution - 1),
            (Vector3.Dot(world, _shadowDirection) - _shadowMinimumDepth) / _shadowExtentDepth);
    }

    private static float ShadowEdge(float ax, float ay, float bx, float by, float px, float py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private readonly record struct ShadowScreenVertex(float X, float Y, float Depth);
    private readonly record struct ShadowSnapshot(
        Entity Entity,
        Mesh Mesh,
        long TransformRevision,
        bool IsVisible);
}
