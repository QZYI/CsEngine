using Tiny3D.Math;

namespace Tiny3D.Graphics;

/// <summary>Immutable indexed triangle geometry shared by mesh-backed entities.</summary>
public sealed class Mesh
{
    private readonly Vector3[] _vertices;
    private readonly int[] _indices;

    /// <summary>Creates a mesh from vertices and triangle indices.</summary>
    public Mesh(IEnumerable<Vector3> vertices, IEnumerable<int> triangleIndices)
    {
        _vertices = vertices.ToArray();
        _indices = triangleIndices.ToArray();

        if (_vertices.Length == 0 || _indices.Length == 0 || _indices.Length % 3 != 0)
        {
            throw new ArgumentException("A mesh needs vertices and complete triangles.");
        }

        if (_indices.Any(index => index < 0 || index >= _vertices.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(triangleIndices), "Mesh indices must reference existing vertices.");
        }
    }

    internal Vector3[] Vertices => _vertices;
    internal int[] Indices => _indices;

    /// <summary>Creates a centered cube with the requested positive edge length.</summary>
    public static Mesh CreateCube(float size = 1)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Cube size must be positive.");
        }

        var h = size / 2;
        return new Mesh(
            [
                new(-h, -h, -h), new(h, -h, -h), new(h, h, -h), new(-h, h, -h),
                new(-h, -h, h), new(h, -h, h), new(h, h, h), new(-h, h, h)
            ],
            [
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2
            ]);
    }
}
