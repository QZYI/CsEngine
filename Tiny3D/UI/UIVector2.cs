using Tiny3D.Math;

namespace Tiny3D.UI;

/// <summary>Combines screen-relative coordinates with absolute pixel offsets.</summary>
public readonly record struct UIVector2(Vector2 RelativeScale, Vector2 AbsoluteOffset)
{
    public static UIVector2 Zero => new(Vector2.Zero, Vector2.Zero);
    public static UIVector2 One => new(Vector2.One, Vector2.Zero);

    /// <summary>Resolves relative coordinates against a parent size and adds pixel offsets.</summary>
    public Vector2 Resolve(float parentWidth, float parentHeight) =>
        new(
            RelativeScale.X * parentWidth + AbsoluteOffset.X,
            RelativeScale.Y * parentHeight + AbsoluteOffset.Y);

    public static UIVector2 FromPixels(float x, float y) =>
        new(Vector2.Zero, new Vector2(x, y));
}
