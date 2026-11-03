# TODO & Changelog
> Legend: Entries marked with ✔ indicate that they are fully implemented. ⚠ indicates the feature is unstable/postponed \
  Logs that have uns in their ID mean they are not fully finalized and are definitively subjects to change.

  ## iteration_uns0.8
  - Rebrand to Nodachi Engine. Sounds cool.
  - Image API implementation across both core engine program and engine-generated environment:
    - New Data Type: `ImageData`, `Image`
      - ImageData represents raw RGBA data of an image.
      - Image is well.. an image.
    - New Image API:
      - `Image.BuildImage(ImageData image)`, `Image.TransformImage(Image image, Color32[,] transform)`
    - New `Decal` Element - A flat 2D image projected onto a surface. Must be parented to a mesh or a construct containing mesh(es) to render.
      - Properties: `bool ContourToMeshSurface`, `Vector2 ImageSceneScale`, `int Opacity`, `Vector3 Position`, `Vector3 Rotation`, `bool IgnoreLighting`, `Vector2 ImageOffset`, `Entity Projector`, `Image ImageTexture`
        - `Projector` - Refers to the entity (Mesh) which bears the decal. Becomes readonly and automatically sets to its parent Mesh, if any. Otherwise it can be set to a mesh under the same construct as the decal.
  - Revisit the core engine UI. New styling and added icons for different components.

  ## iteration_uns0.7
  - Establish test-runner support before adding scheduler coverage:
    - Move the console-based checks to a test framework with `dotnet test`, test discovery, and filtering.
    - Acceptance: the existing checks run and pass through the standard test command.
  - Characterize and then unify update scheduling:
    - First add tests that pin the current windowed and headless callback order/cadence.
    - Adopt variable-step `Game.Update(deltaSeconds)` once per simulation frame and fixed-step `Construct.FixedUpdate()` at 1/60 second; document this as a breaking behavior change for existing `Game.Update` overrides.
    - Use one shared simulation step and accumulator in both modes. Clamp incoming frame time to 0.25 seconds (at most 15 fixed ticks per step); headless simulation feeds its explicit `deltaSeconds` through that same step.
    - Keep host-specific input and rendering/presentation outside the shared simulation scheduling.
    - Acceptance: characterization tests record the old behavior; updated tests assert the new order, delta values, tick counts, and matching windowed/headless simulation semantics.
  - Profile the current Direct3D CPU geometry pipeline and decide whether a renderer redesign is warranted:
    - Measure scenes of 100, 1,000, and 10,000 cube entities (12 triangles each), recording geometry-preparation time and total frame time with `RuntimeProfiler`.
    - Use geometry preparation exceeding 30% of a 16.6 ms frame at 1,000 entities as the trigger to schedule a GPU-side transform/render rewrite; record results and decision so the profiling task has a clear exit.
    - Decide the vertex format and where transforms/projection occur using both profiling and planned texture/shadow/depth requirements. Check clip-space `w` and perspective-correct interpolation before choosing a pre-projected-vertex or GPU-transform design.
    - Treat `MemoryAllocation` as a safety cap, not by itself as evidence for a redesign. Defer any triggered renderer rewrite to a separately scoped iteration.
    - Acceptance: benchmark results, vertex/transform decision, and follow-up decision are recorded; no rewrite is undertaken without the stated trigger and design review.
  - Define image, texture, and decal data/API contracts after the vertex-format decision:
    - Specify image sampling, alpha/blending, and ownership requirements; support UVs for ordinary surface texturing and keep projected decals independent of mesh UVs where practical.
    - Acceptance: documented data/API contracts and mesh/material requirements are settled before the image and decal implementation planned for 0.8.
  - Add configurable vertical synchronization with a defined precedence over `FramerateCap`:
    - Vsync on: ignore the FPS cap. Vsync off with a cap of 0: do not limit frames. Vsync off with a positive cap: apply the cap.
    - Warn through the runtime log when vsync and a positive frame cap are both configured. Document the precedence table in the README.
    - Acceptance: all three combinations have coverage, and the warning and README table match the behavior.
  - Replace or supplement `SSAAStrength` with an explicit MSAA sample-count enum (1x/2x/4x/8x):
    - Keep `SSAAStrength` as a compatibility alias that maps to the enum.
    - Query Direct3D support for the active render-target format; fall back to the highest supported lower sample count (ultimately 1x) and log when the requested count is unavailable.
    - Acceptance: settings and alias mappings are tested, and unsupported-device fallback is logged and tested.
  - Choose one supported `FontResolution` range and validate it at the source of truth; have `EngineUi` use the shared validator/limits.
    - Acceptance: settings and editor accept/reject the same boundary values.
  - Make hidden-node behavior easy to find in the README: visibility hides rendering, not update callbacks.
    - Acceptance: README states the behavior consistently with a test or existing verified behavior.
  - Document the coordinate basis, cross-product convention, and triangle winding; add a test for a known triangle facing the camera. Consider quaternion-backed rotation only if arbitrary 3D rotation or interpolation becomes a goal.
    - Acceptance: documentation convention agrees with the winding test.
  - Confirm and document settings lookup: application output directory first, then the more fragile working-directory fallback, using platform-independent path joining.
    - Acceptance: lookup order and fallback limitation are documented and covered by tests.
  - Keep these as optional follow-ups, not prerequisites for 0.7:
    - Introduce a renderer interface only if runtime-swappable backends become a goal.
    - Restrict Windows-specific projects to a Windows target framework only if that tradeoff is acceptable for headless/software-rendering use.
  - Scope: 0.7 delivers the scheduling change, profiling/design decision, contracts, and focused settings/documentation improvements. A renderer rewrite is a separate iteration if profiling meets the trigger. The Nodachi rebrand and core UI styling/icons remain planned for 0.8.
    - Acceptance: 0.7 items are independently testable and the renderer implementation is not an implicit release blocker.


  ## iteration_0.6
  - Polish and finalize Dither Fade effect. ✔
  - Polish and finalize Lens Flare effect. ✔
  - Increase protection for core features such as Lighting and camera elements inside the explorer: Make Name readonly, while keeping Entity/UI names editable. ✔
  - Redefine `Constructs`: Add an empty, transformless construct as a standalone explorer node, then add entities and UI elements as its children. ✔
  - Add Anti-Aliasing for both core engine UI and engine generated UI. Include an Anti-Aliasing option in `EngineSettings`. ✔
  - Add `default-template-settings` JSON file for reference when creating a new template. ✔
  - Finalize UI API:
    - New properties:
      - Shared bewtween all UI elements:
        - `float SSAAStrength` (Anti-Aliasing 0-1), `int Rotation` ✔
      - Exclusive properties of `TextBox2D`
        - `bool TextFitsBoundary`, `int TextSize`, `bool TextWrapping`, `enum TextAlignmentX`, `enum TextAlignmentY` ✔
    - Integrate a 2D gizmo following the same principle behind the 3D one for convenient quick adjustments of UI elements. Restrict this gizmo to only show while a UI element has been selected via the explorer. ✔
  - Make core engine UI adjustable in size by dragging the edges. ✔
  - Make core engine UI text fit instead of cutting out. ✔
  - Add the option to scroll around a text field with left and right arrow or clicking on a position in the text field. ✔
  
  ## Bugs and Issues:
  - Disabling `CelestialBodiesShown` also hides the lens flare effect. ✔
  - Unintended objects such as `Shape2D`, `TextBox2D`, `Lighting` have a Pivot property. [Low priority] ✔
  - Moving the camera while pressing down on a gizmo will leave behind the subject. ✔
  - You can move the camera while editing a property. ✔
  - ColorWheel rendering issues. (See `References/ColorWheelIssue.png`) ⚠

  ## iteration_uns0.5
  - Add a root solution file to the project. ✔
  - New Data Types: `Vector2` and `UIVector2`. ✔
    - `UIVector2` combines a screen relative scale and absolute pixel scale with `Vector2 RelativeScale`, `Vector2 AbsoluteOffset`.
    - `Vector2 is a 2D equivalent of Vector3`
  - Implement a UI API and subsequent UI elements: `Shape2D`, `TextBox2D` ✔
    - Shared properties between each UI element:
      - `UIVector2 Scale`, `UIVector2 Position`, `int Opacity`, `UIVector2 CornerRadius`, `Color32 Color`, `int OutlineThickness`, `Color32 OutlineColor`, `UIVector2 AnchorPoint`, `event OnButtonDown<Key>`, `event OnButtonUp<Key>`, `OnElementMouseHover`, `OnElementMouseExit`, `bool Rendered`, `int Layer`, `bool ClipDescendants`
    - Exclusive properties of `TextBox2D`
      - `string TextContent`, `int TextOpacity`, `int TextGlyphSpacing`, `int TextLineSpacing`, `int MaxGlyphsVisible`, `Color32 TextColor`
  - Add an in-engine option to insert new constructs by right clicking the explorer. ✔

  ## iteration_0.4
  - Review issue from `iteration_uns0.2` [2] ✔ (Dither fade is applied by both render paths)
  - Add an in-engine color wheel for properties that use `Color32` ✔
  - Add celestial bodies (Sun) to the sky ✔
    - The Sun should feature a lens flare effect ✔
    - New Lighting properties: ✔
      - `CelestialBodiesShown`
      - `RenderLensFlare`
      - `LensFlareStrength`
      - `LensFlareSpread` 
  - Make Constructs able to be duplicated with the shortcut `CTRL + D`, deleted with `Backspace`, copied with `CTRL + C`, pasted with `CTRL + V` and cut with `CTRL + X`. Make exceptions for constructs such as APIs and the Camera. ✔
  
  ### Bugs and Issues:
  - The framerateCap inside EngineSettings does not work. ✔
  - The bloom property inside the Lighting API does not work. ✔

  ## iteration_uns0.3
  - Review issue from `iteration_uns0.2` [1] ⚠ (the workspace has no issue description or reproduction steps)
  - Annotate core engine APIs with XML documentation comments ✔
  - Simplify feature usage and document the engine feature set in `README` ✔
  - Add draggable position/scale/rotation gizmos to selected constructs; switch modes with 1/2/3 ✔
  - Add Ctrl + A text selection and repeat deletion while holding Backspace ✔
  
  ### Bugs & Issues:
  - Selecting the "Filter Constructs" box inside the Scene Explorer permanently marks it as selected. Causing it to type on every key press, even if not intended. ✔

  ## iteration_uns0.2
  - Optimize `Direct3D11Renderer` selection/gizmo overlay allocations ✔
  - Replace Mesh's transform with Pivot ✔
  - Update Camera to be built within `src` instead of as core engine code. ⚠ [1]
  
  ### Bugs & Issues:
  - Shadows cause extreme performance impact ✔
  - Opening UIs (Explorer, Inspector, etc) causes substantial performance impact ✔
  - Dither Fade does not work [Low Priority] ✔ [2]

  ## iteration_uns0.1

  - Move away from the pixelated look of the engine. Make everything look sharper. ✔
  - Expand the explorer to show engine APIs and services too. ✔
  - Add a new Lighting API that will handle scene lighting. ✔
    * Properties:
      * `SkyboxTint`
      * `SkyboxHaze`
      * `RenderShadows`
      * `RenderBloom`
      * `BloomStrength`
      * `BloomThreshold`
      * `AmbientLighting`
      * `DirectionalLightVector3`
  - Built in Dither Fade into the engine. Can be enabled/disabled from EngineSettings ✔


  ### Bugs & Issues
  - UI tends to scale slightly off. Some text breaches its bounding space. ✔
  - Moving the camera into *some* meshes- In the case of recent testing, the baseplate mesh. \
    Some of the mesh's faces would disappear and flicker. ✔
