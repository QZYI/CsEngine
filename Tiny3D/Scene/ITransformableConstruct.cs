namespace Tiny3D.Scene;

/// <summary>Scene-graph construct with a local position, rotation, and scale pivot.</summary>
public interface ITransformableConstruct
{
    Pivot Pivot { get; }
}
