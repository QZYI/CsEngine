using Tiny3D.Math;

namespace Tiny3D.Graphics;

/// <summary>Perspective camera pose and projection settings used by the renderers.</summary>
public sealed class Camera
{
    /// <summary>Camera position in world space.</summary>
    public Vector3 Position { get; set; } = new(0, 0, -5);
    /// <summary>World-space point the camera looks toward.</summary>
    public Vector3 Target { get; set; } = Vector3.Zero;
    /// <summary>Vertical field of view, in degrees.</summary>
    public float FieldOfView { get; set; } = 60;
    /// <summary>Near clipping distance in world units.</summary>
    public float NearPlane { get; set; } = 0.1f;
}
