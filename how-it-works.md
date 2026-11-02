# How Tiny3D Works

This document describes the current implementation: what the application does
from startup through presentation, how scene and UI data are prepared, and which
subsystems are involved. It complements the public-facing API and examples in
the [README](README.md).

## System overview

```text
Game
├── Scene
│   ├── Construct tree
│   │   ├── EmptyConstruct (optional grouping)
│   │   ├── Entity -> Mesh + Color + Pivot
│   │   └── UIElement -> Shape2D / TextBox2D
│   ├── Lighting
│   └── UI API
├── Camera / CameraNode
├── InputState
├── EngineUi (editor and tools)
└── Direct3D11Renderer (windowed) or SoftwareRenderer (CPU)
```

The scene tree is the shared source of object ownership, lifecycle, transforms,
visibility, and update ordering. `Entity` is a transformable scene construct
with a mesh and color. UI elements are also constructs, but use screen-space
layout rather than a 3D pivot. `Scene.Lighting` is a scene-wide service and
`Game.CameraNode` is a scene construct that synchronizes the camera pose.

## Startup and frame execution

### Windowed runtime

`Game.Run()` creates the native Windows window and Direct3D 11 device, compiles
the renderer shaders, creates its swap chain and render targets, and calls the
game's `Initialize()` exactly once. A game instance is intended to be run only
once.

Each windowed frame proceeds roughly as follows:

1. Pump Windows messages and drain queued asset-change callbacks. File-watcher
   events are placed on a concurrent queue; the reload callback runs on the game
   thread.
2. Measure elapsed time and clamp the simulation frame delta to at most 0.25
   seconds.
3. Read the current client size. The internal render size is reduced as needed
   to stay within the dimensions passed to the `Game` constructor, preserving
   the client aspect ratio. The renderer, editor, and pointer input are updated
   to use that viewport.
4. Update scene UI hover/input and process editor input.
5. Advance a 1/60-second accumulator. For each fixed tick, call
   `Scene.FixedUpdate()`, then `Game.Update(1/60)`, then finish that input
   update. A slow frame can therefore run multiple fixed ticks.
6. Finish UI input processing, run `Scene.Update(frameDelta)` once, and
   synchronize the `CameraNode` pose to the `Camera`.
7. Build the editor/UI draw list and render the scene, selected/hovered
   overlays, debug drawing, gizmo, and UI.
8. Wait for any remaining `fps_max` frame budget and record profiler timings,
   allocation, garbage-collection counts, and draw calls.

`fps_max` is initialized from `EngineSettings.FramerateCap`. Setting it to zero
disables the wait-based frame limit. The limit does not use display
synchronization.

### Headless runtime

`RunHeadless(frameCount, deltaSeconds)` runs initialization, scene and game
updates, and profiling without creating a window or graphics device. It uses a
headless input state and does not render. Each simulated frame runs scene
variable update, scene fixed update, UI input update, game update, and camera
synchronization. This mode is useful for deterministic simulation and
construct-lifecycle tests; the windowed and headless update order is not
identical.

## Scene graph and lifecycle

`Scene` owns root constructs and caches the ordered root list and flattened
entity list. Child constructs belong to their parent's branch. A node cannot be
attached to two scenes at once, and parenting prevents cycles.

When a construct is attached:

1. Its `OwningScene` is set.
2. `OnEnterBranch()` runs.
3. `Ready()` runs once for that construct's lifetime.
4. Its descendants are entered recursively.

On detachment, descendant exit callbacks run before the parent's
`OnExitBranch()`. During a tick, constructs update depth-first in
`UpdatePriority` order. Visibility is inherited down the tree; it affects
rendering, not whether update callbacks run.

`QueueFree()` marks an attached construct for disposal after the current
iteration, so a callback can request its own removal without changing the
collection currently being traversed. `DuplicateTree()` creates a detached
copy and recursively duplicates descendants; custom construct types opt in by
implementing `CreateDuplicate()`.

### Transforms

`Pivot` stores local translation, Euler rotation in radians, and scale. The
engine applies scale, then X/Y/Z Euler rotation, then translation. World
positions are computed by applying the node's local transform followed by each
ancestor's transform. A transform revision is propagated when a pivot or
parent relationship changes; the lighting system uses these revisions when
checking whether its cached shadow map is still valid.

## 3D render pipeline

The windowed renderer prepares geometry on the CPU and sends triangle vertices
to Direct3D 11 for rasterization and presentation:

1. Collect `Scene.Entities` in scene traversal order and skip entities hidden
   by themselves or an ancestor.
2. Transform each indexed mesh triangle from object-local coordinates into
   world space and calculate its face normal.
3. Derive camera forward/right/up vectors from camera position and target.
   Perspective projection uses the vertical field of view and render height.
4. Clip triangles against the camera near plane before projection. A triangle
   crossing that plane is clipped and may become a two-triangle polygon fan;
   triangles entirely behind the plane are dropped.
5. Prepare the directional shadow map when shadows are enabled. The light-space
   bounds are fitted to visible scene geometry, and a snapshot of transforms,
   mesh references, and visibility is used to reuse the map until relevant
   scene or light data changes.
6. Append projected triangle data, color, lighting, shadow coordinates, and
   optional dither-fade data to a dynamic vertex buffer. The buffer grows as
   required but is bounded by `MemoryAllocation`.
7. Clear the scene color/depth targets, draw the scene with depth testing and
   the selected sample count, then resolve multisampled color into a
   single-sample texture.
8. If enabled, run a fullscreen post-process pass for bloom and/or lens flare.
   Otherwise copy the resolved scene to the back buffer.
9. Draw screen-space UI on the back buffer without 3D depth testing, then
   present the swap chain.

### Shading and effects

Directional diffuse light is based on the triangle normal and the configured
light direction. Ambient and diffuse contributions are combined and clamped.
When shadows are enabled, the light-space depth map modulates the diffuse term.
`SkyboxTint` sets the cleared scene color; the lighting API also exposes
`SkyboxHaze` and a helper to mix a color toward the sky tint.

`SSAAStrength` selects the Direct3D scene target sample count: 0 uses one sample,
a positive value below 0.5 uses two, and 0.5 or more uses four. This is
multisample antialiasing rather than a larger internal render resolution. UI
rounded edges use per-element coverage sampling, separately controlled by the
UI element's `SSAAStrength`.

Bloom is opt-in. The post-process shader samples a small neighborhood,
thresholds bright pixels, and adds a glow. When the Sun is in the camera view,
the renderer draws a Sun disc; lens flare is a post-process effect along the
screen-space line from that position. `DitherFade` passes depth-based fade data
to the scene shader, which uses a small repeating threshold pattern to discard
fragments close to the camera.

### Resize and viewport mapping

The swap chain and GPU render targets follow the client window size. The
projection and UI use internal dimensions capped by the `Game` constructor
dimensions; vertices are normalized against that logical viewport and
rasterized across the client-sized render target. The logical viewport shares
the client aspect ratio, so the image fills the window without letterboxing;
the vertical field of view stays constant and wider windows reveal more of the
scene horizontally. Pointer coordinates are mapped to the internal dimensions
so editor selection and gizmos use the same coordinate space as the logical
viewport.

## Screen-space UI

`Scene.UI` owns and updates screen-space input behavior for UI constructs. At
layout time, the engine walks scene roots and resolves each visible UI element
relative to its parent bounds:

- `UIVector2.RelativeScale` is multiplied by the parent width or height.
- `UIVector2.AbsoluteOffset` is added in pixels.
- `AnchorPoint` offsets the element's origin within its resolved bounds.
- `ClipDescendants` intersects the descendants' clip rectangle with the
  element's bounds.

Elements are rendered in ascending `Layer` order; equal layers preserve the
tree traversal order. `Rendered = false` removes the element and its subtree
from layout/rendering. `Visible = false` also hides the subtree. Rounded
rectangles, outlines, opacity, rotation, and antialiasing are prepared by the
UI renderer. Text is laid out with a built-in rasterized font, supports newlines
and optional wrapping, and can be scaled to fit its bounds. Rotated UI hit
testing transforms the pointer back into the element's local rectangle.

On pointer movement, the topmost hit UI element receives hover-enter/exit
events. A key press is delivered to the hovered element; its corresponding
key-release event is delivered to the same element if it is still attached to
the scene. The native host supplies input from Windows messages. The editor can
consume camera keys and mouse buttons while a text field, panel, or gizmo is
being manipulated.

In the windowed runtime, `EngineUi.BuildDrawList()` includes both game UI and
engine-editor panels. The Direct3D renderer uploads that draw list and draws it
after the 3D scene and post-processing. `SoftwareRenderer.Render()` draws game
UI into its CPU pixel buffer.

## Camera and picking

`CameraNode` is a transformable construct that synchronizes the camera's
position, target, and field of view. Its `Move`, `Rotate`, and `Pan` helpers
respect `LockedMovement`. The camera looks along its local positive Z direction;
the view target is maintained five world units forward from its position.

The Direct3D renderer retains the scene and camera context used for the current
viewport and ray-tests geometry for editor selection. `SoftwareRenderer`
provides a CPU rasterized color/depth/pick buffer and can map a pixel back to
its visible `Entity`. Picking is an editor/tool facility, separate from normal
game updates.

## Editor and runtime tools

`EngineUi` renders the scene explorer, inspector, console, engine log, profiler,
and viewport gizmos. The explorer can search, select, hover, and reparent
constructs; it also exposes registered services such as `Lighting`. The
inspector discovers `[Inspect]` properties, applies `[Range]` constraints, and
records supported edits in `UndoHistory`. Explicit typed property registration
is available through `InspectionRegistry`.

The viewport gizmos edit position, scale, and rotation for selected
transformable constructs, and provide corresponding 2D transforms for selected
UI elements. Editor keyboard shortcuts are summarized in the
[README](README.md#runtime-editor-and-diagnostics).

Other runtime services:

- `RuntimeProfiler` retains a bounded frame history including update/render
  timings, allocations, garbage collections, and draw calls.
- `EngineLog` stores a bounded, thread-safe log; logging can be written from
  other threads.
- `DebugConsole` executes registered commands and reads/writes string
  variables. Console execution and scene mutation are main-thread operations.
- `DebugDraw` queues world-space lines, boxes, and text for rendering until
  they are removed with `Clear()`.
- `AssetWatcher` reports file changes from a watcher thread. `Game.WatchAssets`
  queues callbacks for execution on the game thread.
- `ConstructPool<T>` reuses detached constructs and can run rent/return
  callbacks.

## Configuration and implementation boundaries

`EngineSettings.LoadDefault()` first checks for `EngineSettings.json` beside
the application, then checks `Tiny3D\EngineSettings.json` in the current
directory, and otherwise uses validated defaults. Values are validated when
loaded and when passed to the `Game` constructor. `EngineSettings` allows
`FontResolution` values from 8 through 24, while the current `EngineUi`
constructor limits a running `Game` to 8 through 18.

The current renderer accepts colored triangle geometry and does not implement a
general material or texture API. The built-in host uses the Windows API and
Direct3D 11; there is no cross-platform window backend. Audio and physics are
also outside the current framework.
