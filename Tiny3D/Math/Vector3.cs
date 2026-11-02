namespace Tiny3D.Math;

public readonly struct Vector3(float x, float y, float z)
{
    public float X { get; } = x;
    public float Y { get; } = y;
    public float Z { get; } = z;

    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3 Normalized()
    {
        var length = Length;
        return length > 0 ? this / length : Zero;
    }

    public static Vector3 Zero => new(0, 0, 0);
    public static Vector3 One => new(1, 1, 1);
    public static Vector3 Up => new(0, 1, 0);

    public static float Dot(Vector3 a, Vector3 b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3 Cross(Vector3 a, Vector3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static Vector3 operator +(Vector3 a, Vector3 b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3 operator -(Vector3 a, Vector3 b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3 operator -(Vector3 value) =>
        new(-value.X, -value.Y, -value.Z);

    public static Vector3 operator *(Vector3 value, float scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    public static Vector3 operator /(Vector3 value, float scale) =>
        new(value.X / scale, value.Y / scale, value.Z / scale);
}
