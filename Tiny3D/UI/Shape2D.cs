using Tiny3D.Scene;

namespace Tiny3D.UI;

/// <summary>A filled screen-space rectangle with optional rounded corners and outline.</summary>
public sealed class Shape2D : UIElement
{
    protected override Construct CreateDuplicate()
    {
        var duplicate = new Shape2D();
        CopyTo(duplicate);
        return duplicate;
    }
}
