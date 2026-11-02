using Tiny3D.Math;
using Tiny3D.Tools;

namespace Tiny3D.Scene;

/// <summary>
/// Local transform data for a scene construct. Position, rotation, and scale are all
/// relative to the construct's parent branch.
/// </summary>
public sealed class Pivot
{
    private readonly Action? _onChanged;
    private Vector3 _position = Vector3.Zero;
    private Vector3 _rotation = Vector3.Zero;
    private Vector3 _scale = Vector3.One;

    public Pivot()
    {
    }

    internal Pivot(Action onChanged) =>
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

    /// <summary>Gets or sets the local translation.</summary>
    [Inspect("Pivot")]
    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_position.Equals(value))
            {
                return;
            }

            _position = value;
            _onChanged?.Invoke();
        }
    }

    /// <summary>Gets or sets the local Euler rotation in radians.</summary>
    [Inspect("Pivot")]
    public Vector3 Rotation
    {
        get => _rotation;
        set
        {
            if (_rotation.Equals(value))
            {
                return;
            }

            _rotation = value;
            _onChanged?.Invoke();
        }
    }

    /// <summary>Gets or sets the local scale on each axis.</summary>
    [Inspect("Pivot")]
    public Vector3 Scale
    {
        get => _scale;
        set
        {
            if (_scale.Equals(value))
            {
                return;
            }

            _scale = value;
            _onChanged?.Invoke();
        }
    }

    internal Vector3 Apply(Vector3 point)
    {
        var x = point.X * Scale.X;
        var y = point.Y * Scale.Y;
        var z = point.Z * Scale.Z;

        var cx = MathF.Cos(Rotation.X);
        var sx = MathF.Sin(Rotation.X);
        (y, z) = (y * cx - z * sx, y * sx + z * cx);

        var cy = MathF.Cos(Rotation.Y);
        var sy = MathF.Sin(Rotation.Y);
        (x, z) = (x * cy + z * sy, -x * sy + z * cy);

        var cz = MathF.Cos(Rotation.Z);
        var sz = MathF.Sin(Rotation.Z);
        (x, y) = (x * cz - y * sz, x * sz + y * cz);

        return new Vector3(x, y, z) + Position;
    }
}
