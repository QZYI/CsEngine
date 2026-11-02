using Tiny3D.Math;

namespace Tiny3D.Graphics;

internal readonly record struct ScreenVertex(
    float X,
    float Y,
    float Z,
    float ShadowX = 0,
    float ShadowY = 0,
    float ShadowDepth = 0);

internal static class CameraProjection
{
    public static int ClipAndProjectTriangle(
        Vector3 worldA,
        Vector3 worldB,
        Vector3 worldC,
        Vector3 cameraPosition,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float focalLength,
        float nearPlane,
        int width,
        int height,
        Lighting lighting,
        Span<ScreenVertex> projected)
    {
        Span<ViewVertex> input = stackalloc ViewVertex[3];
        input[0] = ToView(worldA, cameraPosition, right, up, forward);
        input[1] = ToView(worldB, cameraPosition, right, up, forward);
        input[2] = ToView(worldC, cameraPosition, right, up, forward);
        Span<ViewVertex> clipped = stackalloc ViewVertex[4];
        var clippedCount = 0;
        var previous = input[^1];
        var previousInside = previous.Z >= nearPlane;

        foreach (var current in input)
        {
            var currentInside = current.Z >= nearPlane;
            if (previousInside != currentInside)
            {
                var amount = (nearPlane - previous.Z) / (current.Z - previous.Z);
                clipped[clippedCount++] = Interpolate(previous, current, amount, nearPlane);
            }

            if (currentInside)
            {
                clipped[clippedCount++] = current;
            }

            previous = current;
            previousInside = currentInside;
        }

        if (clippedCount < 3)
        {
            return 0;
        }

        var outputCount = 0;
        var first = Project(clipped[0], focalLength, width, height, lighting);
        for (var index = 1; index < clippedCount - 1; index++)
        {
            projected[outputCount++] = first;
            projected[outputCount++] = Project(clipped[index], focalLength, width, height, lighting);
            projected[outputCount++] = Project(clipped[index + 1], focalLength, width, height, lighting);
        }

        return outputCount;
    }

    private static ViewVertex ToView(
        Vector3 world,
        Vector3 cameraPosition,
        Vector3 right,
        Vector3 up,
        Vector3 forward)
    {
        var relative = world - cameraPosition;
        return new ViewVertex(
            Vector3.Dot(relative, right),
            Vector3.Dot(relative, up),
            Vector3.Dot(relative, forward),
            world);
    }

    private static ScreenVertex Project(
        ViewVertex point,
        float focalLength,
        int width,
        int height,
        Lighting lighting)
    {
        var shadow = lighting.GetShadowCoordinate(point.World);
        return new ScreenVertex(
            width / 2f + point.X * focalLength / point.Z,
            height / 2f - point.Y * focalLength / point.Z,
            point.Z,
            shadow.X,
            shadow.Y,
            shadow.Depth);
    }

    private static ViewVertex Interpolate(ViewVertex first, ViewVertex second, float amount, float nearPlane) =>
        new(
            first.X + (second.X - first.X) * amount,
            first.Y + (second.Y - first.Y) * amount,
            nearPlane,
            first.World + (second.World - first.World) * amount);

    private readonly record struct ViewVertex(float X, float Y, float Z, Vector3 World);
}
