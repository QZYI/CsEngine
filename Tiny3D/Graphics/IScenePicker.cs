using Tiny3D.Scene;

namespace Tiny3D.Graphics;

public interface IScenePicker
{
    bool TryPick(int x, int y, out Entity? entity);
}
