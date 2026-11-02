namespace Tiny3D.Scene;

/// <summary>A transformless scene-graph container for entity and UI constructs.</summary>
public sealed class EmptyConstruct : Construct
{
    protected override Construct CreateDuplicate() => new EmptyConstruct();
}
