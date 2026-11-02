# Tiny3D

Tiny3D is a small C# 3D framework for building scene-based applications. It
provides a Windows game loop, a Direct3D 11 renderer, a scene graph, screen-space
UI, basic lighting, and an in-game editor.

For the execution and rendering internals, see [how-it-works.md](how-it-works.md).

## Requirements

- .NET 8 SDK
- Windows for the built-in window and Direct3D 11 runtime
- A Direct3D 11-compatible GPU and driver

Build the solution from the repository root:

```powershell
dotnet build Tiny3D.sln
```

Run the demo or the checks:

```powershell
dotnet run --project samples\Tiny3D.Demo
dotnet run --project tests\Tiny3D.Tests
```

The demo shows a baseplate and animated cubes. Its default baseplate is
configured in `default-template-settings.json`. Use WASD to move horizontally,
Q/E to move vertically, the arrow keys to rotate, right-drag to look around,
middle-drag to pan, the mouse wheel to dolly, and Escape to exit.

## Create a game

Reference `Tiny3D\Tiny3D.csproj` from an application targeting `net8.0`, then
derive from `Game`. Add constructs to `Scene` and implement `Update`:

```csharp
using Tiny3D;
using Tiny3D.Graphics;
using Tiny3D.Input;
using Tiny3D.Math;
using Tiny3D.Scene;

var game = new SampleGame();
game.Run();

sealed class SampleGame : Game
{
    private readonly Entity _cube = new(Mesh.CreateCube());

    public SampleGame() : base(title: "My Tiny3D game")
    {
        Scene.Add(_cube);
        CameraNode.Position = new Vector3(0, 0, -5);
    }

    protected override void Update(float deltaSeconds)
    {
        _cube.Pivot.Rotation += new Vector3(0, deltaSeconds, 0);
        if (Input.IsKeyDown(Key.Left))
        {
            _cube.Pivot.Position += new Vector3(-deltaSeconds, 0, 0);
        }
    }
}
```

`Game` accepts optional `width`, `height`, `title`, and `EngineSettings`
constructor arguments. Defaults are 1920×1080, `"Tiny3D Game"`, and settings
loaded from `EngineSettings.json` when available.

## Core API

| API | Use |
|---|---|
| `Game` | Owns the scene, camera, settings, editor, input, and runtime loop. Override `Initialize()` for one-time setup and `Update(float deltaSeconds)` for game logic. Call `Run()` for the windowed runtime or `RunHeadless(frameCount)` for simulation without a window or renderer. |
| `Scene` | Owns the root constructs, `Lighting`, and screen-space `UI`. Use `Scene.Add(...)` and `Scene.Remove(...)` for scene roots. `Scene.Entities` provides the mesh-backed entities. |
| `Construct` | Base for scene-tree objects. Use `AddDescendant`/`RemoveDescendant` to parent nodes, `UpdatePriority` to order sibling updates, and `QueueFree()` to defer disposal safely during a scene update. Override lifecycle/update methods for custom behavior. |
| `EmptyConstruct` | Transformless grouping node for organizing related entities and UI elements. |
| `Entity` | A visible mesh-backed construct. Set its `Mesh`, `Color`, and `Pivot` transform. |
| `Mesh` | Immutable indexed triangle geometry. Use `new Mesh(vertices, triangleIndices)` or `Mesh.CreateCube(size)`. |
| `CameraNode` | The game's scene-attached camera controller. Set its `Position`, `Rotation`, `FieldOfView`, or use `Move`, `Rotate`, and `Pan`. Rotation values are radians. |
| `Input` | Protected `Game` property exposing key state/transitions and mouse position, buttons, delta, and wheel movement. Use `IsKeyDown` for held input and `WasKeyPressed` for a one-time press. |
| `Lighting` | `Game.Lighting` and `Scene.Lighting` expose sky, ambient/directional lighting, shadows, bloom, celestial body, and lens-flare settings. |
| `EngineSettings` | Configures UI font resolution, dynamic renderer buffer allocation, frame cap, antialiasing, and depth-based mesh fade. |

Custom constructs can override `OnEnterBranch`, `Ready`, `Update`,
`FixedUpdate`, `OnExitBranch`, and `OnDispose`. `Ready` runs once when a
construct first enters a scene tree. A construct can be duplicated with
`DuplicateTree()` if its type implements `CreateDuplicate()`.

### Transforms and camera

`Entity` and `CameraConstruct` have a local `Pivot` containing `Position`,
`Rotation`, and `Scale`. A pivot is relative to its parent; parent transforms
contribute to descendant world transforms. `Vector3` rotation values are Euler
angles in radians. A `Camera` uses a world-space position and target, a vertical
field of view in degrees, and a near clipping distance.

### Screen-space UI

Add `Shape2D` and `TextBox2D` elements through `Scene.UI`. UI positions and
sizes use `UIVector2`: a relative component multiplied by the parent size, plus
an absolute pixel offset.

```csharp
using Tiny3D.Graphics;
using Tiny3D.Math;
using Tiny3D.UI;

var panel = new Shape2D
{
    Position = new UIVector2(new Vector2(0.02f, 0.03f), Vector2.Zero),
    Scale = new UIVector2(new Vector2(0.3f, 0.15f), Vector2.Zero),
    CornerRadius = UIVector2.FromPixels(8, 8),
    ClipDescendants = true
};
Scene.UI.Add(panel);

var label = new TextBox2D
{
    Position = UIVector2.FromPixels(12, 8),
    Scale = UIVector2.FromPixels(220, 32),
    TextContent = "SCORE: 0",
    TextColor = new Color32(255, 255, 255)
};
panel.AddChild(label);
```

UI elements can be nested, layered, clipped, rotated, and styled with color,
opacity, rounded corners, and outlines. `TextBox2D` also supports wrapping,
boundary fitting, horizontal/vertical alignment, line and glyph spacing, and a
visible-glyph limit. Hover and keyboard events are available through
`OnElementMouseHover`, `OnElementMouseExit`, `OnButtonDown`, and `OnButtonUp`.

### Rendering and lighting features

- Indexed colored triangles, perspective projection, near-plane clipping, and
  depth testing.
- Configurable scene multisampling: `SSAAStrength` selects 1×, 2×, or 4×
  samples. UI rounded edges have their own coverage antialiasing setting.
- Ambient and directional lighting, geometry-derived shadows, sky tint,
  `Lighting.MixSkyboxColor` haze blending, optional bloom, a Sun, and
  screen-space lens flare.
- Optional `EngineSettings.DitherFade` to fade mesh surfaces close to the
  camera.
- `SoftwareRenderer` for CPU rendering checks and viewport picking.

Tiny3D does not currently provide materials, general-purpose texture mapping,
audio, physics, or a cross-platform window backend. Its built-in window host is
Windows-only.

### Runtime editor and diagnostics

The windowed runtime includes a scene explorer, inspector, color wheel, console,
log, profiler, viewport selection, and transform gizmos. Right-click the
explorer to add an entity, UI element, or empty construct. Drag tree rows to
reparent; inspector edits and supported construct operations participate in
undo/redo.

| Shortcut | Action |
|---|---|
| F1 | Show or hide the runtime profiler |
| F2 | Show or hide the console |
| F3 | Show or hide the engine log |
| F4 | Show or hide the editor |
| F5 | Show or hide the scene explorer |
| 1 / 2 / 3 | Choose position / scale / rotation gizmo |
| Ctrl+D / Ctrl+C / Ctrl+V / Ctrl+X | Duplicate / copy / paste / cut the selected construct |
| Backspace | Delete the selected construct |

`Game` exposes `Profiler`, `Log`, `Console`, `DebugDraw`, `UndoHistory`, and
`Editor`. Register console commands with `Console.RegisterCommand`; variables
can be read and set with `Console.Execute`. The runtime uses the `fps_max`
variable for its frame cap. `Game.WatchAssets(directory, callback)` watches for
file changes and invokes the reload callback on the game thread.

## Configuration

`EngineSettings.Load(path)` loads and validates a JSON settings file.
`EngineSettings.LoadDefault()` checks the application output directory, then
`Tiny3D\EngineSettings.json` under the current directory, and otherwise returns
the defaults.

| Setting | Default | Valid values / effect |
|---|---:|---|
| `FontResolution` | 18 | Settings validation accepts 8–24 pixels; the current `Game` editor requires 8–18 |
| `MemoryAllocation` | 67,108,864 bytes | 4,096 through `Int32.MaxValue`, divisible by four; dynamic renderer vertex-buffer limit |
| `FramerateCap` | 60 | 0–1,000 FPS; zero disables frame limiting |
| `SSAAStrength` | 1 | 0–1; 0 disables multisampling, values below 0.5 select 2×, and values of 0.5 or greater select 4× |
| `DitherFade` | `false` | Enables depth-based dither fading near the camera |

`AntiAliasing` and `AntiAliasingStrength` are compatibility aliases for
`SSAAStrength`.

## More detail

See [how-it-works.md](how-it-works.md) for the frame-loop order, scene lifecycle,
projection and rendering pipeline, UI layout and composition, and runtime
tooling behavior.
