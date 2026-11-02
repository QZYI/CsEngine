namespace Tiny3D.Scene;

/// <summary>Base for constructs that contribute a local pivot to scene transforms.</summary>
public abstract class TransformableConstruct : Construct, ITransformableConstruct
{
    protected TransformableConstruct() => Pivot = new Pivot(MarkTransformChanged);

    public Pivot Pivot { get; }
}
