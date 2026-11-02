# TODO & Changelog
> Legend: Entries marked with ✔ indicate that they are fully implemented. ⚠ indicates the feature is unstable/postponed \
  Logs that have uns in their ID mean they are not fully finalized and are definitively subjects to change.
  ## iteration_uns0.7
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


  ## iteration_0.6
  - Polish and finalize Dither Fade effect. ✔
  - Polish and finalize Lens Flare effect. ✔
  - Increase protection for core features such as Lighting and camera elements inside the explorer: Make Name readonly, while keeping Entity/UI names editable. ✔
  - Redifine `Constructs`: Add an empty, transformless construct as a standalone explorer node, then add entities and UI elements as its children. ✔
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
  - You can move the camera while editing a propery. ✔
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
