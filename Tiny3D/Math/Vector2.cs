namespace Tiny3D.Math;

/// <summary>A two-dimensional vector with the same arithmetic helpers as <see cref="Vector3"/>.</summary>
public readonly record struct Vector2(float X, float Y)
{
    public float Length => MathF.Sqrt(X * X + Y * Y);

    public Vector2 Normalized()
    {
        var length = Length;
        return length > 0 ? this / length : Zero;
    }

    public static Vector2 Zero => new(0, 0);
    public static Vector2 One => new(1, 1);
    public static Vector2 Up => new(0, 1);

    public static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;

    public static Vector2 operator +(Vector2 a, Vector2 b) =>
        new(a.X + b.X, a.Y + b.Y);

    public static Vector2 operator -(Vector2 a, Vector2 b) =>
        new(a.X - b.X, a.Y - b.Y);

    public static Vector2 operator -(Vector2 value) =>
        new(-value.X, -value.Y);

    public static Vector2 operator *(Vector2 value, float scale) =>
        new(value.X * scale, value.Y * scale);

    public static Vector2 operator /(Vector2 value, float scale) =>
        new(value.X / scale, value.Y / scale);
}
