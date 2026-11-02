using Tiny3D.Graphics;
using Tiny3D.Tools;

namespace Tiny3D.Scene;

/// <summary>
/// Mesh-backed construct used for visible scene objects. Entities are the most common
/// runtime object for geometry, colors, and nested transforms.
/// </summary>
public sealed class Entity : TransformableConstruct
{
    public Entity(Mesh mesh)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
    }

    public Mesh Mesh { get; }
    [Inspect("Rendering")]
    public Color32 Color { get; set; } = new(90, 170, 245);

    protected override Construct CreateDuplicate() => new Entity(Mesh) { Color = Color };
}
