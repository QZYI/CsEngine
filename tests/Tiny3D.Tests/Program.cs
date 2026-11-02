using System.Reflection;
using Tiny3D;
using Tiny3D.Graphics;
using Tiny3D.Input;
using Tiny3D.Math;
using Tiny3D.Scene;
using Tiny3D.Tools;
using Tiny3D.UI;

Assert(Vector3.Cross(new Vector3(1, 0, 0), new Vector3(0, 1, 0)).Z == 1, "Vector cross product");
Assert(new Vector3(3, 0, 0).Normalized().X == 1, "Vector normalization");
Assert(new Vector2(3, 4).Length == 5 &&
    Vector2.Dot(new Vector2(1, 2), new Vector2(3, 4)) == 11 &&
    new Vector2(0, 5).Normalized().Y == 1,
    "Vector2 length, dot product, and normalization");
Assert(new UIVector2(new Vector2(0.5f, 0.25f), new Vector2(4, 8))
        .Resolve(200, 80).Equals(new Vector2(104, 28)),
    "UIVector2 combines relative and absolute coordinates");
var uiValueParser = typeof(EngineUi).GetMethod("ParseValue", BindingFlags.NonPublic | BindingFlags.Static)!;
Assert((UIVector2)uiValueParser.Invoke(null, [typeof(UIVector2), "0.5, 0.25, 4, 8"])! ==
        new UIVector2(new Vector2(0.5f, 0.25f), new Vector2(4, 8)),
    "UIVector2 inspector editing parses relative and absolute components");
AssertThrows<ArgumentOutOfRangeException>(() => new Shape2D { Opacity = 256 },
    "UI opacity is restricted to byte range");
AssertThrows<ArgumentOutOfRangeException>(() => new Shape2D { SSAAStrength = 1.1f },
    "UI anti-aliasing strength is restricted to the 0-1 range");
var suppressionInput = new InputState();
suppressionInput.SetKeyDown((int)Key.W);
suppressionInput.ConsumeCameraInput();
Assert(!suppressionInput.IsKeyDown(Key.W),
    "Camera movement keys are suppressed while editor input is active");
suppressionInput.SetKeyUp((int)Key.W);
suppressionInput.ConsumeCameraInput();
suppressionInput.SetKeyDown((int)Key.W);
Assert(suppressionInput.IsKeyDown(Key.W),
    "A new camera movement key press is not suppressed after editor interaction ends");
suppressionInput.SetKeyUp((int)Key.W);

var uiScene = new Scene();
var roundedShape = new Shape2D
{
    Name = "Rounded",
    Position = UIVector2.FromPixels(20, 20),
    Scale = UIVector2.FromPixels(30, 20),
    CornerRadius = UIVector2.FromPixels(6, 6),
    Color = new Color32(220, 30, 40),
    OutlineThickness = 2,
    OutlineColor = new Color32(10, 20, 30)
};
uiScene.UI.Add(roundedShape);
Assert(roundedShape.SSAAStrength == 1f,
    "UI elements default to maximum antialiasing strength");
var clippedChild = new Shape2D
{
    Name = "Clipped child",
    Position = UIVector2.FromPixels(25, 0),
    Scale = UIVector2.FromPixels(15, 20),
    Color = new Color32(20, 220, 40),
    Layer = 1
};
roundedShape.ClipDescendants = true;
roundedShape.AddChild(clippedChild);
var uiRenderer = new SoftwareRenderer(80, 60);
var uiPixels = uiRenderer.Render(uiScene, new Camera());
roundedShape.SSAAStrength = 0.25f;
var lowerAaPixels = (int[])uiRenderer.Render(uiScene, new Camera()).Clone();
roundedShape.SSAAStrength = 1f;
var higherAaPixels = uiRenderer.Render(uiScene, new Camera());
Assert(!lowerAaPixels.SequenceEqual(higherAaPixels),
    "Higher UI antialiasing strength increases rounded-edge sampling");
Assert(uiPixels[20 * 80 + 20] == uiScene.Lighting.SkyboxTint.ToPixel(),
    "Rounded UI corners leave pixels outside the rounded shape unchanged");
Assert(uiPixels[30 * 80 + 49] == clippedChild.Color.ToPixel() &&
    uiPixels[30 * 80 + 50] != clippedChild.Color.ToPixel(),
    "UI descendants are clipped by their parent's bounds");

var textScene = new Scene();
var textBox = new TextBox2D
{
    Name = "Text",
    Position = UIVector2.FromPixels(5, 5),
    Scale = UIVector2.FromPixels(40, 24),
    Color = new Color32(0, 0, 0, 0),
    TextContent = "AB",
    TextColor = new Color32(255, 255, 255),
    MaxGlyphsVisible = 1
};
textScene.UI.Add(textBox);
var uiTextRenderer = new SoftwareRenderer(64, 40);
var uiTextPixels = uiTextRenderer.Render(textScene, new Camera());
Assert(uiTextPixels.Skip(5 * 64 + 5).Take(24 * 64)
        .Contains(new Color32(255, 255, 255).ToPixel()),
    "TextBox2D renders visible glyphs");
var oneGlyphPixels = (int[])uiTextPixels.Clone();
textBox.MaxGlyphsVisible = 0;
var noGlyphPixels = (int[])uiTextRenderer.Render(textScene, new Camera()).Clone();
Assert(!oneGlyphPixels.SequenceEqual(noGlyphPixels),
    "TextBox2D MaxGlyphsVisible limits rendered glyphs");
textBox.MaxGlyphsVisible = -1;
textBox.TextContent = "A B A B";
textBox.TextSize = 14;
textBox.TextWrapping = true;
textBox.TextFitsBoundary = true;
textBox.TextAlignmentX = TextAlignmentX.Center;
textBox.TextAlignmentY = TextAlignmentY.Center;
textBox.Rotation = 90;
var rotatedTextPixels = uiTextRenderer.Render(textScene, new Camera());
Assert(!noGlyphPixels.SequenceEqual(rotatedTextPixels),
    "TextBox2D text layout options and rotation affect rendered output");
var rotatedTextHoverCount = 0;
textBox.OnElementMouseHover += () => rotatedTextHoverCount++;
var rotatedTextInput = new InputState();
rotatedTextInput.SetMousePosition(8, 17);
textScene.UI.Update(rotatedTextInput, 64, 40);
Assert(rotatedTextHoverCount == 0,
    "Rotated UI elements use rotated bounds for pointer hit testing");
textBox.Rotation = 0;
textScene.UI.Update(rotatedTextInput, 64, 40);
Assert(rotatedTextHoverCount == 1, "Unrotated UI bounds remain interactive");

var eventScene = new Scene();
var eventElement = new Shape2D
{
    Position = UIVector2.FromPixels(10, 10),
    Scale = UIVector2.FromPixels(20, 20)
};
eventScene.UI.Add(eventElement);
var hoverCount = 0;
var exitCount = 0;
var keyDownCount = 0;
var keyUpCount = 0;
eventElement.OnElementMouseHover += () => hoverCount++;
eventElement.OnElementMouseExit += () => exitCount++;
eventElement.OnButtonDown += key => keyDownCount += key == Key.A ? 1 : 0;
eventElement.OnButtonUp += key => keyUpCount += key == Key.A ? 1 : 0;
var eventInput = new InputState();
eventInput.SetMousePosition(15, 15);
eventInput.SetKeyDown((int)Key.A);
eventScene.UI.Update(eventInput, 100, 100);
eventInput.SetMousePosition(50, 50);
eventInput.SetKeyUp((int)Key.A);
eventScene.UI.Update(eventInput, 100, 100);
Assert(hoverCount == 1 && exitCount == 1 && keyDownCount == 1 && keyUpCount == 1,
    "UI key release returns to its original element after pointer exit");

var explorerScene = new Scene();
var explorerEntity = new Entity(Mesh.CreateCube()) { Name = "Explorer target" };
explorerScene.Add(explorerEntity);
var explorerInput = new InputState();
var explorerRenderer = new SoftwareRenderer(640, 480);
var explorerHistory = new UndoHistory();
var explorerUi = new EngineUi(
    explorerScene, new RuntimeProfiler(), new EngineLog(), new DebugConsole(), new DebugDraw(),
    explorerHistory, 640, 480);
explorerUi.Render(new int[640 * 480]);
explorerInput.SetMousePosition(20, 80);
explorerInput.SetRightMouseButtonDown(true);
explorerUi.Update(explorerInput, explorerRenderer, new Camera());
explorerInput.CompleteUiUpdate();
explorerUi.Render(new int[640 * 480]);
explorerInput.SetMousePosition(30, 104);
explorerInput.SetLeftMouseButtonDown(true);
explorerUi.Update(explorerInput, explorerRenderer, new Camera());
Assert(explorerEntity.Descendants.SingleOrDefault() is Shape2D &&
    explorerUi.Explorer.Selected is Shape2D && explorerHistory.CanUndo,
    "Explorer context menu inserts and selects a Shape2D as an undoable child");
explorerInput.SetLeftMouseButtonDown(false);
explorerUi.Update(explorerInput, explorerRenderer, new Camera());
explorerInput.CompleteUiUpdate();
var emptyConstruct = new EmptyConstruct { Name = "Components" };
explorerScene.Add(emptyConstruct);
var nestedEntity = new Entity(Mesh.CreateCube());
var nestedUi = new Shape2D();
emptyConstruct.AddDescendant(nestedEntity);
emptyConstruct.AddDescendant(nestedUi);
Assert(explorerScene.Entities.Contains(nestedEntity) &&
    nestedUi.OwningScene == explorerScene &&
    explorerUi.Explorer.Search("Components").Contains(emptyConstruct),
    "Empty constructs act as explorer containers for entity and UI components");
var transformNode = new ReadOnlyConstruct();
transformNode.Pivot.Position = new Vector3(2, -1, 3);
transformNode.Pivot.Rotation = new Vector3(0.3f, -0.5f, 0.2f);
transformNode.Pivot.Scale = new Vector3(2, 0.5f, 1.5f);
var localPoint = new Vector3(-1, 2, 0.5f);
var worldPoint = transformNode.TransformPoint(localPoint);
Assert(transformNode.TryInverseTransformPoint(worldPoint, out var roundTripPoint) &&
    (roundTripPoint - localPoint).Length < 0.001f,
    "Inverse construct transform correctly undoes translation, rotation, and scale");
var defaultLighting = new Lighting();
Assert(defaultLighting.SkyboxTint.B > defaultLighting.SkyboxTint.R &&
    defaultLighting.CalculateIntensity(Vector3.Up) > defaultLighting.AmbientLighting,
    "Default lighting uses a blue sky and illuminates upward-facing surfaces");
Assert(!defaultLighting.RenderBloom,
    "Bloom is opt-in to avoid an unnecessary full-screen post-process pass by default");
Assert(defaultLighting.CelestialBodiesShown && defaultLighting.RenderLensFlare &&
    defaultLighting.LensFlareStrength is >= 0 and <= 1 &&
    defaultLighting.LensFlareSpread is >= 0 and <= 1,
    "Celestial body and lens flare settings have valid defaults");
AssertThrows<NotSupportedException>(() => defaultLighting.DuplicateTree(),
    "Lighting service cannot be duplicated");

var duplicateParent = new Entity(Mesh.CreateCube())
{
    Name = "Duplicate Parent",
    Color = new Color32(10, 20, 30, 40)
};
duplicateParent.Pivot.Position = new Vector3(1, 2, 3);
var duplicateChild = new Entity(Mesh.CreateCube(2)) { Name = "Duplicate Child" };
duplicateParent.AddDescendant(duplicateChild);
var duplicateTree = (Entity)duplicateParent.DuplicateTree();
Assert(duplicateTree.Name == duplicateParent.Name &&
    duplicateTree.Color == duplicateParent.Color &&
    duplicateTree.Pivot.Position.Equals(duplicateParent.Pivot.Position) &&
    duplicateTree.Descendants.Count == 1 &&
    duplicateTree.Descendants[0].Name == duplicateChild.Name &&
    duplicateTree.Descendants[0] is Entity { Mesh: var duplicateMesh } &&
    ReferenceEquals(duplicateMesh, duplicateChild.Mesh),
    "Construct duplication preserves entity properties, transforms, and descendants");
duplicateTree.Dispose();

var scene = new Scene();
scene.Add(new Entity(Mesh.CreateCube()));
var renderer = new SoftwareRenderer(96, 96);
var pixels = renderer.Render(scene, new Camera());
Assert(pixels.Distinct().Count() > 1, "Cube rasterization");
Assert(renderer.TryPick(48, 48, out var pickedEntity) && ReferenceEquals(pickedEntity, scene.Entities[0]),
    "Software-rendered viewport picking");
pixels = renderer.Render(scene, new Camera(), selected: scene.Entities[0]);
Assert(pixels.Contains(unchecked((int)0xFFFFCA5C)), "Selected construct outline rendering");
Assert(pixels.Contains(unchecked((int)0xFFFFE8A0)), "Selected construct pivot marker rendering");
Assert(pixels[48 * renderer.Width + 48] != unchecked((int)0xFFFFCA5C),
    "Selection highlight follows the mesh silhouette instead of its screen bounds");
var celestialScene = new Scene();
celestialScene.Lighting.DirectionalLightVector3 = new Vector3(0, 0, -1);
celestialScene.Lighting.RenderLensFlare = false;
var celestialRenderer = new SoftwareRenderer(96, 96);
var celestialPixels = celestialRenderer.Render(celestialScene, new Camera());
Assert(celestialPixels[48 * 96 + 48] == unchecked((int)0xFFFFF4D0),
    "Celestial body renders at the projected directional-light position");
celestialScene.Lighting.CelestialBodiesShown = false;
celestialPixels = celestialRenderer.Render(celestialScene, new Camera());
Assert(celestialPixels[48 * 96 + 48] == celestialScene.Lighting.SkyboxTint.ToPixel(),
    "Celestial body visibility setting hides the Sun");
celestialScene.Lighting.CelestialBodiesShown = true;
celestialScene.Lighting.LensFlareStrength = 1;
celestialScene.Lighting.RenderLensFlare = false;
var noFlarePixels = (int[])celestialRenderer.Render(celestialScene, new Camera()).Clone();
celestialScene.Lighting.RenderLensFlare = true;
var flarePixels = celestialRenderer.Render(celestialScene, new Camera());
Assert(flarePixels.Where((pixel, index) =>
        pixel != noFlarePixels[index] &&
        System.Math.Abs(index % 96 - 48) > 10)
    .Any(),
    "Lens flare strength produces visible screen-space flare beyond the Sun disc");
var direct3DRendererType = typeof(SoftwareRenderer).Assembly
    .GetType("Tiny3D.Graphics.Direct3D11Renderer")!;
var shaderSource = (string)direct3DRendererType
    .GetField("ShaderSource", BindingFlags.Static | BindingFlags.NonPublic)!
    .GetRawConstantValue()!;
var compileShader = direct3DRendererType
    .GetMethod("CompileShader", BindingFlags.Static | BindingFlags.NonPublic)!;
var postProcessBytecode = compileShader.Invoke(null, [shaderSource, "PSPostProcess", "ps_4_0"]);
Assert(postProcessBytecode is ReadOnlyMemory<byte> { Length: > 0 },
    "Direct3D post-process shader compiles with bloom and lens flare");
var vertexBytecode = compileShader.Invoke(null, [shaderSource, "VSMain", "vs_4_0"]);
Assert(vertexBytecode is ReadOnlyMemory<byte> { Length: > 0 },
    "Direct3D scene vertex shader compiles with bloom background data");
var litBytecode = compileShader.Invoke(null, [shaderSource, "PSLitMain", "ps_4_0"]);
Assert(litBytecode is ReadOnlyMemory<byte> { Length: > 0 },
    "Direct3D lit shader compiles with depth-based dither fade");
var vertexFloatCount = (int)direct3DRendererType
    .GetField("VertexFloatCount", BindingFlags.Static | BindingFlags.NonPublic)!
    .GetRawConstantValue()!;
var getVertexCount = direct3DRendererType
    .GetMethod("GetVertexCount", BindingFlags.Static | BindingFlags.NonPublic)!;
Assert((int)getVertexCount.Invoke(null, [vertexFloatCount * 3])! == 3 &&
    (int)getVertexCount.Invoke(null, [vertexFloatCount * 3 + vertexFloatCount - 1])! == 3,
    "Direct3D draw count uses the actual vertex layout stride");
var ditherScene = new Scene();
ditherScene.Lighting.AmbientLighting = 1;
ditherScene.Add(new Entity(new Mesh(
    [new Vector3(-0.1f, -0.1f, -4.5f), new Vector3(0.1f, -0.1f, -4.5f), new Vector3(0, 0.1f, -4.5f)],
    [0, 1, 2])) { Color = new Color32(40, 220, 80) });
ditherScene.Add(new Entity(new Mesh(
    [new Vector3(1.3f, -0.5f, 0), new Vector3(2.3f, -0.5f, 0), new Vector3(1.8f, 0.5f, 0)],
    [0, 1, 2])) { Color = new Color32(220, 80, 40) });
var ditherRenderer = new SoftwareRenderer(96, 96);
var noDitherPixels = (int[])ditherRenderer.Render(ditherScene, new Camera()).Clone();
var ditherPixels = ditherRenderer.Render(ditherScene, new Camera(), ditherFade: 1);
Assert(ditherPixels.Where((pixel, index) => pixel != noDitherPixels[index]).Any(),
    "Near-camera mesh surfaces are dither-faded");
Assert(ditherPixels[48 * 96 + 80] == noDitherPixels[48 * 96 + 80],
    "Dither fade leaves distant mesh surfaces unchanged");
Assert(ditherPixels[0] == noDitherPixels[0],
    "Dither fade does not apply a screen-wide pattern to the background");
var positionGizmoPixels = (int[])pixels.Clone();
var scaleGizmoPixels = (int[])renderer.Render(scene, new Camera(), selected: scene.Entities[0],
    gizmoMode: TransformGizmoMode.Scale).Clone();
var rotationGizmoPixels = (int[])renderer.Render(scene, new Camera(), selected: scene.Entities[0],
    gizmoMode: TransformGizmoMode.Rotation).Clone();
Assert(!positionGizmoPixels.SequenceEqual(scaleGizmoPixels) &&
    !positionGizmoPixels.SequenceEqual(rotationGizmoPixels),
    "Position, scale, and rotation gizmos render distinct handles");

var gizmoScene = new Scene();
var gizmoEntity = new Entity(Mesh.CreateCube());
gizmoScene.Add(gizmoEntity);
var gizmoRenderer = new SoftwareRenderer(640, 480);
var gizmoCamera = new Camera();
gizmoRenderer.Render(gizmoScene, gizmoCamera);
var gizmoEditor = new EngineUi(gizmoScene, new RuntimeProfiler(), new EngineLog(),
    new DebugConsole(), new DebugDraw(), new UndoHistory(), 640, 480);
gizmoEditor.Explorer.Select(gizmoEntity);
var gizmoInput = new InputState();
gizmoInput.SetMousePosition(320, 220);
gizmoInput.SetLeftMouseButtonDown(true);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
gizmoInput.SetMousePosition(320, 200);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
gizmoInput.SetLeftMouseButtonDown(false);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
Assert(gizmoEntity.Pivot.Position.Y > 0.1f,
    "Dragging a position gizmo axis changes the selected construct pivot");
gizmoInput.SetKeyDown((int)Key.D2);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
gizmoInput.SetKeyUp((int)Key.D2);
gizmoInput.SetMousePosition(320, 200);
gizmoInput.SetLeftMouseButtonDown(true);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
gizmoInput.SetMousePosition(320, 180);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
gizmoInput.SetLeftMouseButtonDown(false);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
Assert(gizmoEntity.Pivot.Scale.Y > 1,
    "Dragging a scale gizmo axis changes the selected construct scale");
gizmoInput.SetKeyDown((int)Key.D3);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
gizmoInput.SetKeyUp((int)Key.D3);
gizmoInput.SetMousePosition(270, 220);
gizmoInput.SetLeftMouseButtonDown(true);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
gizmoInput.SetMousePosition(320, 170);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
gizmoInput.SetLeftMouseButtonDown(false);
gizmoEditor.Update(gizmoInput, gizmoRenderer, gizmoCamera);
gizmoInput.CompleteUiUpdate();
Assert(gizmoEntity.Pivot.Rotation.Length > 0.1f,
    "Dragging a rotation gizmo ring changes the selected construct rotation");

var uiGizmoScene = new Scene();
var uiGizmoShape = new Shape2D
{
    Name = "UI gizmo target",
    Position = UIVector2.FromPixels(270, 90),
    Scale = UIVector2.FromPixels(40, 40)
};
uiGizmoScene.UI.Add(uiGizmoShape);
var uiGizmoEditor = new EngineUi(uiGizmoScene, new RuntimeProfiler(), new EngineLog(),
    new DebugConsole(), new DebugDraw(), new UndoHistory(), 640, 480);
uiGizmoEditor.Explorer.Select(uiGizmoShape);
var uiGizmoInput = new InputState();
uiGizmoInput.SetMousePosition(290, 110);
uiGizmoInput.SetLeftMouseButtonDown(true);
uiGizmoEditor.Update(uiGizmoInput, new SoftwareRenderer(640, 480), new Camera());
uiGizmoInput.CompleteUiUpdate();
uiGizmoInput.SetMousePosition(300, 120);
uiGizmoEditor.Update(uiGizmoInput, new SoftwareRenderer(640, 480), new Camera());
uiGizmoInput.CompleteUiUpdate();
uiGizmoInput.SetLeftMouseButtonDown(false);
uiGizmoEditor.Update(uiGizmoInput, new SoftwareRenderer(640, 480), new Camera());
uiGizmoInput.CompleteUiUpdate();
Assert(uiGizmoShape.Position.AbsoluteOffset.Equals(new Vector2(280, 100)),
    "Dragging the selected UI element's position gizmo updates its screen position");

var resizeEditor = new EngineUi(new Scene(), new RuntimeProfiler(), new EngineLog(),
    new DebugConsole(), new DebugDraw(), new UndoHistory(), 640, 480);
var resizeRenderer = new SoftwareRenderer(640, 480);
var resizeInput = new InputState();
resizeInput.SetMousePosition(250, 140);
resizeInput.SetLeftMouseButtonDown(true);
resizeEditor.Update(resizeInput, resizeRenderer, new Camera());
resizeInput.CompleteUiUpdate();
resizeInput.SetMousePosition(300, 140);
resizeEditor.Update(resizeInput, resizeRenderer, new Camera());
resizeInput.CompleteUiUpdate();
resizeInput.SetLeftMouseButtonDown(false);
resizeEditor.Update(resizeInput, resizeRenderer, new Camera());
resizeInput.CompleteUiUpdate();
var explorerWidth = (int)typeof(EngineUi)
    .GetField("_explorerWidth", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(resizeEditor)!;
Assert(explorerWidth == 300, "Dragging the editor divider resizes the explorer panel");

var textEditor = new EngineUi(new Scene(), new RuntimeProfiler(), new EngineLog(),
    new DebugConsole(), new DebugDraw(), new UndoHistory(), 640, 480);
var textRenderer = new SoftwareRenderer(640, 480);
var textInput = new InputState();
textInput.SetMousePosition(30, 60);
textInput.SetLeftMouseButtonDown(true);
textEditor.Update(textInput, textRenderer, new Camera());
textInput.SetLeftMouseButtonDown(false);
textEditor.Update(textInput, textRenderer, new Camera());
foreach (var character in "Cube")
{
    textInput.AddTextInput(character);
}
textEditor.Update(textInput, textRenderer, new Camera());
textInput.CompleteUiUpdate();
textInput.SetKeyDown((int)Key.Control);
textInput.SetKeyDown((int)Key.A);
textEditor.Update(textInput, textRenderer, new Camera());
var selectionPixels = new int[640 * 480];
textEditor.Render(selectionPixels);
Assert(selectionPixels.Contains(unchecked((int)0xFF446074)),
    "Ctrl+A visibly selects all text in the focused field");
textInput.CompleteUiUpdate();
textInput.SetKeyUp((int)Key.A);
textInput.AddTextInput('X');
textEditor.Update(textInput, textRenderer, new Camera());
textEditor.Render(selectionPixels);
Assert(!selectionPixels.AsSpan(54 * 640, 20 * 640).Contains(unchecked((int)0xFF446074)),
    "Typing replaces the selected text in a focused field");
textInput.CompleteUiUpdate();
textInput.AddTextInput('Y');
textInput.AddTextInput('Z');
textEditor.Update(textInput, textRenderer, new Camera());
textInput.CompleteUiUpdate();
textInput.SetKeyDown((int)Key.Backspace);
textEditor.Update(textInput, textRenderer, new Camera());
textInput.CompleteUiUpdate();
Thread.Sleep(220);
textEditor.Update(textInput, textRenderer, new Camera());
textInput.CompleteUiUpdate();
textInput.SetKeyUp((int)Key.Backspace);
textEditor.Update(textInput, textRenderer, new Camera());
var searchText = (string)typeof(EngineUi)
    .GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(textEditor)!;
Assert(searchText == "X", "Holding Backspace repeatedly deletes characters from the focused field");
textInput.SetKeyDown((int)Key.Left);
textEditor.Update(textInput, textRenderer, new Camera());
textInput.CompleteUiUpdate();
textInput.SetKeyUp((int)Key.Left);
textInput.AddTextInput('A');
textEditor.Update(textInput, textRenderer, new Camera());
textInput.CompleteUiUpdate();
searchText = (string)typeof(EngineUi)
    .GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(textEditor)!;
Assert(searchText == "AX", "Left and right arrow keys move the text insertion caret");

var shortcutScene = new Scene();
var shortcutEntity = new Entity(Mesh.CreateCube()) { Name = "Shortcut Cube" };
shortcutScene.Add(shortcutEntity);
var shortcutCamera = new CameraConstruct(new Camera());
shortcutScene.Add(shortcutCamera);
AssertThrows<NotSupportedException>(() => shortcutCamera.DuplicateTree(),
    "Camera construct cannot be duplicated");
var shortcutUi = new EngineUi(shortcutScene, new RuntimeProfiler(), new EngineLog(),
    new DebugConsole(), new DebugDraw(), new UndoHistory(), 640, 480);
var shortcutRenderer = new SoftwareRenderer(640, 480);
var shortcutInput = new InputState();
void SendShortcut(Key key, bool control)
{
    if (control)
    {
        shortcutInput.SetKeyDown((int)Key.Control);
    }

    shortcutInput.SetKeyDown((int)key);
    shortcutUi.Update(shortcutInput, shortcutRenderer, new Camera());
    shortcutInput.EndUpdate();
    shortcutInput.SetKeyUp((int)key);
    if (control)
    {
        shortcutInput.SetKeyUp((int)Key.Control);
    }
}

void ClickUi(EngineUi ui, InputState input, SoftwareRenderer targetRenderer, int x, int y)
{
    input.SetMousePosition(x, y);
    input.SetLeftMouseButtonDown(true);
    ui.Update(input, targetRenderer, new Camera());
    input.CompleteUiUpdate();
    input.SetLeftMouseButtonDown(false);
    ui.Update(input, targetRenderer, new Camera());
    input.CompleteUiUpdate();
}

(int X, int Y, int Width, int Height) ReadPrivateBounds(EngineUi ui, string fieldName)
{
    var fieldValue = typeof(EngineUi)
        .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(ui)!;
    object boundsValue = fieldName == "_colorHits"
        ? fieldValue is System.Collections.IEnumerable hits
            ? hits.Cast<object>().First().GetType()
                .GetProperty("Bounds")!.GetValue(hits.Cast<object>().First())!
            : throw new InvalidOperationException("Color swatch list is not enumerable.")
        : fieldValue;
    var type = boundsValue.GetType();
    return (
        (int)type.GetProperty("X")!.GetValue(boundsValue)!,
        (int)type.GetProperty("Y")!.GetValue(boundsValue)!,
        (int)type.GetProperty("Width")!.GetValue(boundsValue)!,
        (int)type.GetProperty("Height")!.GetValue(boundsValue)!);
}

shortcutUi.Explorer.Select(shortcutEntity);
SendShortcut(Key.D, control: true);
Assert(shortcutScene.Roots.Count == 3 &&
    shortcutUi.Explorer.Selected is Entity { Name: "Shortcut Cube Copy" },
    "Ctrl+D duplicates and selects the scene construct");
SendShortcut(Key.C, control: true);
SendShortcut(Key.V, control: true);
Assert(shortcutScene.Roots.Count == 4 &&
    shortcutUi.Explorer.Selected is Entity { Name: "Shortcut Cube Copy Copy" },
    "Ctrl+C and Ctrl+V copy and paste a construct");
SendShortcut(Key.X, control: true);
Assert(shortcutScene.Roots.Count == 3 && shortcutUi.Explorer.Selected is null,
    "Ctrl+X cuts the selected construct");
SendShortcut(Key.V, control: true);
Assert(shortcutScene.Roots.Count == 4, "Ctrl+V pastes a cut construct");
SendShortcut(Key.Backspace, control: false);
Assert(shortcutScene.Roots.Count == 3 && shortcutUi.Explorer.Selected is null,
    "Backspace deletes the selected construct");
shortcutUi.Explorer.Select(shortcutCamera);
SendShortcut(Key.Backspace, control: false);
Assert(shortcutCamera.OwningScene == shortcutScene && !shortcutCamera.IsDisposed,
    "Backspace cannot delete the protected camera construct");
shortcutUi.Explorer.Select(shortcutScene.Lighting);
SendShortcut(Key.Backspace, control: false);
Assert(!shortcutScene.Lighting.IsDisposed,
    "Backspace cannot delete an engine API service");

var colorEntity = new Entity(Mesh.CreateCube()) { Color = new Color32(80, 120, 255, 77) };
var colorScene = new Scene();
colorScene.Add(colorEntity);
var colorUi = new EngineUi(colorScene, new RuntimeProfiler(), new EngineLog(),
    new DebugConsole(), new DebugDraw(), new UndoHistory(), 640, 480);
var colorRenderer = new SoftwareRenderer(640, 480);
var colorInput = new InputState();
colorUi.Explorer.Select(colorEntity);
colorUi.Render(new int[640 * 480]);
var colorSwatch = ReadPrivateBounds(colorUi, "_colorHits");
var colorSwatchX = colorSwatch.X + colorSwatch.Width / 2;
var colorSwatchY = colorSwatch.Y + colorSwatch.Height / 2;
ClickUi(colorUi, colorInput, colorRenderer, colorSwatchX, colorSwatchY);
var colorPicker = ReadPrivateBounds(colorUi, "_colorWheelBounds");
ClickUi(colorUi, colorInput, colorRenderer, colorPicker.X + 79, colorPicker.Y + 54);
Assert(colorEntity.Color.R > 200 && colorEntity.Color.G < 80 &&
    colorEntity.Color.A == 77,
    "Inspector color wheel changes hue and preserves alpha");

var overlayDraw = new DebugDraw();
overlayDraw.Line(new Vector3(-2, 2, 0), new Vector3(2, 2, 0), new Color32(255, 0, 0));
pixels = renderer.Render(scene, new Camera(), debugDraw: overlayDraw);
Assert(pixels.Contains(unchecked((int)0xFFFF0000)), "Projected debug-draw line");
renderer.SetSize(192, 96);
pixels = renderer.Render(scene, new Camera());
Assert(renderer.Width == 192 && renderer.Height == 96 && pixels.Length == 192 * 96,
    "Renderer resizes to the window dimensions");
Assert(renderer.TryPick(96, 48, out pickedEntity) && ReferenceEquals(pickedEntity, scene.Entities[0]),
    "Picking remains aligned after renderer resize");
var bloomScene = new Scene();
bloomScene.Lighting.AmbientLighting = 1;
bloomScene.Lighting.RenderBloom = false;
bloomScene.Lighting.BloomThreshold = 0.75f;
bloomScene.Lighting.BloomStrength = 1;
bloomScene.Add(new Entity(new Mesh(
    [new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(0, 1, 0)],
    [0, 1, 2])) { Color = new Color32(255, 255, 255) });
var bloomRenderer = new SoftwareRenderer(96, 96);
var noBloomPixels = (int[])bloomRenderer.Render(bloomScene, new Camera()).Clone();
bloomScene.Lighting.RenderBloom = true;
var bloomPixels = bloomRenderer.Render(bloomScene, new Camera());
Assert(bloomPixels.Where((pixel, index) => pixel != noBloomPixels[index]).Any(),
    "Bloom adds a thresholded glow around bright geometry");
Assert(bloomPixels[0] == noBloomPixels[0],
    "Bloom does not brighten the uniform sky background");
Assert(bloomPixels.Where((pixel, index) =>
        pixel != noBloomPixels[index] &&
        noBloomPixels[index] == bloomScene.Lighting.SkyboxTint.ToPixel())
    .Any(),
    "Bloom spreads a glow from bright geometry onto nearby sky pixels");
Assert(bloomPixels[48 * 96 + 64] != noBloomPixels[48 * 96 + 64],
    "Bloom spreads a visible halo several pixels beyond bright geometry");

var shadowScene = new Scene();
shadowScene.Lighting.DirectionalLightVector3 = new Vector3(0, -1, 0);
shadowScene.Lighting.AmbientLighting = 0.2f;
var shadowParent = new ReadOnlyConstruct();
var shadowCaster = new Entity(Mesh.CreateCube());
shadowCaster.Pivot.Position = new Vector3(0, 2, 1);
shadowParent.AddDescendant(shadowCaster);
shadowScene.Add(shadowParent);
shadowScene.Lighting.RenderShadows = false;
var litIntensity = shadowScene.Lighting.CalculateIntensity(
    Vector3.Up, new Vector3(0, 0, 1), shadowScene.Entities);
shadowScene.Lighting.RenderShadows = true;
var shadowedIntensity = shadowScene.Lighting.CalculateIntensity(
    Vector3.Up, new Vector3(0, 0, 1), shadowScene.Entities);
Assert(shadowedIntensity < litIntensity,
    "Shadow sampling uses the same flipped light-space Y axis as shadow rasterization");
shadowParent.Pivot.Position = new Vector3(100, 0, 0);
var movedCasterIntensity = shadowScene.Lighting.CalculateIntensity(
    Vector3.Up, new Vector3(0, 0, 1), shadowScene.Entities);
Assert(movedCasterIntensity > shadowedIntensity,
    "Shadow maps are refreshed when a construct pivot changes");

var clippedScene = new Scene();
clippedScene.Add(new Entity(new Mesh(
    [new Vector3(-1, -1, -1), new Vector3(1, -1, 1), new Vector3(0, 1, 1)],
    [0, 1, 2])));
var clippedRenderer = new SoftwareRenderer(96, 96);
var closeCamera = new Camera
{
    Position = Vector3.Zero,
    Target = new Vector3(0, 0, 1),
    FieldOfView = 90
};
clippedRenderer.Render(clippedScene, closeCamera);
Assert(clippedRenderer.LastDrawCallCount >= 2,
    "Triangles crossing the camera near plane are clipped rather than discarded");

var depthScene = new Scene();
var farCube = new Entity(Mesh.CreateCube()) { Color = new Color32(255, 0, 0) };
farCube.Pivot.Position = new Vector3(0, 0, 2);
var nearCube = new Entity(Mesh.CreateCube()) { Color = new Color32(0, 255, 0) };
nearCube.Pivot.Position = new Vector3(0, 0, -1);
depthScene.Add(farCube);
depthScene.Add(nearCube);
var depthRenderer = new SoftwareRenderer(96, 96);
depthRenderer.Render(depthScene, new Camera());
Assert(depthRenderer.TryPick(48, 48, out pickedEntity) && ReferenceEquals(pickedEntity, nearCube),
    "Reciprocal depth buffer keeps nearest construct pickable");

var lifecycle = new List<string>();
var parent = new TrackingConstruct("parent", lifecycle);
var later = new TrackingConstruct("later", lifecycle) { UpdatePriority = 5 };
var earlier = new TrackingConstruct("earlier", lifecycle) { UpdatePriority = -1 };
parent.AddDescendant(later);
parent.AddDescendant(earlier);
var tree = new Scene();
tree.Add(parent);
Assert(lifecycle.SequenceEqual([
    "enter:parent", "ready:parent",
    "enter:earlier", "ready:earlier",
    "enter:later", "ready:later"
]), "Depth-first construct lifecycle");

lifecycle.Clear();
tree.Update(0.25f);
Assert(lifecycle.SequenceEqual(["update:parent", "update:earlier", "update:later"]),
    "Deterministic priority update order");

earlier.UpdatePriority = 10;
lifecycle.Clear();
tree.Update(0.25f);
Assert(lifecycle.SequenceEqual(["update:parent", "update:later", "update:earlier"]),
    "Update order cache refreshes after priority changes");
earlier.UpdatePriority = -1;

var queued = new TrackingConstruct("queued", lifecycle) { QueueOnUpdate = true };
tree.Add(queued);
var directDispose = new TrackingConstruct("direct-dispose", lifecycle) { DisposeOnUpdate = true };
tree.Add(directDispose);
var survivor = new TrackingConstruct("survivor", lifecycle);
tree.Add(survivor);
tree.Update(0.1f);
Assert(queued.IsDisposed && !tree.Roots.Contains(queued), "Deferred QueueFree disposal");
Assert(directDispose.IsDisposed, "Dispose defers during scene iteration");
Assert(lifecycle.Contains("update:survivor"), "Updates continue after deferred destruction is queued");

var cube = new Entity(Mesh.CreateCube());
cube.Name = "Cube";
parent.AddDescendant(cube);
Assert(tree.Entities.Count == 1 && ReferenceEquals(cube.Branch, parent),
    "Mesh entities participate in the construct tree");

var history = new UndoHistory();
var panel = new PropertyPanel(history);
panel.SetValue(cube, "Pivot.Position", new Vector3(3, 0, 0));
Assert(cube.Pivot.Position.X == 3, "Inspectable property editing");
Assert(history.Undo() && cube.Pivot.Position.X == 0, "Inspectable property undo");
Assert(history.Redo() && cube.Pivot.Position.X == 3, "Inspectable property redo");

var settings = new SettingsConstruct();
var settingsProperties = InspectionRegistry.GetProperties(settings.GetType());
var amount = settingsProperties.Single(property => property.Name == nameof(SettingsConstruct.Amount));
amount.SetValue(settings, 8);
Assert(settings.Amount == 8, "Inspectable metadata discovery");
var rejectedRange = false;
try
{
    amount.SetValue(settings, 11);
}
catch (ArgumentOutOfRangeException)
{
    rejectedRange = true;
}
Assert(rejectedRange, "Inspectable range validation");
var readOnly = new ReadOnlyConstruct();
var readOnlyProperty = InspectionRegistry.GetProperties(readOnly.GetType())
    .Single(property => property.Name == nameof(ReadOnlyConstruct.Value));
Assert(readOnlyProperty.IsReadOnly, "Inspectable read-only property metadata");
var entityNameProperty = InspectionRegistry.GetProperties<Entity>()
    .Single(property => property.Name == nameof(Construct.Name));
var namePanel = new PropertyPanel(new UndoHistory());
var entityName = new Entity(Mesh.CreateCube());
var shapeName = new Shape2D();
namePanel.SetValue(entityName, nameof(Construct.Name), "Renamed Entity");
namePanel.SetValue(shapeName, nameof(Construct.Name), "Renamed UI");
var cameraNameProperty = namePanel.GetProperties(shortcutCamera)
    .Single(property => property.Name == nameof(Construct.Name));
var lightingNameProperty = namePanel.GetProperties(shortcutScene.Lighting)
    .Single(property => property.Name == nameof(Construct.Name));
Assert(!entityNameProperty.IsReadOnly && entityName.Name == "Renamed Entity" &&
    shapeName.Name == "Renamed UI" && cameraNameProperty.IsReadOnly && lightingNameProperty.IsReadOnly &&
    typeof(Construct).GetProperty(nameof(Entity.Pivot)) is null &&
    typeof(Shape2D).GetProperty(nameof(Entity.Pivot)) is null &&
    typeof(Lighting).GetProperty(nameof(Entity.Pivot)) is null,
    "Entity/UI names are editable while camera and lighting names remain protected");
InspectionRegistry.Register<SettingsConstruct>(
[
    InspectionRegistry.CreateProperty<SettingsConstruct, int>(
        nameof(SettingsConstruct.Amount), "Settings",
        static target => target.Amount,
        static (target, value) => target.Amount = value,
        minimum: 0,
        maximum: 10)
]);
InspectionRegistry.GetProperties<SettingsConstruct>().Single().SetValue(settings, 4);
Assert(settings.Amount == 4, "Typed inspectable accessor registration");

var metadata = new MetadataStacks();
var tags = metadata.CreateNewStack<string>("tags");
tags.QueryNewEntries = true;
tags.OnQuery = tag => tag.StartsWith("ok", StringComparison.Ordinal);
Assert(tags.Include("okay") && !tags.Include("blocked"), "Metadata include query");
metadata.FreezeStack("tags");
var frozenRejected = false;
try
{
    tags.Exclude("okay");
}
catch (InvalidOperationException)
{
    frozenRejected = true;
}
Assert(frozenRejected && tags.IsFrozen, "Frozen metadata stack");
var transient = metadata.CreateNewStack<int>("transient");
transient.Include(1);
metadata.ClearStack("transient");
Assert(transient.Values.Count == 0, "Metadata stack clearing");

var explorer = new ConstructExplorer(tree);
Assert(explorer.Search("cub").Contains(cube), "Construct explorer search");
explorer.Reparent(cube, null);
Assert(tree.Roots.Contains(cube), "Construct explorer reparenting to scene root");

var console = new DebugConsole();
console.SetVariable("fps_max", "60");
Assert(console.Execute("fps_max") == "fps_max = 60", "Console variable query");
Assert(console.Execute("fps_max 30") == "fps_max = 30", "Console variable update");
var commandValue = string.Empty;
console.RegisterCommand("echo", args => commandValue = string.Join(" ", args));
console.Execute("echo hello world");
Assert(commandValue == "hello world", "Console command dispatch");

var log = new EngineLog(capacity: 1);
log.Write(LogLevel.Information, "test", "first");
log.Write(LogLevel.Warning, "test", "second");
Assert(log.Entries.Count == 1 && log.Entries[0].Message == "second", "Bounded categorized logging");

var debugDraw = new DebugDraw();
debugDraw.Box(Vector3.Zero, Vector3.One, new Color32(255, 255, 255));
Assert(debugDraw.Lines.Count == 12, "Debug box line generation");

var camera = new Camera();
var cameraConstruct = new CameraConstruct(camera);
cameraConstruct.Position = new Vector3(1, 2, -5);
cameraConstruct.Rotate(new Vector3(0.1f, 0.2f, 0));
cameraConstruct.FieldOfView = 75;
cameraConstruct.SyncCamera();
Assert(camera.FieldOfView == 75 && camera.Position.X == 1 && camera.Position.Y == 2 &&
    camera.Target.Z != camera.Position.Z, "Camera construct synchronizes its pivot and projection");
cameraConstruct.LockedMovement = true;
var lockedPosition = cameraConstruct.Position;
var lockedRotation = cameraConstruct.Rotation;
cameraConstruct.Move(new Vector3(1, 1, 1));
cameraConstruct.Rotate(new Vector3(1, 1, 1));
Assert(cameraConstruct.Position.X == lockedPosition.X &&
    cameraConstruct.Rotation.X == lockedRotation.X,
    "Locked camera movement prevents pan and rotation");
cameraConstruct.LockedMovement = false;
var cameraBeforePan = cameraConstruct.Position;
cameraConstruct.Pan(new Vector3(1, -1, 0));
var cameraBeforeMouseLook = cameraConstruct.Rotation;
cameraConstruct.Rotate(new Vector3(0.1f, 0.2f, 0));
Assert(cameraConstruct.Position.X != cameraBeforePan.X &&
    cameraConstruct.Rotation.Y != cameraBeforeMouseLook.Y,
    "Camera supports view-relative panning and rotation");
var legacyCamera = new Camera();
var legacyCameraConstruct = new CameraConstruct(legacyCamera);
legacyCamera.Position = new Vector3(1, 0, -8);
legacyCameraConstruct.SyncCamera();
Assert(legacyCameraConstruct.Position.Z == -8,
    "Camera construct preserves direct camera pose changes");
var engineSettings = EngineSettings.Load(Path.Combine(AppContext.BaseDirectory, "EngineSettings.json"));
Assert(engineSettings.FontResolution is >= 8 and <= 18 &&
    engineSettings.MemoryAllocation == 67108864 &&
    engineSettings.FramerateCap == 60 &&
    engineSettings.SSAAStrength == 1f,
    "Engine settings JSON loads configured defaults, including anti-aliasing strength");
AssertThrows<ArgumentOutOfRangeException>(() => new EngineSettings { SSAAStrength = 1.1f },
    "Engine anti-aliasing strength is restricted to the 0-1 range");

var assetDirectory = Path.Combine(Path.GetTempPath(), $"tiny3d-assets-{Guid.NewGuid():N}");
Directory.CreateDirectory(assetDirectory);
try
{
    using var changedSignal = new ManualResetEventSlim();
    var reloadGame = new HeadlessGame();
    var reloadThread = 0;
    var mainThread = Environment.CurrentManagedThreadId;
    using var assetWatcher = reloadGame.WatchAssets(assetDirectory, _ =>
        reloadThread = Environment.CurrentManagedThreadId);
    assetWatcher.AssetChanged += _ => changedSignal.Set();
    File.WriteAllText(Path.Combine(assetDirectory, "reload.txt"), "changed");
    Assert(changedSignal.Wait(TimeSpan.FromSeconds(5)), "Asset watcher receives file changes");
    reloadGame.RunHeadless(1);
    Assert(reloadThread == mainThread, "Asset reload callback is dispatched on the game thread");
}
finally
{
    if (Directory.Exists(assetDirectory))
    {
        Directory.Delete(assetDirectory, recursive: true);
    }
}

var pool = new ConstructPool<TrackingConstruct>(() => new TrackingConstruct("pooled", lifecycle));
var pooled = pool.Rent();
tree.Add(pooled);
tree.Remove(pooled);
pool.Return(pooled);
Assert(ReferenceEquals(pool.Rent(), pooled), "Construct pool reuse");

var game = new HeadlessGame();
Assert(game.Width == 1920 && game.Height == 1080 &&
    ReferenceEquals(game.CameraNode.OwningScene, game.Scene) &&
    game.Console.Execute("fps_max") == "fps_max = 60" &&
    game.Settings.FontResolution == engineSettings.FontResolution,
    "Game installs camera construct and configured defaults");
var frameLimitSeconds = (double)typeof(Game)
    .GetMethod("GetFrameLimitSeconds", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(game, null)!;
Assert(Math.Abs(frameLimitSeconds - 1d / 60) < 0.000001,
    "Configured framerate cap produces the expected frame interval");
Assert(game.Editor.Explorer.Services.Contains(game.Lighting) &&
    game.Editor.Explorer.Search("Lighting").Contains(game.Lighting),
    "Lighting service is discoverable in the engine explorer");
Assert(game.Lighting.SkyboxTint.B > game.Lighting.SkyboxTint.R,
    "The default skybox tint is blue");
game.Editor.Explorer.Select(game.Lighting);
Assert(new PropertyPanel(game.UndoHistory).GetProperties(game.Lighting)
    .Any(property => property.Name == nameof(Lighting.DirectionalLightVector3)) &&
    new PropertyPanel(game.UndoHistory).GetProperties(game.Lighting)
        .Any(property => property.Name == nameof(Lighting.CelestialBodiesShown)),
    "Lighting and celestial body service properties are inspectable");
var uiCube = new Entity(Mesh.CreateCube()) { Name = "UI Cube" };
game.Scene.Add(uiCube);
game.Editor.Explorer.Select(uiCube);
game.RunHeadless(2);
Assert(game.UpdateCount == 2 && game.Profiler.Frames.Count == 2, "Headless game updates and profiling");
var overlay = new int[game.Width * game.Height];
game.Editor.Render(overlay);
Assert(overlay.Distinct().Count() > 2, "Engine UI overlay rendering");
var textPixels = Enumerable.Range(8, 20)
    .SelectMany(y => overlay.AsSpan(y * game.Width + 8, 12).ToArray()
        .Select((pixel, x) => (Y: y, X: x, Pixel: pixel)))
    .Where(pixel => pixel.Pixel != unchecked((int)0xFF191E23))
    .ToArray();
Assert(textPixels.Select(pixel => pixel.Pixel).Distinct().Count() > 2,
    "Engine UI text uses anti-aliased coverage");
Assert(textPixels.Max(pixel => pixel.Y) - textPixels.Min(pixel => pixel.Y) >= 8,
    "Engine UI glyphs are not vertically clipped");
game.Profiler.IsVisible = true;
game.Editor.Render(overlay);
Assert(overlay.Distinct().Count() > 2, "Runtime profiler overlay rendering");
game.Editor.SetSize(25, 100);
game.Editor.Render(new int[25 * 100]);

Console.WriteLine("All Tiny3D checks passed.");

static void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Failed: {name}");
    }
}

static void AssertThrows<TException>(Action action, string name)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Failed: {name}");
}

sealed class TrackingConstruct(string name, List<string> events) : Construct
{
    public bool QueueOnUpdate { get; set; }
    public bool DisposeOnUpdate { get; set; }

    protected override void OnEnterBranch() => events.Add($"enter:{name}");
    protected override void Ready() => events.Add($"ready:{name}");

    protected override void Update(float deltaSeconds)
    {
        events.Add($"update:{name}");
        if (QueueOnUpdate)
        {
            QueueFree();
        }
        if (DisposeOnUpdate)
        {
            Dispose();
        }
    }

    protected override void OnDispose() => events.Add($"dispose:{name}");
}

sealed class SettingsConstruct : Construct
{
    [Inspect("Settings")]
    [Range(0, 10)]
    public int Amount { get; set; }
}

sealed class ReadOnlyConstruct : TransformableConstruct
{
    [Inspect("Settings", ReadOnly = true)]
    public int Value => 1;
}

sealed class HeadlessGame : Game
{
    public int UpdateCount { get; private set; }

    protected override void Update(float deltaSeconds) => UpdateCount++;
}
