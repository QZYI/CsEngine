using Tiny3D.Graphics;
using Tiny3D.Math;
using Tiny3D.Scene;

namespace Tiny3D.Tools;

internal static class TransformGizmoGeometry
{
    private static readonly (Vector3 Axis, Vector3 First, Vector3 Second, Color32 Color)[] Axes =
    [
        (new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1), new Color32(255, 92, 92)),
        (new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Color32(110, 230, 130)),
        (new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Color32(100, 155, 255))
    ];

    public static (Vector3 Origin, float Size) GetFrame(Construct construct, Vector3 cameraPosition)
    {
        var origin = construct.TransformPoint(Vector3.Zero);
        var distance = (origin - cameraPosition).Length;
        return (origin, System.Math.Clamp(distance * 0.12f, 0.5f, 2.5f));
    }

    public static IReadOnlyList<(Vector3 Axis, Vector3 First, Vector3 Second, Color32 Color)> GetAxes() => Axes;

    public static Vector3 AddLines(
        Construct construct,
        TransformGizmoMode mode,
        Vector3 cameraPosition,
        List<DebugLine> lines)
    {
        ArgumentNullException.ThrowIfNull(construct);
        ArgumentNullException.ThrowIfNull(lines);

        var (origin, size) = GetFrame(construct, cameraPosition);

        foreach (var (axis, first, second, color) in Axes)
        {
            if (mode == TransformGizmoMode.Rotation)
            {
                AddRing(origin, size, first, second, color, lines);
            }
            else
            {
                AddAxis(origin, size, axis, first, second, color, mode, lines);
            }
        }

        return origin;
    }

    private static void AddAxis(
        Vector3 origin,
        float size,
        Vector3 axis,
        Vector3 first,
        Vector3 second,
        Color32 color,
        TransformGizmoMode mode,
        List<DebugLine> lines)
    {
        var end = origin + axis * size;
        lines.Add(new DebugLine(origin, end, color));
        if (mode == TransformGizmoMode.Scale)
        {
            var handleSize = size * 0.08f;
            Span<Vector3> corners = stackalloc Vector3[4];
            corners[0] = end + first * handleSize + second * handleSize;
            corners[1] = end - first * handleSize + second * handleSize;
            corners[2] = end - first * handleSize - second * handleSize;
            corners[3] = end + first * handleSize - second * handleSize;
            for (var index = 0; index < corners.Length; index++)
            {
                lines.Add(new DebugLine(corners[index], corners[(index + 1) % corners.Length], color));
            }
            return;
        }

        var tip = end - axis * (size * 0.16f);
        var wing = size * 0.08f;
        lines.Add(new DebugLine(end, tip + first * wing, color));
        lines.Add(new DebugLine(end, tip - first * wing, color));
        lines.Add(new DebugLine(end, tip + second * wing, color));
        lines.Add(new DebugLine(end, tip - second * wing, color));
    }

    private static void AddRing(
        Vector3 origin,
        float radius,
        Vector3 first,
        Vector3 second,
        Color32 color,
        List<DebugLine> lines)
    {
        const int segmentCount = 32;
        var previous = origin + first * radius;
        for (var segment = 1; segment <= segmentCount; segment++)
        {
            var angle = segment * (2 * MathF.PI / segmentCount);
            var current = origin + first * (MathF.Cos(angle) * radius) +
                second * (MathF.Sin(angle) * radius);
            lines.Add(new DebugLine(previous, current, color));
            previous = current;
        }
    }
}
