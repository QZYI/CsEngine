using Tiny3D.Math;
using Tiny3D.Scene;
using Tiny3D.Tools;
using GameScene = Tiny3D.Scene.Scene;

namespace Tiny3D.Graphics;

/// <summary>CPU renderer used for headless rendering checks and software viewport picking.</summary>
public sealed class SoftwareRenderer : IScenePicker
{
    private static readonly byte[] DitherPattern = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
    private static ReadOnlySpan<int> BloomKernel => [1, 2, 3, 2, 1];
    private const float DitherFadeRange = 1f;
    private const int BloomSampleSpacing = 4;
    private static readonly Color32 DefaultBackgroundColor = new(74, 144, 226);
    private int _width;
    private int _height;
    private int[] _pixels;
    private float[] _depth;
    private int[] _pickIds;
    private Color32? _backgroundColorOverride;
    private IReadOnlyList<Entity> _pickEntities = [];
    private GameScene? _pickScene;
    private Camera? _lastCamera;
    private readonly List<DebugLine> _gizmoLines = [];

    public SoftwareRenderer(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Render dimensions must be positive.");
        }

        var bufferLength = checked(width * height);
        _width = width;
        _height = height;
        _pixels = new int[bufferLength];
        _depth = new float[bufferLength];
        _pickIds = new int[bufferLength];
    }

    public Color32 BackgroundColor
    {
        get => _backgroundColorOverride ?? DefaultBackgroundColor;
        set => _backgroundColorOverride = value;
    }
    public int LastDrawCallCount { get; private set; }
    public int Width => _width;
    public int Height => _height;
    internal Camera? LastCamera => _lastCamera;

    public void SetSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Render dimensions must be positive.");
        }

        var bufferLength = checked(width * height);
        if (width == _width && height == _height)
        {
            return;
        }

        var pixels = new int[bufferLength];
        var depth = new float[bufferLength];
        var pickIds = new int[bufferLength];
        _width = width;
        _height = height;
        _pixels = pixels;
        _depth = depth;
        _pickIds = pickIds;
        _pickEntities = [];
        _pickScene = null;
    }

    /// <summary>Renders a scene and optional selection/debug overlays into the reusable pixel buffer.</summary>
    /// <param name="ditherFade">Enables depth-based dithering for geometry near the camera.</param>
    public int[] Render(
        GameScene scene,
        Camera camera,
        Construct? selected = null,
        Construct? hovered = null,
        DebugDraw? debugDraw = null,
        TransformGizmoMode gizmoMode = TransformGizmoMode.Position,
        float ditherFade = 0)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        var backgroundColor = _backgroundColorOverride ?? scene.Lighting.SkyboxTint;
        Array.Fill(_pixels, backgroundColor.ToPixel());
        Array.Clear(_depth);
        Array.Clear(_pickIds);
        LastDrawCallCount = 0;
        var entities = scene.Entities;
        _pickEntities = entities;
        _pickScene = scene;

        if (camera.NearPlane <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "The camera near plane must be positive.");
        }

        if (camera.FieldOfView <= 0 || camera.FieldOfView >= 180)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "The camera field of view must be between 0 and 180 degrees.");
        }

        var direction = camera.Target - camera.Position;
        if (direction.Length < 0.0001f)
        {
            throw new ArgumentException("The camera position and target must differ.", nameof(camera));
        }

        _lastCamera = camera;
        var forward = direction.Normalized();
        var right = Vector3.Cross(Vector3.Up, forward).Normalized();
        var up = Vector3.Cross(forward, right).Normalized();
        var fov = camera.FieldOfView * (MathF.PI / 180);
        var focalLength = _height / (2 * MathF.Tan(fov / 2));

        var selectedId = FindEntityId(entities, selected);
        var hoveredId = FindEntityId(entities, hovered);
        var selectedBounds = ScreenBounds.Empty;
        var hoveredBounds = ScreenBounds.Empty;
        Span<ScreenVertex> projected = stackalloc ScreenVertex[6];
        scene.Lighting.PrepareShadows(entities);
        var sunCenter = default(ScreenVertex);
        var sunRadius = 0f;
        var celestialPositionVisible = TryGetCelestialScreenPosition(
            scene.Lighting, camera, right, up, forward, focalLength, out sunCenter, out sunRadius);
        var sunVisible = celestialPositionVisible && scene.Lighting.CelestialBodiesShown;
        if (sunVisible)
        {
            DrawSunDisc(sunCenter, sunRadius);
        }

        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            var entity = entities[entityIndex];
            if (!entity.IsVisibleInTree)
            {
                continue;
            }

            var vertices = entity.Mesh.Vertices;
            var indices = entity.Mesh.Indices;

            for (var i = 0; i < indices.Length; i += 3)
            {
                var worldA = entity.TransformPoint(vertices[indices[i]]);
                var worldB = entity.TransformPoint(vertices[indices[i + 1]]);
                var worldC = entity.TransformPoint(vertices[indices[i + 2]]);
                var normal = Vector3.Cross(worldB - worldA, worldC - worldA).Normalized();
                var center = (worldA + worldB + worldC) / 3;
                var lighting = scene.Lighting.CalculateIntensity(normal, center, entities, entity);
                var shadedColor = entity.Color.Shade(lighting);
                var projectedCount = CameraProjection.ClipAndProjectTriangle(
                    worldA, worldB, worldC, camera.Position, right, up, forward, focalLength,
                    camera.NearPlane, _width, _height, scene.Lighting, projected);
                for (var vertex = 0; vertex < projectedCount; vertex += 3)
                {
                    var a = projected[vertex];
                    var b = projected[vertex + 1];
                    var c = projected[vertex + 2];
                    if (entityIndex + 1 == selectedId)
                    {
                        selectedBounds = selectedBounds.Include(a, b, c, _width, _height);
                    }
                    if (entityIndex + 1 == hoveredId)
                    {
                        hoveredBounds = hoveredBounds.Include(a, b, c, _width, _height);
                    }
                    LastDrawCallCount++;
                    DrawTriangle(a, b, c, shadedColor, entityIndex + 1, ditherFade, camera.NearPlane);
                }
            }
        }

        if (scene.Lighting.RenderBloom && scene.Lighting.BloomStrength > 0)
        {
            ApplyBloom(scene.Lighting.BloomStrength, scene.Lighting.BloomThreshold, backgroundColor);
        }

        if (celestialPositionVisible && sunCenter.X >= 0 && sunCenter.X < _width &&
            sunCenter.Y >= 0 && sunCenter.Y < _height &&
            scene.Lighting.RenderLensFlare && scene.Lighting.LensFlareStrength > 0 &&
            _pickIds[(int)sunCenter.Y * _width + (int)sunCenter.X] == 0)
        {
            ApplyLensFlare(sunCenter, sunRadius, scene.Lighting.LensFlareStrength, scene.Lighting.LensFlareSpread);
        }

        DrawSelectionOutlines(hoveredId, hoveredBounds, selectedId, selectedBounds);

        if (debugDraw is not null)
        {
            foreach (var line in debugDraw.Lines)
            {
                if (TryProject(line.Start, camera, right, up, forward, focalLength, out var start) &&
                    TryProject(line.End, camera, right, up, forward, focalLength, out var end))
                {
                    DrawProjectedLine(start, end, line.Color);
                }
            }

            foreach (var text in debugDraw.Texts)
            {
                if (TryProject(text.Position, camera, right, up, forward, focalLength, out var position) &&
                    position.X >= 0 && position.X < _width && position.Y >= 0 && position.Y < _height)
                {
                    UiCanvas.DrawText(_pixels, _width, _height, (int)position.X, (int)position.Y, text.Text, text.Color);
                }
            }
        }

        if (selected is ITransformableConstruct)
        {
            DrawTransformGizmo(selected, gizmoMode, camera, right, up, forward, focalLength);
        }

        scene.UI.Render(_pixels, _width, _height);
        return _pixels;
    }

    private bool TryGetCelestialScreenPosition(
        Lighting lighting,
        Camera camera,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float focalLength,
        out ScreenVertex screenPosition,
        out float radius)
    {
        var direction = -lighting.DirectionalLightVector3;
        if (direction.Length <= 0.0001f)
        {
            screenPosition = default;
            radius = 0;
            return false;
        }

        direction = direction.Normalized();
        var depth = Vector3.Dot(direction, forward);
        if (depth <= 0.0001f)
        {
            screenPosition = default;
            radius = 0;
            return false;
        }

        var centerX = _width / 2f + Vector3.Dot(direction, right) * focalLength / depth;
        var centerY = _height / 2f - Vector3.Dot(direction, up) * focalLength / depth;
        radius = MathF.Max(8, _height * 0.015f);
        if (centerX < -radius || centerX >= _width + radius ||
            centerY < -radius || centerY >= _height + radius)
        {
            screenPosition = default;
            radius = 0;
            return false;
        }

        screenPosition = new ScreenVertex(
            centerX, centerY, MathF.Max(10_000, camera.NearPlane + 1));
        return true;
    }

    private void DrawSunDisc(ScreenVertex center, float radius)
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
            DrawTriangle(center, previous, current, color, 0, 0, 0);
            LastDrawCallCount++;
            previous = current;
        }
    }

    private void ApplyLensFlare(ScreenVertex sunCenter, float sunRadius, float strength, float spread)
    {
        var sunX = sunCenter.X;
        var sunY = sunCenter.Y;
        var screenCenterX = _width * 0.5f;
        var screenCenterY = _height * 0.5f;
        var axisX = sunX - screenCenterX;
        var axisY = sunY - screenCenterY;
        var axisLength = MathF.Sqrt(axisX * axisX + axisY * axisY);
        if (axisLength > 0.001f)
        {
            axisX /= axisLength;
            axisY /= axisLength;
        }

        var extent = sunRadius * (5 + System.Math.Clamp(spread, 0f, 1f) * 10);
        var minimumX = System.Math.Max(0, (int)MathF.Floor(sunX - extent));
        var maximumX = System.Math.Min(_width - 1, (int)MathF.Ceiling(sunX + extent));
        var minimumY = System.Math.Max(0, (int)MathF.Floor(sunY - extent));
        var maximumY = System.Math.Min(_height - 1, (int)MathF.Ceiling(sunY + extent));
        var flareStrength = System.Math.Clamp(strength, 0f, 1f);
        var flareSpread = System.Math.Clamp(spread, 0f, 1f);

        for (var y = minimumY; y <= maximumY; y++)
        {
            for (var x = minimumX; x <= maximumX; x++)
            {
                var deltaX = x + 0.5f - sunX;
                var deltaY = y + 0.5f - sunY;
                var distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                var glow = MathF.Exp(-distance / (sunRadius * (1 + flareSpread * 8)));
                var haloRadius = sunRadius * (2 + flareSpread * 8);
                var halo = MathF.Exp(-MathF.Abs(distance - haloRadius) /
                    MathF.Max(sunRadius * flareSpread * 2, 1));
                var ghostAX = screenCenterX - (sunX - screenCenterX) * 0.45f;
                var ghostAY = screenCenterY - (sunY - screenCenterY) * 0.45f;
                var ghostBX = screenCenterX - (sunX - screenCenterX) * 0.15f;
                var ghostBY = screenCenterY - (sunY - screenCenterY) * 0.15f;
                var ghostA = MathF.Exp(-((x - ghostAX) * (x - ghostAX) + (y - ghostAY) * (y - ghostAY)) /
                    (sunRadius * sunRadius * 36)) * 0.5f;
                var ghostB = MathF.Exp(-((x - ghostBX) * (x - ghostBX) + (y - ghostBY) * (y - ghostBY)) /
                    (sunRadius * sunRadius * 16)) * 0.35f;
                var streakDistance = MathF.Abs(deltaX * -axisY + deltaY * axisX);
                var streak = MathF.Exp(-streakDistance / MathF.Max(sunRadius * flareSpread * 2, 1)) *
                    MathF.Exp(-distance / (sunRadius * (5 + flareSpread * 10))) * 0.18f;
                var amount = (glow * 0.18f + halo * 0.16f + ghostA + ghostB + streak) * flareStrength;
                if (amount <= 0.002f)
                {
                    continue;
                }

                var pixelIndex = y * _width + x;
                var pixel = _pixels[pixelIndex];
                var red = System.Math.Clamp(((pixel >> 16) & 0xFF) + (int)(255 * amount), 0, 255);
                var green = System.Math.Clamp(((pixel >> 8) & 0xFF) + (int)(197 * amount), 0, 255);
                var blue = System.Math.Clamp((pixel & 0xFF) + (int)(110 * amount), 0, 255);
                _pixels[pixelIndex] = (pixel & unchecked((int)0xFF000000)) |
                    (red << 16) | (green << 8) | blue;
            }
        }
    }

    private void DrawTransformGizmo(
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
                TryProject(line.End, camera, right, up, forward, focalLength, out var end))
            {
                DrawProjectedLine(start, end, line.Color);
            }
        }

        if (TryProject(origin, camera, right, up, forward, focalLength, out var pivot))
        {
            var x = (int)MathF.Round(pivot.X);
            var y = (int)MathF.Round(pivot.Y);
            var marker = new Color32(255, 232, 160);
            DrawLine(x - 5, y, x + 5, y, marker);
            DrawLine(x, y - 5, x, y + 5, marker);
        }
    }

    private void ApplyBloom(float strength, float threshold, Color32 background)
    {
        var source = (int[])_pixels.Clone();
        var bloomStrength = System.Math.Clamp(strength, 0f, 1f) * 3f;
        var bloomThreshold = System.Math.Clamp(threshold, 0f, 1f);
        var backgroundRed = background.R / 255f;
        var backgroundGreen = background.G / 255f;
        var backgroundBlue = background.B / 255f;
        var effectiveThreshold = MathF.Max(bloomThreshold,
            backgroundRed * 0.2126f + backgroundGreen * 0.7152f + backgroundBlue * 0.0722f + 0.04f);
        var kernel = BloomKernel;
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                var pixelIndex = y * _width + x;
                var red = 0f;
                var green = 0f;
                var blue = 0f;
                for (var kernelY = 0; kernelY < kernel.Length; kernelY++)
                {
                    var sampleY = y + (kernelY - 2) * BloomSampleSpacing;
                    if ((uint)sampleY >= (uint)_height)
                    {
                        continue;
                    }

                    for (var kernelX = 0; kernelX < kernel.Length; kernelX++)
                    {
                        var sampleX = x + (kernelX - 2) * BloomSampleSpacing;
                        if ((uint)sampleX >= (uint)_width)
                        {
                            continue;
                        }

                        var sourceIndex = sampleY * _width + sampleX;
                        if (_pickIds[sourceIndex] == 0)
                        {
                            continue;
                        }

                        var color = source[sourceIndex];
                        var sourceRed = ((color >> 16) & 0xFF) / 255f;
                        var sourceGreen = ((color >> 8) & 0xFF) / 255f;
                        var sourceBlue = (color & 0xFF) / 255f;
                        var brightness = MathF.Max(sourceRed, MathF.Max(sourceGreen, sourceBlue));
                        var backgroundDifference =
                            (sourceRed - backgroundRed) * (sourceRed - backgroundRed) +
                            (sourceGreen - backgroundGreen) * (sourceGreen - backgroundGreen) +
                            (sourceBlue - backgroundBlue) * (sourceBlue - backgroundBlue);
                        if (backgroundDifference < 0.0016f || brightness <= effectiveThreshold)
                        {
                            continue;
                        }

                        var contribution = (brightness - effectiveThreshold) / brightness *
                            kernel[kernelX] * kernel[kernelY];
                        red += sourceRed * contribution;
                        green += sourceGreen * contribution;
                        blue += sourceBlue * contribution;
                    }
                }

                var destination = source[pixelIndex];
                var resultRed = System.Math.Clamp(((destination >> 16) & 0xFF) + (int)(red / 81 * bloomStrength * 255), 0, 255);
                var resultGreen = System.Math.Clamp(((destination >> 8) & 0xFF) + (int)(green / 81 * bloomStrength * 255), 0, 255);
                var resultBlue = System.Math.Clamp((destination & 0xFF) + (int)(blue / 81 * bloomStrength * 255), 0, 255);
                _pixels[pixelIndex] = (destination & unchecked((int)0xFF000000)) |
                    (resultRed << 16) | (resultGreen << 8) | resultBlue;
            }
        }
    }

    public bool TryPick(int x, int y, out Entity? entity)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
        {
            entity = null;
            return false;
        }

        var id = _pickIds[y * _width + x];
        if (id == 0 || id > _pickEntities.Count)
        {
            entity = null;
            return false;
        }

        entity = _pickEntities[id - 1];
        if (!ReferenceEquals(entity.OwningScene, _pickScene))
        {
            entity = null;
            return false;
        }

        return true;
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
        var z = Vector3.Dot(relative, forward);
        if (!float.IsFinite(z) || z < camera.NearPlane)
        {
            vertex = default;
            return false;
        }

        var x = _width / 2f + Vector3.Dot(relative, right) * focalLength / z;
        var y = _height / 2f - Vector3.Dot(relative, up) * focalLength / z;
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            vertex = default;
            return false;
        }

        vertex = new ScreenVertex(x, y, z);
        return true;
    }

    private void DrawTriangle(
        ScreenVertex a,
        ScreenVertex b,
        ScreenVertex c,
        Color32 color,
        int entityId,
        float ditherFade,
        float nearPlane)
    {
        var area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (MathF.Abs(area) < 0.0001f)
        {
            return;
        }

        if (area < 0)
        {
            (b, c) = (c, b);
            area = -area;
        }

        var minimumX = MathF.Min(a.X, MathF.Min(b.X, c.X));
        var maximumX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        var minimumY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
        var maximumY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
        if (maximumX < 0 || maximumY < 0 || minimumX >= _width || minimumY >= _height)
        {
            return;
        }

        var minX = System.Math.Clamp((int)MathF.Floor(minimumX), 0, _width - 1);
        var maxX = System.Math.Clamp((int)MathF.Ceiling(maximumX), 0, _width - 1);
        var minY = System.Math.Clamp((int)MathF.Floor(minimumY), 0, _height - 1);
        var maxY = System.Math.Clamp((int)MathF.Ceiling(maximumY), 0, _height - 1);
        var pixel = color.ToPixel();
        var inverseArea = 1 / area;
        var inverseAZ = inverseArea / a.Z;
        var inverseBZ = inverseArea / b.Z;
        var inverseCZ = inverseArea / c.Z;
        var sampleX = minX + 0.5f;
        var sampleY = minY + 0.5f;
        var edgeAStart = Edge(b.X, b.Y, c.X, c.Y, sampleX, sampleY);
        var edgeBStart = Edge(c.X, c.Y, a.X, a.Y, sampleX, sampleY);
        var edgeCStart = Edge(a.X, a.Y, b.X, b.Y, sampleX, sampleY);
        var edgeAStepX = c.Y - b.Y;
        var edgeBStepX = a.Y - c.Y;
        var edgeCStepX = b.Y - a.Y;
        var edgeAStepY = b.X - c.X;
        var edgeBStepY = c.X - a.X;
        var edgeCStepY = a.X - b.X;

        var rowEdgeA = edgeAStart;
        var rowEdgeB = edgeBStart;
        var rowEdgeC = edgeCStart;
        for (var y = minY; y <= maxY; y++)
        {
            var edgeA = rowEdgeA;
            var edgeB = rowEdgeB;
            var edgeC = rowEdgeC;
            var index = y * _width + minX;
            for (var x = minX; x <= maxX; x++, index++)
            {
                if (edgeA >= 0 && edgeB >= 0 && edgeC >= 0)
                {
                    var inverseZ = edgeA * inverseAZ + edgeB * inverseBZ + edgeC * inverseCZ;
                    var fade = ditherFade > 0 && inverseZ > 0
                        ? System.Math.Clamp(ditherFade, 0f, 1f) *
                            System.Math.Clamp((nearPlane + DitherFadeRange - 1 / inverseZ) / DitherFadeRange, 0f, 1f)
                        : 0f;
                    var patternIndex = (y & 3) * 4 + (x & 3);
                    if (DitherPattern[patternIndex] / 16f >= fade && inverseZ > _depth[index])
                    {
                        _depth[index] = inverseZ;
                        _pixels[index] = pixel;
                        _pickIds[index] = entityId;
                    }
                }

                edgeA += edgeAStepX;
                edgeB += edgeBStepX;
                edgeC += edgeCStepX;
            }

            rowEdgeA += edgeAStepY;
            rowEdgeB += edgeBStepY;
            rowEdgeC += edgeCStepY;
        }
    }

    private static int FindEntityId(IReadOnlyList<Entity> entities, Construct? construct)
    {
        if (construct is null)
        {
            return 0;
        }

        for (var i = 0; i < entities.Count; i++)
        {
            if (ReferenceEquals(entities[i], construct) && entities[i].IsVisibleInTree)
            {
                return i + 1;
            }
        }

        return 0;
    }

    private void DrawSelectionOutlines(
        int hoveredId,
        ScreenBounds hoveredBounds,
        int selectedId,
        ScreenBounds selectedBounds)
    {
        if (hoveredId == 0 && selectedId == 0)
        {
            return;
        }

        if (selectedId != 0)
        {
            DrawOutline(selectedBounds, selectedId, new Color32(255, 202, 92).ToPixel());
        }
        if (hoveredId != 0 && hoveredId != selectedId)
        {
            DrawOutline(hoveredBounds, hoveredId, new Color32(92, 190, 245).ToPixel());
        }
    }

    private void DrawOutline(ScreenBounds bounds, int entityId, int outlinePixel)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        for (var y = bounds.Top; y <= bounds.Bottom; y++)
        {
            for (var x = bounds.Left; x <= bounds.Right; x++)
            {
                var index = y * _width + x;
                if (_pickIds[index] != entityId)
                {
                    continue;
                }

                var isBoundary = false;
                for (var offsetY = -1; offsetY <= 1 && !isBoundary; offsetY++)
                {
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        var neighborX = x + offsetX;
                        var neighborY = y + offsetY;
                        if ((uint)neighborX >= (uint)_width || (uint)neighborY >= (uint)_height ||
                            _pickIds[neighborY * _width + neighborX] != entityId)
                        {
                            isBoundary = true;
                            break;
                        }
                    }
                }

                if (isBoundary)
                {
                    _pixels[index] = outlinePixel;
                }
            }
        }
    }

    private void DrawLine(int x0, int y0, int x1, int y1, Color32 color)
    {
        var dx = System.Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -System.Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        var pixel = color.ToPixel();
        while (true)
        {
            if ((uint)x0 < (uint)_width && (uint)y0 < (uint)_height)
            {
                _pixels[y0 * _width + x0] = pixel;
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var doubledError = 2 * error;
            if (doubledError >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (doubledError <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private void DrawProjectedLine(ScreenVertex start, ScreenVertex end, Color32 color)
    {
        double x0 = start.X;
        double y0 = start.Y;
        double x1 = end.X;
        double y1 = end.Y;
        if (!double.IsFinite(x0) || !double.IsFinite(y0) ||
            !double.IsFinite(x1) || !double.IsFinite(y1))
        {
            return;
        }

        var originalX = x0;
        var originalY = y0;
        var deltaX = x1 - x0;
        var deltaY = y1 - y0;
        var startRatio = 0d;
        var endRatio = 1d;
        if (!Clip(-deltaX, x0, ref startRatio, ref endRatio) ||
            !Clip(deltaX, _width - 1 - x0, ref startRatio, ref endRatio) ||
            !Clip(-deltaY, y0, ref startRatio, ref endRatio) ||
            !Clip(deltaY, _height - 1 - y0, ref startRatio, ref endRatio))
        {
            return;
        }

        DrawLine(
            (int)System.Math.Round(originalX + startRatio * deltaX),
            (int)System.Math.Round(originalY + startRatio * deltaY),
            (int)System.Math.Round(originalX + endRatio * deltaX),
            (int)System.Math.Round(originalY + endRatio * deltaY),
            color);
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

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private readonly record struct ScreenBounds(int Left, int Top, int Right, int Bottom)
    {
        public static ScreenBounds Empty => new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
        public bool IsEmpty => Right < Left || Bottom < Top;

        public ScreenBounds Include(ScreenVertex a, ScreenVertex b, ScreenVertex c, int width, int height)
        {
            var minimumX = MathF.Min(a.X, MathF.Min(b.X, c.X));
            var maximumX = MathF.Max(a.X, MathF.Max(b.X, c.X));
            var minimumY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
            var maximumY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
            if (maximumX < 0 || maximumY < 0 || minimumX >= width || minimumY >= height)
            {
                return this;
            }

            var left = System.Math.Clamp((int)MathF.Floor(minimumX), 0, width - 1);
            var right = System.Math.Clamp((int)MathF.Ceiling(maximumX), 0, width - 1);
            var top = System.Math.Clamp((int)MathF.Floor(minimumY), 0, height - 1);
            var bottom = System.Math.Clamp((int)MathF.Ceiling(maximumY), 0, height - 1);
            if (IsEmpty)
            {
                return new ScreenBounds(left, top, right, bottom);
            }

            return new ScreenBounds(
                System.Math.Min(Left, left),
                System.Math.Min(Top, top),
                System.Math.Max(Right, right),
                System.Math.Max(Bottom, bottom));
        }
    }
}
