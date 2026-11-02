using System.Globalization;
using Tiny3D.Graphics;
using Tiny3D.Input;
using Tiny3D.Math;
using Tiny3D.Scene;
using Tiny3D.UI;
using SceneGraph = Tiny3D.Scene.Scene;

namespace Tiny3D.Tools;

/// <summary>Supported transform gizmo modes for scene selection and editing.</summary>
public enum TransformGizmoMode
{
    Position,
    Rotation,
    Scale
}

/// <summary>Built-in in-game editor UI for the scene tree, inspector, console, and runtime tools.</summary>
public sealed class EngineUi
{
    private const int CharacterAdvance = UiCanvas.CharacterAdvance;
    private const int ToolbarHeight = 26;
    private const int ExplorerWidth = 250;
    private const int PropertyWidth = 300;
    private const int RowHeight = 20;
    private readonly SceneGraph _scene;
    private readonly RuntimeProfiler _profiler;
    private readonly EngineLog _log;
    private readonly DebugConsole _console;
    private readonly DebugDraw _debugDraw;
    private readonly UndoHistory _history;
    private readonly PropertyPanel _propertyPanel;
    private readonly int _fontResolution;
    private readonly List<RowHit> _treeHits = [];
    private readonly List<RowHit> _propertyHits = [];
    private readonly List<ColorHit> _colorHits = [];
    private readonly List<ButtonHit> _buttonHits = [];
    private readonly List<ContextMenuHit> _contextMenuHits = [];
    private readonly List<(Construct Construct, int Depth)> _treeRows = [];
    private readonly FrameProfile[] _visibleFrames = new FrameProfile[366];
    private readonly Queue<string> _consoleOutput = new();
    private UiDrawList? _drawList;
    private int _width;
    private int _height;
    private int _explorerWidth = ExplorerWidth;
    private int _propertyWidth = PropertyWidth;
    private int _treeScrollOffset;
    private int _propertyScrollOffset;
    private string _search = string.Empty;
    private string _consoleInput = string.Empty;
    private string _editBuffer = string.Empty;
    private string? _editingProperty;
    private Construct? _editingTarget;
    private Construct? _constructClipboard;
    private Construct? _colorWheelTarget;
    private InspectableProperty? _colorWheelProperty;
    private Color32? _colorWheelPreview;
    private UiRect _colorWheelBounds;
    private float _colorWheelHue;
    private float _colorWheelSaturation;
    private float _colorWheelBrightness;
    private bool _colorWheelDragging;
    private Construct? _draggedConstruct;
    private GizmoDrag? _gizmoDrag;
    private UiResizeDrag? _uiResizeDrag;
    private UiTransformDrag? _uiTransformDrag;
    private int _textCursor;
    private int _textScrollOffset;
    private UiFocus _focus;
    private string _status = string.Empty;
    private DateTimeOffset _statusTime;
    private bool _contextMenuOpen;
    private int _contextMenuX;
    private int _contextMenuY;
    private Construct? _contextMenuParent;
    private DateTimeOffset? _backspaceRepeatAt;
    private bool _textSelectionActive;

    public EngineUi(
        SceneGraph scene,
        RuntimeProfiler profiler,
        EngineLog log,
        DebugConsole console,
        DebugDraw debugDraw,
        UndoHistory history,
        int width,
        int height,
        int fontResolution = 14)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _debugDraw = debugDraw ?? throw new ArgumentNullException(nameof(debugDraw));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        if (fontResolution is < 8 or > 18)
        {
            throw new ArgumentOutOfRangeException(nameof(fontResolution));
        }
        _fontResolution = fontResolution;
        _propertyPanel = new PropertyPanel(history);
        Explorer = new ConstructExplorer(scene);
        Explorer.AddService(scene.Lighting);
        SetSize(width, height);
    }

    public bool IsVisible { get; set; } = true;
    public bool ExplorerVisible { get; set; } = true;
    public bool PropertiesVisible { get; set; } = true;
    public bool ConsoleVisible { get; private set; }
    public bool LogVisible { get; private set; }
    public ConstructExplorer Explorer { get; }
    public TransformGizmoMode GizmoMode { get; private set; } = TransformGizmoMode.Position;
    public UndoHistory History => _history;

    public void SetSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "UI dimensions must be positive.");
        }

        _width = width;
        _height = height;
    }

    public void Update(InputState input, SoftwareRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        Update(input, (IScenePicker)renderer, renderer.LastCamera ?? new Camera());
    }

    public void Update(InputState input, SoftwareRenderer renderer, Camera camera) =>
        Update(input, (IScenePicker)renderer, camera);

    internal void Update(InputState input, IScenePicker renderer) =>
        Update(input, renderer, new Camera());

    internal void Update(InputState input, IScenePicker renderer, Camera camera)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(camera);

        if (input.WasKeyPressed(Key.F1))
        {
            _profiler.IsVisible = !_profiler.IsVisible;
            input.ConsumeKeyPress(Key.F1);
        }
        if (input.WasKeyPressed(Key.F2))
        {
            ConsoleVisible = !ConsoleVisible;
            _focus = ConsoleVisible ? UiFocus.Console : UiFocus.None;
            input.ConsumeKeyPress(Key.F2);
        }
        if (input.WasKeyPressed(Key.F3))
        {
            LogVisible = !LogVisible;
            input.ConsumeKeyPress(Key.F3);
        }
        if (input.WasKeyPressed(Key.F4))
        {
            IsVisible = !IsVisible;
            input.ConsumeKeyPress(Key.F4);
        }
        if (input.WasKeyPressed(Key.F5))
        {
            ExplorerVisible = !ExplorerVisible;
            input.ConsumeKeyPress(Key.F5);
        }
        if (input.WasKeyPressed(Key.D1))
        {
            GizmoMode = TransformGizmoMode.Position;
            input.ConsumeKeyPress(Key.D1);
        }
        if (input.WasKeyPressed(Key.D2))
        {
            GizmoMode = TransformGizmoMode.Scale;
            input.ConsumeKeyPress(Key.D2);
        }
        if (input.WasKeyPressed(Key.D3))
        {
            GizmoMode = TransformGizmoMode.Rotation;
            input.ConsumeKeyPress(Key.D3);
        }

        if (Explorer.Selected is { } selected && !Explorer.Contains(selected))
        {
            Explorer.Select(null);
        }
        if (Explorer.Hovered is { } hovered && !Explorer.Contains(hovered))
        {
            Explorer.SetHovered(null);
        }

        HandleTextInput(input);
        if (_focus != UiFocus.None || _colorWheelProperty is not null ||
            _gizmoDrag is not null || _uiTransformDrag is not null || _uiResizeDrag is not null)
        {
            input.ConsumeCameraInput();
        }
        UpdateColorWheel(input);
        HandleConstructShortcuts(input);
        if (_uiResizeDrag is { } resizeDrag)
        {
            if (input.IsLeftMouseButtonDown || input.WasLeftMouseButtonReleased)
            {
                UpdatePanelResize(resizeDrag, input);
            }
            if (input.WasLeftMouseButtonReleased)
            {
                _uiResizeDrag = null;
            }
            input.ConsumeCameraInput();
            return;
        }
        if (_uiTransformDrag is { } uiDrag)
        {
            if (input.IsLeftMouseButtonDown || input.WasLeftMouseButtonReleased)
            {
                UpdateUiTransformDrag(uiDrag, input);
            }
            if (input.WasLeftMouseButtonReleased)
            {
                _uiTransformDrag = null;
            }
            input.ConsumeCameraInput();
            return;
        }
        if (_gizmoDrag is { } drag)
        {
            if (input.IsLeftMouseButtonDown || input.WasLeftMouseButtonReleased)
            {
                UpdateGizmoDrag(drag, input, camera);
            }
            if (input.WasLeftMouseButtonReleased)
            {
                _gizmoDrag = null;
            }
            input.ConsumeCameraInput();
            return;
        }

        if (input.MouseWheelDelta != 0 && IsVisible)
        {
            if (ExplorerVisible && input.MouseX < System.Math.Min(_explorerWidth, _width / 2))
            {
                _treeScrollOffset = System.Math.Max(0, _treeScrollOffset - System.Math.Sign(input.MouseWheelDelta) * 3);
            }
            else if (PropertiesVisible && input.MouseX >= _width - System.Math.Min(_propertyWidth, _width / 2))
            {
                _propertyScrollOffset = System.Math.Max(0, _propertyScrollOffset - System.Math.Sign(input.MouseWheelDelta) * 3);
            }
        }
        UpdateHover(input, renderer);
        if (input.WasRightMouseButtonPressed)
        {
            HandleExplorerContextMenu(input);
        }
        if (input.WasLeftMouseButtonPressed)
        {
            if (TryBeginPanelResize(input))
            {
                input.ConsumeCameraInput();
                return;
            }
            if (TryBeginUiTransformDrag(input))
            {
                input.ConsumeCameraInput();
                return;
            }
            if (TryBeginGizmoDrag(input, camera))
            {
                input.ConsumeCameraInput();
                return;
            }
            HandleClick(input, renderer);
        }
        if (input.WasLeftMouseButtonReleased)
        {
            HandleDrop(input);
        }
        if (_focus != UiFocus.None || _colorWheelProperty is not null ||
            _gizmoDrag is not null || _uiTransformDrag is not null || _uiResizeDrag is not null)
        {
            input.ConsumeCameraInput();
        }
    }

    public void Render(int[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != _width * _height)
        {
            throw new ArgumentException("The pixel buffer dimensions do not match the UI dimensions.", nameof(pixels));
        }

        UiFont.SetResolution(_fontResolution);
        _scene.UI.Render(pixels, _width, _height);
        RenderCore(new SoftwareUiDrawTarget(pixels, _width, _height));
    }

    internal UiDrawList BuildDrawList()
    {
        _drawList ??= new UiDrawList(_width, _height);
        _drawList.Prepare(_width, _height);
        UiFont.SetResolution(_fontResolution);
        _scene.UI.Render(_drawList, _width, _height);
        RenderCore(_drawList);
        return _drawList;
    }

    private void RenderCore(IUiDrawTarget pixels)
    {
        UiFont.SetResolution(_fontResolution);
        _treeHits.Clear();
        _propertyHits.Clear();
        _colorHits.Clear();
        _buttonHits.Clear();
        _contextMenuHits.Clear();
        if (!IsVisible)
        {
            if (_profiler.IsVisible)
            {
                DrawProfiler(pixels);
            }
            if (ConsoleVisible)
            {
                DrawConsole(pixels);
            }
            DrawColorPicker(pixels);
            if (LogVisible)
            {
                DrawLogPanel(pixels);
            }
            return;
        }

        UiCanvas.FillRect(pixels, _width, _height, 0, 0, _width, ToolbarHeight, Palette.Toolbar);
        UiCanvas.DrawText(pixels, _width, _height, 8, 3, "TINY3D", Palette.Accent);
        var buttonX = 8 + "TINY3D".Length * CharacterAdvance + 10;
        DrawButton(pixels, buttonX, "EXPLORER", ExplorerVisible, () => ExplorerVisible = !ExplorerVisible);
        buttonX += ButtonWidth("EXPLORER") + 8;
        DrawButton(pixels, buttonX, "PROPERTIES", PropertiesVisible, () => PropertiesVisible = !PropertiesVisible);
        buttonX += ButtonWidth("PROPERTIES") + 8;
        DrawButton(pixels, buttonX, "PROFILER (F1)", _profiler.IsVisible,
            () => _profiler.IsVisible = !_profiler.IsVisible);
        buttonX += ButtonWidth("PROFILER (F1)") + 8;
        DrawButton(pixels, buttonX, "CONSOLE (F2)", ConsoleVisible, ToggleConsole);
        buttonX += ButtonWidth("CONSOLE (F2)") + 8;
        DrawButton(pixels, buttonX, "LOG (F3)", LogVisible, () => LogVisible = !LogVisible);
        buttonX += ButtonWidth("LOG (F3)") + 8;
        DrawButton(pixels, buttonX, "UNDO", _history.CanUndo, UndoPropertyEdit);
        buttonX += ButtonWidth("UNDO") + 8;
        DrawButton(pixels, buttonX, "REDO", _history.CanRedo, RedoPropertyEdit);
        buttonX += ButtonWidth("REDO") + 8;
        var toolbarText = $"F4 UI   F5 TREE   {WidthLabel()}";
        var toolbarTextX = _width - toolbarText.Length * CharacterAdvance - 8;
        if (toolbarTextX >= buttonX)
        {
            UiCanvas.DrawText(pixels, _width, _height, toolbarTextX, 3, toolbarText, Palette.Muted);
        }

        DrawUiGizmo(pixels);
        var bottom = _height - (LogVisible ? System.Math.Min(150, _height / 3) : 0);
        var leftWidth = ExplorerVisible ? System.Math.Min(_explorerWidth, _width / 2) : 0;
        var rightWidth = PropertiesVisible ? System.Math.Min(_propertyWidth, _width / 2) : 0;
        if (ExplorerVisible && bottom > ToolbarHeight)
        {
            DrawExplorer(pixels, leftWidth, bottom);
        }
        if (PropertiesVisible && bottom > ToolbarHeight)
        {
            DrawProperties(pixels, _width - rightWidth, rightWidth, bottom);
        }
        if (ExplorerVisible && leftWidth > 0)
        {
            UiCanvas.FillRect(pixels, _width, _height, leftWidth - 2, ToolbarHeight, 4,
                bottom - ToolbarHeight, Palette.Border);
        }
        if (PropertiesVisible && rightWidth > 0)
        {
            UiCanvas.FillRect(pixels, _width, _height, _width - rightWidth - 2, ToolbarHeight, 4,
                bottom - ToolbarHeight, Palette.Border);
        }
        if (LogVisible)
        {
            DrawLogPanel(pixels);
        }
        if (_profiler.IsVisible)
        {
            DrawProfiler(pixels);
        }
        if (ConsoleVisible)
        {
            DrawConsole(pixels);
        }
        DrawColorPicker(pixels);

        if (!string.IsNullOrEmpty(_status) &&
            DateTimeOffset.UtcNow - _statusTime < TimeSpan.FromSeconds(4))
        {
            var statusWidth = System.Math.Min(_width - 16, _status.Length * CharacterAdvance + 16);
            UiCanvas.FillRect(pixels, _width, _height, 8, _height - 29, statusWidth, 20, Palette.Panel);
            UiCanvas.DrawText(pixels, _width, _height, 14, _height - 29, _status, Palette.Warning);
        }
        DrawContextMenu(pixels);
    }

    private void HandleTextInput(InputState input)
    {
        if (input.WasKeyPressed(Key.Escape))
        {
            if (_colorWheelProperty is not null)
            {
                CloseColorPicker();
                input.ConsumeKeyPress(Key.Escape);
            }
            else if (ConsoleVisible)
            {
                ConsoleVisible = false;
                _focus = UiFocus.None;
                _textSelectionActive = false;
                input.ConsumeKeyPress(Key.Escape);
            }
            else if (_focus != UiFocus.None)
            {
                _focus = UiFocus.None;
                _editingProperty = null;
                _textSelectionActive = false;
                input.ConsumeKeyPress(Key.Escape);
            }
        }

        var controlDown = input.IsKeyDown(Key.Control) ||
            input.IsKeyDown(Key.LeftControl) || input.IsKeyDown(Key.RightControl);
        if (controlDown && input.WasKeyPressed(Key.A) && _focus != UiFocus.None)
        {
            SelectAllTextInFocus();
            input.ConsumeKeyPress(Key.A);
            input.ConsumeKeyDown(Key.A);
        }

        if (_focus != UiFocus.None)
        {
            if (input.WasKeyPressed(Key.Left))
            {
                _textCursor = System.Math.Max(0, _textCursor - 1);
                _textSelectionActive = false;
                input.ConsumeKeyPress(Key.Left);
                input.ConsumeKeyDown(Key.Left);
            }
            if (input.WasKeyPressed(Key.Right))
            {
                _textCursor = System.Math.Min(GetFocusedText().Length, _textCursor + 1);
                _textSelectionActive = false;
                input.ConsumeKeyPress(Key.Right);
                input.ConsumeKeyDown(Key.Right);
            }
        }

        if (input.WasKeyPressed(Key.Backspace))
        {
            DeleteSelectedOrPreviousCharacter();
            _backspaceRepeatAt = DateTimeOffset.UtcNow.AddMilliseconds(200);
            if (_focus != UiFocus.None)
            {
                input.ConsumeKeyPress(Key.Backspace);
            }
        }
        else if (_focus != UiFocus.None && input.IsKeyDown(Key.Backspace))
        {
            if (_backspaceRepeatAt is { } repeatAt && DateTimeOffset.UtcNow >= repeatAt)
            {
                DeleteSelectedOrPreviousCharacter();
                _backspaceRepeatAt = DateTimeOffset.UtcNow.AddMilliseconds(50);
            }
        }
        else
        {
            _backspaceRepeatAt = null;
        }

        if (input.WasKeyPressed(Key.Enter))
        {
            if (_focus == UiFocus.Console)
            {
                ExecuteConsoleInput();
            }
            else if (_focus == UiFocus.Property)
            {
                CommitPropertyEdit();
            }
            else if (_focus == UiFocus.Search)
            {
                _focus = UiFocus.None;
            }
            _textSelectionActive = false;
            if (_focus != UiFocus.None || ConsoleVisible)
            {
                input.ConsumeKeyPress(Key.Enter);
            }
        }

        foreach (var character in input.TextInput)
        {
            if (_textSelectionActive)
            {
                ClearFocusedText();
                _textSelectionActive = false;
            }

            switch (_focus)
            {
                case UiFocus.Console:
                    _consoleInput = InsertText(_consoleInput, character);
                    break;
                case UiFocus.Search:
                    _search = InsertText(_search, character);
                    break;
                case UiFocus.Property:
                    _editBuffer = InsertText(_editBuffer, character);
                    break;
            }
        }
    }

    private string InsertText(string text, char character)
    {
        _textCursor = System.Math.Clamp(_textCursor, 0, text.Length);
        var result = text.Insert(_textCursor, character.ToString());
        _textCursor++;
        return result;
    }

    private void SetTextCursorFromClick(string text, int fieldX, int mouseX)
    {
        var visibleOffset = System.Math.Max(0, (mouseX - fieldX) / CharacterAdvance);
        _textCursor = System.Math.Clamp(_textScrollOffset + visibleOffset, 0, text.Length);
    }

    private string GetEditableText(string text, int visibleCharacters, bool focused)
    {
        var cursor = focused ? System.Math.Clamp(_textCursor, 0, text.Length) : text.Length;
        var scrollOffset = focused
            ? System.Math.Clamp(_textScrollOffset, 0, text.Length)
            : System.Math.Max(0, text.Length - visibleCharacters);
        if (focused && cursor < scrollOffset)
        {
            scrollOffset = cursor;
        }
        else if (focused && cursor > scrollOffset + visibleCharacters)
        {
            scrollOffset = cursor - visibleCharacters;
        }
        if (focused)
        {
            _textScrollOffset = scrollOffset;
        }

        var visible = text.Substring(scrollOffset,
            System.Math.Min(text.Length - scrollOffset, visibleCharacters));
        if (!focused)
        {
            return visible;
        }
        var localCursor = System.Math.Clamp(cursor - scrollOffset, 0, visible.Length);
        return visible.Insert(localCursor, "|");
    }

    private void SelectAllTextInFocus()
    {
        _textSelectionActive = _focus != UiFocus.None && GetFocusedText().Length > 0;
    }

    private string GetFocusedText() => _focus switch
    {
        UiFocus.Console => _consoleInput,
        UiFocus.Search => _search,
        UiFocus.Property => _editBuffer,
        _ => string.Empty
    };

    private void TryDeleteTextInput()
    {
        switch (_focus)
        {
            case UiFocus.Console when _consoleInput.Length > 0:
                _textCursor = System.Math.Clamp(_textCursor, 0, _consoleInput.Length);
                if (_textCursor > 0)
                {
                    _consoleInput = _consoleInput.Remove(_textCursor - 1, 1);
                    _textCursor--;
                }
                break;
            case UiFocus.Search when _search.Length > 0:
                _textCursor = System.Math.Clamp(_textCursor, 0, _search.Length);
                if (_textCursor > 0)
                {
                    _search = _search.Remove(_textCursor - 1, 1);
                    _textCursor--;
                }
                break;
            case UiFocus.Property when _editBuffer.Length > 0:
                _textCursor = System.Math.Clamp(_textCursor, 0, _editBuffer.Length);
                if (_textCursor > 0)
                {
                    _editBuffer = _editBuffer.Remove(_textCursor - 1, 1);
                    _textCursor--;
                }
                break;
        }
    }

    private void DeleteSelectedOrPreviousCharacter()
    {
        if (_textSelectionActive)
        {
            ClearFocusedText();
            _textSelectionActive = false;
        }
        else
        {
            TryDeleteTextInput();
        }
    }

    private void ClearFocusedText()
    {
        switch (_focus)
        {
            case UiFocus.Console:
                _consoleInput = string.Empty;
                break;
            case UiFocus.Search:
                _search = string.Empty;
                break;
            case UiFocus.Property:
                _editBuffer = string.Empty;
                break;
        }
        _textCursor = 0;
        _textScrollOffset = 0;
    }

    private void DrawSelectionHighlight(
        IUiDrawTarget pixels,
        UiFocus focus,
        string text,
        int x,
        int y,
        int width,
        int height)
    {
        if (_textSelectionActive && _focus == focus && text.Length > 0)
        {
            UiCanvas.FillRect(pixels, _width, _height, x, y,
                System.Math.Min(text.Length * CharacterAdvance, System.Math.Max(0, width)),
                height, Palette.TextSelection);
        }
    }

    private bool TryBeginPanelResize(InputState input)
    {
        if (!IsVisible || input.MouseY < ToolbarHeight ||
            input.MouseY >= _height - (LogVisible ? System.Math.Min(150, _height / 3) : 0))
        {
            return false;
        }

        if (ExplorerVisible && System.Math.Abs(input.MouseX - System.Math.Min(_explorerWidth, _width / 2)) <= 3)
        {
            _uiResizeDrag = new UiResizeDrag(PanelEdge.Explorer, input.MouseX);
            return true;
        }

        if (PropertiesVisible &&
            System.Math.Abs(input.MouseX - (_width - System.Math.Min(_propertyWidth, _width / 2))) <= 3)
        {
            _uiResizeDrag = new UiResizeDrag(PanelEdge.Properties, input.MouseX);
            return true;
        }

        return false;
    }

    private void UpdatePanelResize(UiResizeDrag drag, InputState input)
    {
        const int minimumWidth = 150;
        var maximumWidth = System.Math.Max(minimumWidth, _width - minimumWidth);
        if (drag.Edge == PanelEdge.Explorer)
        {
            _explorerWidth = System.Math.Clamp(input.MouseX, minimumWidth, maximumWidth);
        }
        else
        {
            _propertyWidth = System.Math.Clamp(_width - input.MouseX, minimumWidth, maximumWidth);
        }
    }

    private bool TryBeginUiTransformDrag(InputState input)
    {
        if (Explorer.Selected is not UIElement { IsVisibleInTree: true } element ||
            !Explorer.Contains(element) || IsOverPanel(input.MouseX, input.MouseY) ||
            !TryGetUiBounds(element, out var bounds))
        {
            return false;
        }

        var centerX = bounds.X + bounds.Width / 2f;
        var centerY = bounds.Y + bounds.Height / 2f;
        var deltaX = input.MouseX - centerX;
        var deltaY = input.MouseY - centerY;
        var axis = -1;
        if (GizmoMode == TransformGizmoMode.Position)
        {
            if (MathF.Abs(deltaX) < 12 && MathF.Abs(deltaY) < 12)
            {
                axis = 2;
            }
            else if (MathF.Abs(deltaY) < 8 && deltaX >= 0 && deltaX <= 42)
            {
                axis = 0;
            }
            else if (MathF.Abs(deltaX) < 8 && deltaY <= 0 && deltaY >= -42)
            {
                axis = 1;
            }
        }
        else if (GizmoMode == TransformGizmoMode.Scale)
        {
            var handleX = bounds.Right;
            var handleY = bounds.Bottom;
            if (MathF.Abs(input.MouseX - handleX) <= 12 && MathF.Abs(input.MouseY - handleY) <= 12)
            {
                axis = 2;
            }
        }
        else if (MathF.Abs(MathF.Sqrt(deltaX * deltaX + deltaY * deltaY) - 36) <= 8)
        {
            axis = 2;
        }

        if (axis < 0)
        {
            return false;
        }

        _uiTransformDrag = new UiTransformDrag(
            element,
            GizmoMode,
            axis,
            input.MouseX,
            input.MouseY,
            centerX,
            centerY,
            MathF.Atan2(deltaY, deltaX),
            element.Position,
            element.Scale,
            element.Rotation);
        return true;
    }

    private void UpdateUiTransformDrag(UiTransformDrag drag, InputState input)
    {
        var deltaX = input.MouseX - drag.InitialMouseX;
        var deltaY = input.MouseY - drag.InitialMouseY;
        switch (drag.Mode)
        {
            case TransformGizmoMode.Position:
                var positionOffset = drag.InitialPosition.AbsoluteOffset;
                if (drag.Axis == 0)
                {
                    deltaY = 0;
                }
                else if (drag.Axis == 1)
                {
                    deltaX = 0;
                }
                drag.Element.Position = drag.InitialPosition with
                {
                    AbsoluteOffset = new Vector2(positionOffset.X + deltaX, positionOffset.Y + deltaY)
                };
                break;
            case TransformGizmoMode.Scale:
                var scaleOffset = drag.InitialScale.AbsoluteOffset;
                drag.Element.Scale = drag.InitialScale with
                {
                    AbsoluteOffset = new Vector2(
                        MathF.Max(1, scaleOffset.X + deltaX),
                        MathF.Max(1, scaleOffset.Y + deltaY))
                };
                break;
            case TransformGizmoMode.Rotation:
                var angle = MathF.Atan2(input.MouseY - drag.CenterY, input.MouseX - drag.CenterX);
                var difference = angle - drag.InitialPointerAngle;
                while (difference > MathF.PI)
                {
                    difference -= 2 * MathF.PI;
                }
                while (difference < -MathF.PI)
                {
                    difference += 2 * MathF.PI;
                }
                drag.Element.Rotation = drag.InitialRotation + (int)MathF.Round(difference * 180 / MathF.PI);
                break;
        }
    }

    private bool TryGetUiBounds(UIElement target, out UiRect bounds)
    {
        var ancestry = new Stack<UIElement>();
        for (Construct? current = target; current is not null; current = current.Branch)
        {
            if (current is UIElement element)
            {
                ancestry.Push(element);
            }
        }

        bounds = new UiRect(0, 0, _width, _height);
        while (ancestry.Count > 0)
        {
            var element = ancestry.Pop();
            var size = element.Scale.Resolve(bounds.Width, bounds.Height);
            var position = element.Position.Resolve(bounds.Width, bounds.Height);
            var anchor = element.AnchorPoint.Resolve(size.X, size.Y);
            bounds = new UiRect(
                (int)MathF.Round(bounds.X + position.X - anchor.X),
                (int)MathF.Round(bounds.Y + position.Y - anchor.Y),
                (int)MathF.Round(size.X),
                (int)MathF.Round(size.Y));
        }

        return bounds.Width > 0 && bounds.Height > 0;
    }

    private void DrawUiGizmo(IUiDrawTarget pixels)
    {
        if (Explorer.Selected is not UIElement { IsVisibleInTree: true } element ||
            !Explorer.Contains(element) || !TryGetUiBounds(element, out var bounds))
        {
            return;
        }

        UiCanvas.DrawRectangle(pixels, _width, _height, bounds.X, bounds.Y,
            bounds.Width, bounds.Height, Palette.Accent);
        var centerX = bounds.X + bounds.Width / 2;
        var centerY = bounds.Y + bounds.Height / 2;
        if (GizmoMode == TransformGizmoMode.Position)
        {
            UiCanvas.DrawLine(pixels, centerX, centerY, centerX + 42, centerY, Palette.Error);
            UiCanvas.DrawLine(pixels, centerX, centerY, centerX, centerY - 42, Palette.Graph);
            UiCanvas.FillRect(pixels, _width, _height, centerX - 5, centerY - 5, 11, 11, Palette.Accent);
        }
        else if (GizmoMode == TransformGizmoMode.Scale)
        {
            UiCanvas.DrawLine(pixels, bounds.X, bounds.Y, bounds.Right, bounds.Bottom, Palette.Accent);
            UiCanvas.DrawRectangle(pixels, _width, _height, bounds.Right - 5, bounds.Bottom - 5,
                11, 11, Palette.Text);
        }
        else
        {
            const int radius = 36;
            var previousX = centerX + radius;
            var previousY = centerY;
            for (var index = 1; index <= 48; index++)
            {
                var angle = index * MathF.Tau / 48;
                var currentX = centerX + (int)MathF.Round(MathF.Cos(angle) * radius);
                var currentY = centerY + (int)MathF.Round(MathF.Sin(angle) * radius);
                UiCanvas.DrawLine(pixels, previousX, previousY, currentX, currentY, Palette.Accent);
                previousX = currentX;
                previousY = currentY;
            }
        }
    }

    private bool TryBeginGizmoDrag(InputState input, Camera camera)
    {
        if ((uint)input.MouseX >= (uint)_width || (uint)input.MouseY >= (uint)_height ||
            Explorer.Selected is not { IsVisibleInTree: true } selected ||
            selected is not ITransformableConstruct transformable ||
            IsOverPanel(input.MouseX, input.MouseY) ||
            !TryProjectWorld(selected.TransformPoint(Vector3.Zero), camera, out var screenOrigin))
        {
            return false;
        }

        var (origin, size) = TransformGizmoGeometry.GetFrame(selected, camera.Position);
        var axes = TransformGizmoGeometry.GetAxes();
        var selectedAxis = -1;
        if (GizmoMode == TransformGizmoMode.Rotation)
        {
            var nearestDistance = 9f;
            for (var axisIndex = 0; axisIndex < axes.Count; axisIndex++)
            {
                var axis = axes[axisIndex];
                var previous = origin + axis.First * size;
                for (var segmentIndex = 1; segmentIndex <= 32; segmentIndex++)
                {
                    var angle = segmentIndex * (2 * MathF.PI / 32);
                    var current = origin + axis.First * (MathF.Cos(angle) * size) +
                        axis.Second * (MathF.Sin(angle) * size);
                    if (TryProjectWorld(previous, camera, out var segmentStart) &&
                        TryProjectWorld(current, camera, out var segmentEnd))
                    {
                        var distance = DistanceToSegment(input.MouseX, input.MouseY, segmentStart, segmentEnd);
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            selectedAxis = axisIndex;
                        }
                    }
                    previous = current;
                }
            }
        }
        else
        {
            var nearestDistance = 9f;
            for (var axisIndex = 0; axisIndex < axes.Count; axisIndex++)
            {
                var axisEnd = origin + axes[axisIndex].Axis * size;
                if (!TryProjectWorld(axisEnd, camera, out var screenEnd))
                {
                    continue;
                }

                var distance = DistanceToSegment(input.MouseX, input.MouseY, screenOrigin, screenEnd);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    selectedAxis = axisIndex;
                }
            }
        }

        if (selectedAxis < 0)
        {
            return false;
        }

        var handleEnd = origin + axes[selectedAxis].Axis * size;
        if (!TryProjectWorld(handleEnd, camera, out var screenEndPoint))
        {
            return false;
        }

        _gizmoDrag = new GizmoDrag(
            selected,
            transformable,
            GizmoMode,
            selectedAxis,
            input.MouseX,
            input.MouseY,
            screenOrigin,
            screenEndPoint,
            size,
            MathF.Atan2(input.MouseY - screenOrigin.Y, input.MouseX - screenOrigin.X),
            transformable.Pivot.Position,
            transformable.Pivot.Rotation,
            transformable.Pivot.Scale);
        return true;
    }

    private static float DistanceToSegment(float x, float y, ScreenPoint start, ScreenPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared <= float.Epsilon)
        {
            return MathF.Sqrt((x - start.X) * (x - start.X) + (y - start.Y) * (y - start.Y));
        }

        var amount = System.Math.Clamp(
            ((x - start.X) * deltaX + (y - start.Y) * deltaY) / lengthSquared, 0, 1);
        var nearestX = start.X + amount * deltaX;
        var nearestY = start.Y + amount * deltaY;
        return MathF.Sqrt((x - nearestX) * (x - nearestX) + (y - nearestY) * (y - nearestY));
    }

    private void UpdateGizmoDrag(GizmoDrag drag, InputState input, Camera camera)
    {
        var axis = TransformGizmoGeometry.GetAxes()[drag.AxisIndex].Axis;
        if (drag.Mode == TransformGizmoMode.Rotation)
        {
            var currentAngle = MathF.Atan2(
                input.MouseY - drag.ScreenOrigin.Y,
                input.MouseX - drag.ScreenOrigin.X);
            var angleDelta = currentAngle - drag.InitialPointerAngle;
            while (angleDelta > MathF.PI)
            {
                angleDelta -= 2 * MathF.PI;
            }
            while (angleDelta < -MathF.PI)
            {
                angleDelta += 2 * MathF.PI;
            }

            drag.Transform.Pivot.Rotation = WithComponent(
                drag.InitialRotation, drag.AxisIndex, GetComponent(drag.InitialRotation, drag.AxisIndex) + angleDelta);
            return;
        }

        var projectedAxisX = drag.ScreenEnd.X - drag.ScreenOrigin.X;
        var projectedAxisY = drag.ScreenEnd.Y - drag.ScreenOrigin.Y;
        var projectedLengthSquared = projectedAxisX * projectedAxisX + projectedAxisY * projectedAxisY;
        if (projectedLengthSquared <= 1)
        {
            return;
        }

        var pointerDeltaX = input.MouseX - drag.InitialMouseX;
        var pointerDeltaY = input.MouseY - drag.InitialMouseY;
        var worldDeltaAmount = ((pointerDeltaX * projectedAxisX + pointerDeltaY * projectedAxisY) /
            projectedLengthSquared) * drag.Size;

        if (drag.Mode == TransformGizmoMode.Position)
        {
            var worldDelta = axis * worldDeltaAmount;
            var localDelta = worldDelta;
            if (drag.Construct.Branch is { } parent)
            {
                var worldOrigin = parent.TransformPoint(drag.Transform.Pivot.Position);
                if (!parent.TryInverseTransformPoint(worldOrigin, out var localOrigin) ||
                    !parent.TryInverseTransformPoint(worldOrigin + worldDelta, out var localMoved))
                {
                    ReportError("Cannot move a construct through a parent transform with zero scale.");
                    _gizmoDrag = null;
                    return;
                }
                localDelta = localMoved - localOrigin;
            }

            drag.Transform.Pivot.Position = drag.InitialPosition + localDelta;
            return;
        }

        var initialScale = GetComponent(drag.InitialScale, drag.AxisIndex);
        drag.Transform.Pivot.Scale = WithComponent(
            drag.InitialScale,
            drag.AxisIndex,
            initialScale + worldDeltaAmount / drag.Size);
    }

    private bool TryProjectWorld(Vector3 point, Camera camera, out ScreenPoint screenPoint)
    {
        var direction = camera.Target - camera.Position;
        if (direction.Length < 0.0001f)
        {
            screenPoint = default;
            return false;
        }

        var forward = direction.Normalized();
        var right = Vector3.Cross(Vector3.Up, forward).Normalized();
        var up = Vector3.Cross(forward, right).Normalized();
        var relative = point - camera.Position;
        var depth = Vector3.Dot(relative, forward);
        if (!float.IsFinite(depth) || depth < camera.NearPlane)
        {
            screenPoint = default;
            return false;
        }

        var focalLength = _height / (2 * MathF.Tan(camera.FieldOfView * MathF.PI / 360));
        var x = _width / 2f + Vector3.Dot(relative, right) * focalLength / depth;
        var y = _height / 2f - Vector3.Dot(relative, up) * focalLength / depth;
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            screenPoint = default;
            return false;
        }

        screenPoint = new ScreenPoint(x, y);
        return true;
    }

    private static float GetComponent(Vector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z
    };

    private static Vector3 WithComponent(Vector3 value, int axis, float component) => axis switch
    {
        0 => new Vector3(component, value.Y, value.Z),
        1 => new Vector3(value.X, component, value.Z),
        _ => new Vector3(value.X, value.Y, component)
    };

    private void UpdateHover(InputState input, IScenePicker renderer)
    {
        if (IsOverlayPanelHit(input.MouseX, input.MouseY))
        {
            Explorer.SetHovered(null);
            return;
        }

        var construct = _treeHits
            .LastOrDefault(hit => hit.Bounds.Contains(input.MouseX, input.MouseY))
            .Construct;
        if (construct is null && !IsOverPanel(input.MouseX, input.MouseY))
        {
            renderer.TryPick(input.MouseX, input.MouseY, out var picked);
            construct = picked?.OwningScene == _scene ? picked : null;
        }

        Explorer.SetHovered(Explorer.Contains(construct) ? construct : null);
    }

    private void HandleClick(InputState input, IScenePicker renderer)
    {
        var contextMenuHit = _contextMenuHits
            .LastOrDefault(hit => hit.Bounds.Contains(input.MouseX, input.MouseY));
        if (contextMenuHit.Action is not null)
        {
            _contextMenuOpen = false;
            contextMenuHit.Action();
            return;
        }
        if (_contextMenuOpen)
        {
            _contextMenuOpen = false;
        }

        if (_colorWheelProperty is not null && _colorWheelBounds.Contains(input.MouseX, input.MouseY))
        {
            _colorWheelDragging = UpdateColorWheelFromPointer(input.MouseX, input.MouseY);
            return;
        }

        var colorHitIndex = _colorHits.FindLastIndex(hit => hit.Bounds.Contains(input.MouseX, input.MouseY));
        if (colorHitIndex >= 0)
        {
            OpenColorPicker(_colorHits[colorHitIndex]);
            return;
        }
        if (_colorWheelProperty is not null)
        {
            CloseColorPicker();
        }

        foreach (var button in _buttonHits)
        {
            if (button.Bounds.Contains(input.MouseX, input.MouseY))
            {
                _focus = UiFocus.None;
                _textSelectionActive = false;
                button.Action();
                return;
            }
        }

        var searchPressed = ExplorerVisible &&
            input.MouseX < System.Math.Min(_explorerWidth, _width / 2) &&
            input.MouseY >= ToolbarHeight + 28 &&
            input.MouseY < ToolbarHeight + 48;
        var consoleInputPressed = ConsoleVisible && IsConsoleInputHit(input.MouseX, input.MouseY);
        var propertyPressed = _propertyHits.Any(hit => hit.Bounds.Contains(input.MouseX, input.MouseY));
        if (_focus != UiFocus.None && !searchPressed && !consoleInputPressed && !propertyPressed)
        {
            _focus = UiFocus.None;
            _textSelectionActive = false;
        }

        if (IsOverlayPanelHit(input.MouseX, input.MouseY))
        {
            if (consoleInputPressed)
            {
                _focus = UiFocus.Console;
                _textSelectionActive = false;
                _textScrollOffset = 0;
                var width = System.Math.Min(570, _width - 24);
                var left = (_width - width) / 2 + 14 + 2 * CharacterAdvance;
                SetTextCursorFromClick(_consoleInput, left, input.MouseX);
            }
            return;
        }

        if (ExplorerVisible && input.MouseX < System.Math.Min(_explorerWidth, _width / 2) &&
            input.MouseY >= ToolbarHeight &&
            input.MouseY < _height - (LogVisible ? System.Math.Min(150, _height / 3) : 0))
        {
            if (searchPressed)
            {
                _focus = UiFocus.Search;
                _textSelectionActive = false;
                _textScrollOffset = 0;
                SetTextCursorFromClick(_search, 14, input.MouseX);
                return;
            }

            var row = _treeHits.LastOrDefault(hit => hit.Bounds.Contains(input.MouseX, input.MouseY));
            if (Explorer.Contains(row.Construct))
            {
                Explorer.Select(row.Construct);
                _draggedConstruct = row.Construct?.OwningScene == _scene ? row.Construct : null;
                _focus = UiFocus.None;
                _textSelectionActive = false;
                _editingProperty = null;
                _editingTarget = null;
                return;
            }
        }

        foreach (var row in _propertyHits)
        {
            if (!row.Bounds.Contains(input.MouseX, input.MouseY) ||
                Explorer.Selected is not { } target ||
                row.Property is not { } property)
            {
                continue;
            }

            _focus = UiFocus.None;
            _textSelectionActive = false;
            if (property.PropertyType == typeof(Color32))
            {
                OpenColorPicker(new ColorHit(row.Bounds, target, property));
                return;
            }

            if (property.IsReadOnly)
            {
                return;
            }

            if (property.PropertyType == typeof(bool))
            {
                try
                {
                    _propertyPanel.SetValue(target, property.Name, !(bool)property.GetValue(target)!);
                }
                catch (Exception exception) when (IsInputError(exception))
                {
                    ReportError(exception.Message);
                }
                return;
            }

            _editingTarget = target;
            _editingProperty = property.Name;
            _editBuffer = FormatValue(property.GetValue(target));
            _focus = UiFocus.Property;
            _textCursor = _editBuffer.Length;
            _textScrollOffset = 0;
            SetTextCursorFromClick(_editBuffer, row.Bounds.X + row.Bounds.Width / 2, input.MouseX);
            _textSelectionActive = false;
            return;
        }

        if (!IsOverPanel(input.MouseX, input.MouseY))
        {
            if (renderer.TryPick(input.MouseX, input.MouseY, out var picked))
            {
                Explorer.Select(picked?.OwningScene == _scene ? picked : null);
            }
            else
            {
                Explorer.Select(null);
            }
        }
    }

    private void HandleExplorerContextMenu(InputState input)
    {
        if (!IsVisible || !ExplorerVisible ||
            input.MouseX < 0 || input.MouseX >= System.Math.Min(_explorerWidth, _width / 2) ||
            input.MouseY < ToolbarHeight ||
            input.MouseY >= _height - (LogVisible ? System.Math.Min(150, _height / 3) : 0))
        {
            _contextMenuOpen = false;
            return;
        }

        var row = _treeHits.LastOrDefault(hit => hit.Bounds.Contains(input.MouseX, input.MouseY));
        _contextMenuParent = row.Construct?.OwningScene == _scene ? row.Construct : null;
        _contextMenuOpen = true;
        _contextMenuX = input.MouseX;
        _contextMenuY = input.MouseY;
    }

    private void DrawContextMenu(IUiDrawTarget pixels)
    {
        if (!_contextMenuOpen || !IsVisible || !ExplorerVisible)
        {
            return;
        }

        const int itemHeight = 22;
        var explorerRight = System.Math.Min(_explorerWidth, _width / 2);
        var menuWidth = System.Math.Min(160, explorerRight);
        var menuHeight = itemHeight * 4 + 2;
        var bottom = _height - (LogVisible ? System.Math.Min(150, _height / 3) : 0);
        var x = System.Math.Clamp(_contextMenuX, 0, System.Math.Max(0, explorerRight - menuWidth));
        var y = System.Math.Clamp(_contextMenuY, ToolbarHeight, System.Math.Max(ToolbarHeight, bottom - menuHeight));
        UiCanvas.FillRect(pixels, _width, _height, x, y, menuWidth, menuHeight, Palette.Panel);
        UiCanvas.DrawRectangle(pixels, _width, _height, x, y, menuWidth, menuHeight, Palette.Border);

        var labels = new[] { "ADD ENTITY", "ADD SHAPE2D", "ADD TEXTBOX2D", "ADD EMPTY CONSTRUCT" };
        for (var index = 0; index < labels.Length; index++)
        {
            var itemBounds = new UiRect(x + 1, y + 1 + index * itemHeight, menuWidth - 2, itemHeight);
            var label = UiCanvas.Truncate(labels[index], System.Math.Max(0, (menuWidth - 16) / CharacterAdvance));
            UiCanvas.DrawText(pixels, _width, _height, itemBounds.X + 8, itemBounds.Y, label, Palette.Text);
            var kind = (NewConstructKind)index;
            _contextMenuHits.Add(new ContextMenuHit(itemBounds, () => AddExplorerConstruct(kind)));
        }
    }

    private void AddExplorerConstruct(NewConstructKind kind)
    {
        Construct construct = kind switch
        {
            NewConstructKind.Entity => new Entity(Mesh.CreateCube()) { Name = "Entity" },
            NewConstructKind.Shape2D => new Shape2D { Name = "Shape2D" },
            NewConstructKind.TextBox2D => new TextBox2D
            {
                Name = "TextBox2D",
                TextContent = "TextBox2D",
                Color = new Color32(32, 36, 40, 220),
                CornerRadius = UIVector2.FromPixels(4, 4)
            },
            NewConstructKind.EmptyConstruct => new EmptyConstruct { Name = "Construct" },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        try
        {
            _history.Execute(new AddConstructCommand(_scene, construct, _contextMenuParent));
            Explorer.Select(construct);
            _contextMenuParent = null;
        }
        catch (Exception exception) when (IsInputError(exception))
        {
            construct.Dispose();
            ReportError(exception.Message);
        }
    }

    private void HandleDrop(InputState input)
    {
        if (_draggedConstruct is null)
        {
            return;
        }

        var dragged = _draggedConstruct;
        _draggedConstruct = null;
        if (IsOverlayPanelHit(input.MouseX, input.MouseY))
        {
            return;
        }

        if (dragged.OwningScene != _scene)
        {
            return;
        }

        var target = _treeHits
            .LastOrDefault(hit => hit.Bounds.Contains(input.MouseX, input.MouseY))
            .Construct;
        var overTree = ExplorerVisible &&
            input.MouseX < System.Math.Min(_explorerWidth, _width / 2) &&
            input.MouseY >= ToolbarHeight &&
            input.MouseY < _height - (LogVisible ? System.Math.Min(150, _height / 3) : 0);
        if (target is null && IsOverPanel(input.MouseX, input.MouseY) && !overTree)
        {
            return;
        }
        if (target is not null && ReferenceEquals(target, dragged))
        {
            return;
        }

        try
        {
            Explorer.Reparent(dragged, target);
            SetStatus(target is null ? "Moved to scene root" : $"Reparented to {DisplayName(target)}");
        }
        catch (InvalidOperationException exception)
        {
            ReportError(exception.Message);
        }
    }

    private void HandleConstructShortcuts(InputState input)
    {
        if (_focus != UiFocus.None || _colorWheelProperty is not null)
        {
            return;
        }

        var controlDown = input.IsKeyDown(Key.Control) ||
            input.IsKeyDown(Key.LeftControl) || input.IsKeyDown(Key.RightControl);
        if (controlDown)
        {
            var key = input.WasKeyPressed(Key.D) ? Key.D :
                input.WasKeyPressed(Key.C) ? Key.C :
                input.WasKeyPressed(Key.V) ? Key.V :
                input.WasKeyPressed(Key.X) ? Key.X :
                default;
            if (key == default)
            {
                return;
            }

            input.ConsumeKeyPress(key);
            input.ConsumeKeyDown(key);
            try
            {
                switch (key)
                {
                    case Key.D:
                        DuplicateSelected();
                        break;
                    case Key.C:
                        CopySelected(cut: false);
                        break;
                    case Key.V:
                        PasteConstruct();
                        break;
                    case Key.X:
                        CopySelected(cut: true);
                        break;
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                ReportError(exception.Message);
            }

            return;
        }

        if (input.WasKeyPressed(Key.Backspace))
        {
            input.ConsumeKeyPress(Key.Backspace);
            try
            {
                DeleteSelected();
            }
            catch (InvalidOperationException exception)
            {
                ReportError(exception.Message);
            }
        }
    }

    private void DuplicateSelected()
    {
        var selected = GetEditableSelection();
        var duplicate = selected.DuplicateTree();
        duplicate.Name = CopyName(selected.Name);
        AddBesideSelection(duplicate, selected);
        Explorer.Select(duplicate);
        SetStatus($"Duplicated {DisplayName(selected)}");
    }

    private void CopySelected(bool cut)
    {
        var selected = GetEditableSelection();
        var copy = selected.DuplicateTree();
        if (cut)
        {
            selected.Dispose();
            Explorer.Select(null);
            SetStatus($"Cut {DisplayName(selected)}");
        }
        else
        {
            SetStatus($"Copied {DisplayName(selected)}");
        }

        _constructClipboard?.Dispose();
        _constructClipboard = copy;
    }

    private void PasteConstruct()
    {
        if (_constructClipboard is null)
        {
            return;
        }

        var duplicate = _constructClipboard.DuplicateTree();
        duplicate.Name = CopyName(_constructClipboard.Name);
        var selected = Explorer.Selected;
        var parent = selected is not null && CanEdit(selected) ? selected.Branch : null;
        if (parent is null)
        {
            _scene.Add(duplicate);
        }
        else
        {
            parent.AddDescendant(duplicate);
        }

        Explorer.Select(duplicate);
        SetStatus($"Pasted {DisplayName(duplicate)}");
    }

    private void DeleteSelected()
    {
        var selected = GetEditableSelection();
        selected.Dispose();
        Explorer.Select(null);
        SetStatus($"Deleted {DisplayName(selected)}");
    }

    private Construct GetEditableSelection()
    {
        var selected = Explorer.Selected;
        if (selected is null || !CanEdit(selected))
        {
            throw new InvalidOperationException("Select a scene construct that can be edited.");
        }

        return selected;
    }

    private bool CanEdit(Construct construct) =>
        ReferenceEquals(construct.OwningScene, _scene) && !ContainsProtectedConstruct(construct);

    private bool ContainsProtectedConstruct(Construct construct) =>
        construct is CameraConstruct ||
        Explorer.Services.Contains(construct) ||
        construct.Descendants.Any(ContainsProtectedConstruct);

    private void AddBesideSelection(Construct duplicate, Construct selected)
    {
        if (selected.Branch is { } parent)
        {
            parent.AddDescendant(duplicate);
        }
        else
        {
            _scene.Add(duplicate);
        }
    }

    private static string CopyName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "Construct Copy" : $"{name} Copy";

    private void OpenColorPicker(ColorHit hit)
    {
        _colorWheelTarget = hit.Target;
        _colorWheelProperty = hit.Property;
        _colorWheelPreview = (Color32)hit.Property.GetValue(hit.Target)!;
        GetHsv(_colorWheelPreview.Value, out _colorWheelHue, out _colorWheelSaturation,
            out _colorWheelBrightness);
        _colorWheelDragging = false;
        const int pickerWidth = 152;
        const int pickerHeight = 108;
        var x = System.Math.Clamp(hit.Bounds.X - pickerWidth, 0,
            System.Math.Max(0, _width - pickerWidth));
        var y = System.Math.Clamp(hit.Bounds.Bottom + 3, ToolbarHeight + 2,
            System.Math.Max(ToolbarHeight + 2, _height - pickerHeight));
        _colorWheelBounds = new UiRect(x, y, pickerWidth, pickerHeight);
    }

    private void UpdateColorWheel(InputState input)
    {
        if (!_colorWheelDragging)
        {
            return;
        }

        if (input.IsLeftMouseButtonDown)
        {
            UpdateColorWheelFromPointer(input.MouseX, input.MouseY);
        }

        if (!input.WasLeftMouseButtonReleased)
        {
            return;
        }

        UpdateColorWheelFromPointer(input.MouseX, input.MouseY);
        if (_colorWheelTarget is { } target &&
            _colorWheelProperty is { } property &&
            _colorWheelPreview is { } preview)
        {
            try
            {
                _propertyPanel.SetValue(target, property.Name, preview);
                SetStatus($"Updated {property.Name}");
            }
            catch (Exception exception) when (IsInputError(exception))
            {
                ReportError(exception.Message);
            }
        }

        CloseColorPicker();
    }

    private bool UpdateColorWheelFromPointer(int x, int y)
    {
        if (_colorWheelTarget is null || _colorWheelPreview is null)
        {
            return false;
        }

        var centerX = _colorWheelBounds.X + 49;
        var centerY = _colorWheelBounds.Y + 54;
        var deltaX = x - centerX;
        var deltaY = y - centerY;
        const float radius = 37;
        var distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance <= radius)
        {
            _colorWheelHue = (MathF.Atan2(deltaY, deltaX) * 180 / MathF.PI + 360) % 360;
            _colorWheelSaturation = System.Math.Clamp(distance / radius, 0, 1);
        }
        else
        {
            var slider = new UiRect(_colorWheelBounds.X + 109, _colorWheelBounds.Y + 12, 12, 84);
            if (!slider.Contains(x, y))
            {
                return false;
            }

            _colorWheelBrightness = 1 - System.Math.Clamp(
                (y - slider.Y) / (float)(slider.Height - 1), 0, 1);
        }

        _colorWheelPreview = FromHsv(
            _colorWheelHue,
            _colorWheelSaturation,
            _colorWheelBrightness,
            _colorWheelPreview.Value.A);
        return true;
    }

    private void DrawColorPicker(IUiDrawTarget pixels)
    {
        if (_colorWheelProperty is null || _colorWheelPreview is not { } preview)
        {
            return;
        }

        var bounds = _colorWheelBounds;
        UiCanvas.FillRect(pixels, _width, _height, bounds.X, bounds.Y,
            bounds.Width, bounds.Height, Palette.Panel);
        UiCanvas.DrawRectangle(pixels, _width, _height, bounds.X, bounds.Y,
            bounds.Width, bounds.Height, Palette.Border);

        var centerX = bounds.X + 49;
        var centerY = bounds.Y + 54;
        const int radius = 37;
        const int hueSegments = 96;
        const int saturationSteps = 8;
        for (var hueIndex = 0; hueIndex < hueSegments; hueIndex++)
        {
            var hue = hueIndex * 360f / hueSegments;
            var angle = hue * MathF.PI / 180;
            var directionX = MathF.Cos(angle);
            var directionY = MathF.Sin(angle);
            for (var step = 0; step < saturationSteps; step++)
            {
                var saturation = (step + 0.5f) / saturationSteps;
                var startRadius = radius * step / saturationSteps;
                var endRadius = radius * (step + 1) / saturationSteps;
                var color = FromHsv(hue, saturation, _colorWheelBrightness, preview.A);
                UiCanvas.DrawLine(pixels,
                    (int)MathF.Round(centerX + directionX * startRadius),
                    (int)MathF.Round(centerY + directionY * startRadius),
                    (int)MathF.Round(centerX + directionX * endRadius),
                    (int)MathF.Round(centerY + directionY * endRadius),
                    color);
            }
        }

        var selectorRadius = radius * _colorWheelSaturation;
        var selectorX = (int)MathF.Round(centerX + MathF.Cos(_colorWheelHue * MathF.PI / 180) * selectorRadius);
        var selectorY = (int)MathF.Round(centerY + MathF.Sin(_colorWheelHue * MathF.PI / 180) * selectorRadius);
        UiCanvas.DrawRectangle(pixels, _width, _height, selectorX - 2, selectorY - 2,
            5, 5, Palette.Text);
        var slider = new UiRect(bounds.X + 109, bounds.Y + 12, 12, 84);
        for (var offset = 0; offset < slider.Height; offset++)
        {
            var value = 1 - offset / (float)(slider.Height - 1);
            UiCanvas.FillRect(pixels, _width, _height, slider.X, slider.Y + offset,
                slider.Width, 1, FromHsv(_colorWheelHue, _colorWheelSaturation, value, preview.A));
        }

        UiCanvas.DrawRectangle(pixels, _width, _height, slider.X, slider.Y,
            slider.Width, slider.Height, Palette.Border);
        var sliderY = slider.Y + (int)MathF.Round((1 - _colorWheelBrightness) * (slider.Height - 1));
        UiCanvas.DrawLine(pixels, slider.X - 2, sliderY, slider.Right + 1, sliderY, Palette.Text);
    }

    private void CloseColorPicker()
    {
        _colorWheelTarget = null;
        _colorWheelProperty = null;
        _colorWheelPreview = null;
        _colorWheelDragging = false;
    }

    private static void GetHsv(Color32 color, out float hue, out float saturation, out float value)
    {
        var red = color.R / 255f;
        var green = color.G / 255f;
        var blue = color.B / 255f;
        var maximum = MathF.Max(red, MathF.Max(green, blue));
        var minimum = MathF.Min(red, MathF.Min(green, blue));
        var delta = maximum - minimum;
        hue = delta == 0 ? 0 :
            maximum == red ? 60 * (((green - blue) / delta) % 6) :
            maximum == green ? 60 * ((blue - red) / delta + 2) :
            60 * ((red - green) / delta + 4);
        hue = (hue + 360) % 360;
        saturation = maximum == 0 ? 0 : delta / maximum;
        value = maximum;
    }

    private static Color32 FromHsv(float hue, float saturation, float value, byte alpha)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - MathF.Abs(hue / 60 % 2 - 1));
        var offset = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0f),
            < 120 => (x, chroma, 0f),
            < 180 => (0f, chroma, x),
            < 240 => (0f, x, chroma),
            < 300 => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };
        return new Color32(
            (byte)MathF.Round((red + offset) * 255),
            (byte)MathF.Round((green + offset) * 255),
            (byte)MathF.Round((blue + offset) * 255),
            alpha);
    }

    private void DrawExplorer(IUiDrawTarget pixels, int width, int bottom)
    {
        DrawPanel(pixels, 0, ToolbarHeight, width, bottom - ToolbarHeight, "SCENE EXPLORER");
        var searchBounds = new UiRect(8, ToolbarHeight + 28, width - 16, 20);
        UiCanvas.FillRect(pixels, _width, _height, searchBounds.X, searchBounds.Y,
            searchBounds.Width, searchBounds.Height, _focus == UiFocus.Search ? Palette.FieldActive : Palette.Field);
        DrawSelectionHighlight(pixels, UiFocus.Search, _search, searchBounds.X + 6, searchBounds.Y,
            searchBounds.Width - 12, searchBounds.Height);
        var searchCapacity = System.Math.Max(0, (searchBounds.Width - 12) / CharacterAdvance);
        UiCanvas.DrawText(pixels, _width, _height, searchBounds.X + 6, searchBounds.Y,
            string.IsNullOrEmpty(_search) ? "FILTER CONSTRUCTS" :
                GetEditableText(_search, searchCapacity, _focus == UiFocus.Search),
            string.IsNullOrEmpty(_search) ? Palette.Muted : Palette.Text);
        var rows = _treeRows;
        rows.Clear();
        if (string.IsNullOrEmpty(_search))
        {
            foreach (var root in _scene.Roots)
            {
                AppendTreeRows(rows, root, 0);
            }
            foreach (var service in Explorer.Services)
            {
                AppendTreeRows(rows, service, 0);
            }
        }
        else
        {
            foreach (var root in _scene.Roots)
            {
                AppendMatchingTreeRows(rows, root, _search, 0);
            }
            foreach (var service in Explorer.Services)
            {
                AppendMatchingTreeRows(rows, service, _search, 0);
            }
        }

        var firstRowY = searchBounds.Bottom + 5;
        var visibleRows = System.Math.Max(0, (bottom - firstRowY) / RowHeight);
        _treeScrollOffset = System.Math.Clamp(_treeScrollOffset, 0, System.Math.Max(0, rows.Count - visibleRows));
        var drawCount = System.Math.Min(visibleRows, rows.Count - _treeScrollOffset);
        var y = firstRowY;
        for (var index = _treeScrollOffset; index < _treeScrollOffset + drawCount; index++)
        {
            var row = rows[index];
            DrawTreeRow(pixels, row.Construct, row.Depth, width, bottom, ref y);
        }

        if (_scene.Roots.Count == 0 && Explorer.Services.Count == 0)
        {
            UiCanvas.DrawText(pixels, _width, _height, 14, y, "NO CONSTRUCTS", Palette.Muted);
        }
    }

    private static void AppendTreeRows(List<(Construct Construct, int Depth)> rows, Construct construct, int depth)
    {
        rows.Add((construct, depth));
        foreach (var child in construct.Descendants)
        {
            AppendTreeRows(rows, child, depth + 1);
        }
    }

    private static void AppendMatchingTreeRows(
        List<(Construct Construct, int Depth)> rows,
        Construct construct,
        string query,
        int depth)
    {
        if (construct.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            rows.Add((construct, System.Math.Min(depth, 5)));
        }

        foreach (var child in construct.Descendants)
        {
            AppendMatchingTreeRows(rows, child, query, depth + 1);
        }
    }

    private void DrawTreeRow(IUiDrawTarget pixels, Construct construct, int depth, int width, int bottom, ref int y)
    {
        if (y + RowHeight > bottom)
        {
            return;
        }

        var bounds = new UiRect(4, y, width - 8, RowHeight);
        if (ReferenceEquals(construct, Explorer.Selected))
        {
            UiCanvas.DrawRectangle(pixels, _width, _height,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, Palette.Accent);
        }
        else if (ReferenceEquals(construct, Explorer.Hovered))
        {
            UiCanvas.DrawRectangle(pixels, _width, _height,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, Palette.Border);
        }

        var marker = construct.Descendants.Count == 0 ? "-" : "+";
        UiCanvas.DrawText(pixels, _width, _height, 10 + depth * 12, y, marker, Palette.Accent);
        var visibleName = construct.Visible ? DisplayName(construct) : $"[{DisplayName(construct)}]";
        UiCanvas.DrawText(pixels, _width, _height, 22 + depth * 12, y, visibleName, Palette.Text);
        if (!construct.Visible)
        {
            UiCanvas.DrawText(pixels, _width, _height, width - 20, y, "H", Palette.Muted);
        }

        _treeHits.Add(new RowHit(bounds, construct, null));
        y += RowHeight;
    }

    private void DrawProperties(IUiDrawTarget pixels, int x, int width, int bottom)
    {
        DrawPanel(pixels, x, ToolbarHeight, width, bottom - ToolbarHeight, "INSPECTOR");
        var construct = Explorer.Selected;
        if (construct is null)
        {
            UiCanvas.DrawText(pixels, _width, _height, x + 12, ToolbarHeight + 30, "SELECT A CONSTRUCT", Palette.Muted);
            return;
        }

        UiCanvas.DrawText(pixels, _width, _height, x + 12, ToolbarHeight + 30,
            DisplayName(construct).ToUpperInvariant(), Palette.Accent);
        var properties = _propertyPanel.GetProperties(construct);
        var rows = properties
            .GroupBy(property => property.Group)
            .SelectMany(group => new[] { new PropertyRow(group.Key, null) }
                .Concat(group.Select(property => new PropertyRow(null, property))))
            .ToArray();
        var firstRowY = ToolbarHeight + 50;
        var visibleRows = System.Math.Max(0, (bottom - firstRowY) / RowHeight);
        _propertyScrollOffset = System.Math.Clamp(
            _propertyScrollOffset, 0, System.Math.Max(0, rows.Length - visibleRows));
        var drawCount = System.Math.Min(visibleRows, rows.Length - _propertyScrollOffset);
        var y = firstRowY;
        for (var index = _propertyScrollOffset; index < _propertyScrollOffset + drawCount; index++)
        {
            var row = rows[index];
            if (row.Group is { } groupName)
            {
                UiCanvas.FillRect(pixels, _width, _height, x + 1, y, width - 2, RowHeight, Palette.Group);
                UiCanvas.DrawText(pixels, _width, _height, x + 10, y, groupName.ToUpperInvariant(), Palette.Accent);
                y += RowHeight;
                continue;
            }

            var property = row.Property!;
            var bounds = new UiRect(x + 5, y, width - 10, RowHeight);
            var isEditing = ReferenceEquals(construct, _editingTarget) && property.Name == _editingProperty;
            if (isEditing)
            {
                UiCanvas.FillRect(pixels, _width, _height, x + width / 2, y + 2,
                    width / 2 - 10, RowHeight - 4, Palette.FieldActive);
                DrawSelectionHighlight(pixels, UiFocus.Property, _editBuffer, x + width / 2, y + 2,
                    width / 2 - 10, RowHeight - 4);
            }
            var label = UiCanvas.Truncate(
                property.Name.Replace("Pivot.", string.Empty, StringComparison.Ordinal),
                System.Math.Max(0, (width / 2 - 20) / CharacterAdvance));
            UiCanvas.DrawText(pixels, _width, _height, x + 10, y, label, property.IsReadOnly ? Palette.Muted : Palette.Text);
            var valueCapacity = System.Math.Max(
                0, (width / 2 - (property.PropertyType == typeof(Color32) ? 38 : 10)) / CharacterAdvance);
            var valueText = isEditing
                ? GetEditableText(_editBuffer, valueCapacity, focused: true)
                : UiCanvas.Truncate(FormatValue(property.GetValue(construct)), valueCapacity);
            var valueColor = property.IsReadOnly ? Palette.Muted : Palette.Value;
            if (property.PropertyType == typeof(Color32))
            {
                var color = ReferenceEquals(construct, _colorWheelTarget) &&
                    property.Name == _colorWheelProperty?.Name &&
                    _colorWheelPreview is { } preview
                        ? preview
                        : (Color32)property.GetValue(construct)!;
                var swatch = new UiRect(x + width - 29, y + 3, 18, RowHeight - 6);
                UiCanvas.FillRect(pixels, _width, _height, swatch.X, swatch.Y,
                    swatch.Width, swatch.Height, color);
                UiCanvas.DrawRectangle(pixels, _width, _height, swatch.X, swatch.Y,
                    swatch.Width, swatch.Height, Palette.Border);
                if (!property.IsReadOnly)
                {
                    _colorHits.Add(new ColorHit(swatch, construct, property));
                }
            }
            UiCanvas.DrawText(pixels, _width, _height, x + width / 2, y,
                valueText, valueColor);
            if (!property.IsReadOnly)
            {
                _propertyHits.Add(new RowHit(bounds, construct, property));
            }

            y += RowHeight;
        }
    }

    private void DrawProfiler(IUiDrawTarget pixels)
    {
        var width = System.Math.Min(390, _width - 24);
        var height = System.Math.Min(280, _height - ToolbarHeight - 12);
        if (width <= 0 || height <= 0)
        {
            return;
        }
        var x = _width - width - 12;
        var y = ToolbarHeight + 12;
        DrawPanel(pixels, x, y, width, height, "RUNTIME PROFILER");
        var graph = new UiRect(x + 12, y + 32, width - 24, 70);
        var frameBufferLength = System.Math.Clamp(graph.Width, 1, _visibleFrames.Length);
        var visibleFrameCount = _profiler.CopyRecentFrames(_visibleFrames.AsSpan(0, frameBufferLength));
        if (visibleFrameCount == 0)
        {
            UiCanvas.DrawText(pixels, _width, _height, x + 12, y + 32, "WAITING FOR FRAME DATA", Palette.Muted);
            return;
        }

        UiCanvas.FillRect(pixels, _width, _height, graph.X, graph.Y, graph.Width, graph.Height, Palette.Field);
        UiCanvas.DrawLine(pixels, _width, _height, graph.X, graph.Bottom - 1, graph.Right, graph.Bottom - 1, Palette.Border);
        var maxTime = 16.7;
        for (var index = 0; index < visibleFrameCount; index++)
        {
            maxTime = System.Math.Max(maxTime, _visibleFrames[index].FrameMilliseconds);
        }
        for (var index = 1; index < visibleFrameCount; index++)
        {
            var x0 = graph.X + (index - 1) * (graph.Width - 1) / System.Math.Max(1, visibleFrameCount - 1);
            var x1 = graph.X + index * (graph.Width - 1) / System.Math.Max(1, visibleFrameCount - 1);
            var y0 = graph.Bottom - 2 - (int)((graph.Height - 4) * System.Math.Min(_visibleFrames[index - 1].FrameMilliseconds, maxTime) / maxTime);
            var y1 = graph.Bottom - 2 - (int)((graph.Height - 4) * System.Math.Min(_visibleFrames[index].FrameMilliseconds, maxTime) / maxTime);
            UiCanvas.DrawLine(pixels, _width, _height, x0, y0, x1, y1, Palette.Graph);
        }

        var latest = _visibleFrames[visibleFrameCount - 1];
        UiCanvas.DrawText(pixels, _width, _height, x + 12, y + 108,
            $"FRAME {latest.FrameMilliseconds:0.00} MS   {FrameRate(latest.FrameMilliseconds)} FPS", Palette.Text);
        UiCanvas.DrawText(pixels, _width, _height, x + 12, y + 128,
            $"ALLOC {FormatBytes(latest.AllocatedBytes)}   TRIANGLES {latest.DrawCalls}", Palette.Text);
        UiCanvas.DrawText(pixels, _width, _height, x + 12, y + 148,
            $"GC {latest.Gen0Collections}/{latest.Gen1Collections}/{latest.Gen2Collections}", Palette.Muted);
        var timingY = y + 168;
        foreach (var timing in latest.SystemMilliseconds.OrderByDescending(item => item.Value).Take(4))
        {
            UiCanvas.DrawText(pixels, _width, _height, x + 12, timingY,
                $"{timing.Key.ToUpperInvariant()} {timing.Value:0.00} MS", Palette.Muted);
            timingY += 20;
        }
    }

    private void DrawConsole(IUiDrawTarget pixels)
    {
        var width = System.Math.Min(570, _width - 24);
        var height = System.Math.Min(170, _height - 40);
        if (width <= 0 || height <= 0)
        {
            return;
        }
        var x = (_width - width) / 2;
        var y = _height - height - 20;
        DrawPanel(pixels, x, y, width, height, "DEBUG CONSOLE");
        var output = _consoleOutput.TakeLast(5).ToArray();
        for (var index = 0; index < output.Length; index++)
        {
            UiCanvas.DrawText(pixels, _width, _height, x + 12, y + 28 + index * 20,
                output[index], Palette.Muted);
        }
        UiCanvas.FillRect(pixels, _width, _height, x + 8, y + height - 29, width - 16, 20,
            _focus == UiFocus.Console ? Palette.FieldActive : Palette.Field);
        DrawSelectionHighlight(pixels, UiFocus.Console, _consoleInput, x + 14 + 2 * CharacterAdvance,
            y + height - 29, width - 16 - 2 * CharacterAdvance, 20);
        var consoleCapacity = System.Math.Max(0, (width - 16 - 2 * CharacterAdvance) / CharacterAdvance);
        UiCanvas.DrawText(pixels, _width, _height, x + 14, y + height - 29,
            $"> {GetEditableText(_consoleInput, consoleCapacity, _focus == UiFocus.Console)}",
            Palette.Value);
    }

    private void DrawLogPanel(IUiDrawTarget pixels)
    {
        var height = System.Math.Min(150, _height / 3);
        var y = _height - height;
        DrawPanel(pixels, 0, y, _width, height, "ENGINE LOG");
        var entries = _log.Entries;
        var shown = entries.TakeLast(System.Math.Max(1, (height - 36) / 20)).ToArray();
        var rowY = y + 26;
        foreach (var entry in shown)
        {
            var color = entry.Level switch
            {
                LogLevel.Error => Palette.Error,
                LogLevel.Warning => Palette.Warning,
                _ => Palette.Muted
            };
            UiCanvas.DrawText(pixels, _width, _height, 10, rowY,
                $"{entry.Level.ToString().ToUpperInvariant()} [{entry.Category}] {entry.Message}", color);
            rowY += 20;
        }
    }

    private void DrawPanel(IUiDrawTarget pixels, int x, int y, int width, int height, string title)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        UiCanvas.FillRect(pixels, _width, _height, x, y, width, height, Palette.Panel);
        UiCanvas.DrawRectangle(pixels, _width, _height, x, y, width, height, Palette.Border);
        UiCanvas.FillRect(pixels, _width, _height, x + 1, y + 1, width - 2, 22, Palette.PanelHeader);
        UiCanvas.DrawText(pixels, _width, _height, x + 10, y + 2, title, Palette.Text);
    }

    private void DrawButton(IUiDrawTarget pixels, int x, string text, bool active, Action action)
    {
        var width = ButtonWidth(text);
        var bounds = new UiRect(x, 3, width, 20);
        UiCanvas.FillRect(pixels, _width, _height, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            active ? Palette.ButtonActive : Palette.Toolbar);
        UiCanvas.DrawText(pixels, _width, _height, x + 6, bounds.Y, text, active ? Palette.Accent : Palette.Text);
        _buttonHits.Add(new ButtonHit(bounds, action));
    }

    private static int ButtonWidth(string text) => text.Length * CharacterAdvance + 12;

    private void ExecuteConsoleInput()
    {
        var command = _consoleInput.Trim();
        if (command.Length == 0)
        {
            return;
        }

        AddConsoleOutput($"> {command}");
        try
        {
            var result = _console.Execute(command);
            if (!string.IsNullOrEmpty(result))
            {
                AddConsoleOutput(result);
            }
        }
        catch (Exception exception)
        {
            ReportError(exception.Message);
            AddConsoleOutput($"ERROR: {exception.Message}");
        }
        _consoleInput = string.Empty;
    }

    private void ToggleConsole()
    {
        ConsoleVisible = !ConsoleVisible;
        _focus = ConsoleVisible ? UiFocus.Console : UiFocus.None;
    }

    private void UndoPropertyEdit()
    {
        try
        {
            if (_history.Undo())
            {
                SetStatus("Undid property edit");
            }
        }
        catch (Exception exception)
        {
            ReportError(exception.Message);
        }
    }

    private void RedoPropertyEdit()
    {
        try
        {
            if (_history.Redo())
            {
                SetStatus("Redid property edit");
            }
        }
        catch (Exception exception)
        {
            ReportError(exception.Message);
        }
    }

    private void CommitPropertyEdit()
    {
        if (_editingTarget is null || _editingProperty is null)
        {
            _focus = UiFocus.None;
            return;
        }

        try
        {
            var property = _propertyPanel.GetProperties(_editingTarget)
                .First(candidate => candidate.Name == _editingProperty);
            var value = ParseValue(property.PropertyType, _editBuffer);
            _propertyPanel.SetValue(_editingTarget, _editingProperty, value);
            SetStatus($"Updated {_editingProperty}");
            _editingProperty = null;
            _focus = UiFocus.None;
        }
        catch (Exception exception) when (IsInputError(exception))
        {
            ReportError(exception.Message);
        }
    }

    private static object ParseValue(Type type, string text)
    {
        if (type == typeof(string))
        {
            return text;
        }
        if (type == typeof(Vector3))
        {
            var components = text.Split(',');
            if (components.Length != 3)
            {
                throw new FormatException("Enter a vector as x, y, z.");
            }
            return new Vector3(
                float.Parse(components[0], NumberStyles.Float, CultureInfo.InvariantCulture),
                float.Parse(components[1], NumberStyles.Float, CultureInfo.InvariantCulture),
                float.Parse(components[2], NumberStyles.Float, CultureInfo.InvariantCulture));
        }
        if (type == typeof(Vector2))
        {
            var components = ParseVectorComponents(text, 2);
            return new Vector2(components[0], components[1]);
        }
        if (type == typeof(UIVector2))
        {
            var components = ParseVectorComponents(text, 4);
            return new UIVector2(
                new Vector2(components[0], components[1]),
                new Vector2(components[2], components[3]));
        }
        if (type == typeof(Color32))
        {
            var components = text.Split(',');
            if (components.Length is < 3 or > 4)
            {
                throw new FormatException("Enter a color as red, green, blue[, alpha].");
            }

            var channels = components
                .Select(component => byte.Parse(component.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture))
                .ToArray();
            return new Color32(channels[0], channels[1], channels[2], channels.Length == 4 ? channels[3] : byte.MaxValue);
        }
        if (type.IsEnum)
        {
            return Enum.Parse(type, text, ignoreCase: true);
        }
        return Convert.ChangeType(text, type, CultureInfo.InvariantCulture)
            ?? throw new FormatException($"Could not convert the value to {type.Name}.");
    }

    private static float[] ParseVectorComponents(string text, int expectedCount)
    {
        var components = text.Split(',');
        if (components.Length != expectedCount)
        {
            throw new FormatException(expectedCount == 2
                ? "Enter a vector as x, y."
                : "Enter a UI vector as relativeX, relativeY, offsetX, offsetY.");
        }

        var values = components
            .Select(component => float.Parse(component, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Any(value => !float.IsFinite(value)))
        {
            throw new FormatException("Vector components must be finite numbers.");
        }

        return values;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        Vector3 vector => string.Create(CultureInfo.InvariantCulture, $"{vector.X:0.##}, {vector.Y:0.##}, {vector.Z:0.##}"),
        Vector2 vector => string.Create(CultureInfo.InvariantCulture, $"{vector.X:0.##}, {vector.Y:0.##}"),
        UIVector2 vector => string.Create(
            CultureInfo.InvariantCulture,
            $"{vector.RelativeScale.X:0.##}, {vector.RelativeScale.Y:0.##}, {vector.AbsoluteOffset.X:0.##}, {vector.AbsoluteOffset.Y:0.##}"),
        Color32 color => string.Create(CultureInfo.InvariantCulture, $"{color.R}, {color.G}, {color.B}, {color.A}"),
        bool boolean => boolean ? "TRUE" : "FALSE",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private bool IsOverPanel(int x, int y)
    {
        if (IsVisible && y < ToolbarHeight)
        {
            return true;
        }
        if (IsVisible && ExplorerVisible && x < System.Math.Min(_explorerWidth, _width / 2))
        {
            return true;
        }
        if (IsVisible && PropertiesVisible && x >= _width - System.Math.Min(_propertyWidth, _width / 2))
        {
            return true;
        }
        return IsOverlayPanelHit(x, y);
    }

    private bool IsOverlayPanelHit(int x, int y)
    {
        if (LogVisible && y >= _height - System.Math.Min(150, _height / 3))
        {
            return true;
        }
        if (ConsoleVisible)
        {
            var width = System.Math.Min(570, _width - 24);
            var height = System.Math.Min(170, _height - 40);
            if (x >= (_width - width) / 2 && x < (_width + width) / 2 &&
                y >= _height - height - 20 && y < _height - 20)
            {
                return true;
            }
        }
        if (_profiler.IsVisible)
        {
            var width = System.Math.Min(390, _width - 24);
            var height = System.Math.Min(280, _height - ToolbarHeight - 12);
            if (x >= _width - width - 12 && x < _width - 12 &&
                y >= ToolbarHeight + 12 && y < ToolbarHeight + 12 + height)
            {
                return true;
            }
        }
        return false;
    }

    private bool IsConsoleInputHit(int x, int y)
    {
        var width = System.Math.Min(570, _width - 24);
        var height = System.Math.Min(170, _height - 40);
        var left = (_width - width) / 2 + 8;
        var top = _height - height - 20 + height - 29;
        return x >= left && x < left + width - 16 && y >= top && y < top + 20;
    }

    private string WidthLabel() => $"{_width}X{_height}";
    private static string DisplayName(Construct construct) =>
        string.IsNullOrWhiteSpace(construct.Name) ? construct.GetType().Name : construct.Name;
    private static string FrameRate(double milliseconds) =>
        milliseconds <= 0 ? "--" : (1000 / milliseconds).ToString("0", CultureInfo.InvariantCulture);
    private static string FormatBytes(long bytes) =>
        bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes} B";
    private void AddConsoleOutput(string text)
    {
        _consoleOutput.Enqueue(text);
        while (_consoleOutput.Count > 50)
        {
            _consoleOutput.Dequeue();
        }
    }
    private void SetStatus(string message)
    {
        _status = message;
        _statusTime = DateTimeOffset.UtcNow;
    }
    private void ReportError(string message)
    {
        SetStatus(message);
        _log.Write(LogLevel.Error, "UI", message);
    }
    private static bool IsInputError(Exception exception) =>
        exception is FormatException or ArgumentException or InvalidOperationException or
            OverflowException or KeyNotFoundException;

    private readonly record struct RowHit(UiRect Bounds, Construct? Construct, InspectableProperty? Property);
    private readonly record struct ColorHit(UiRect Bounds, Construct Target, InspectableProperty Property);
    private readonly record struct ButtonHit(UiRect Bounds, Action Action);
    private readonly record struct ContextMenuHit(UiRect Bounds, Action? Action);
    private readonly record struct PropertyRow(string? Group, InspectableProperty? Property);
    private readonly record struct ScreenPoint(float X, float Y);
    private readonly record struct UiResizeDrag(PanelEdge Edge, int StartX);
    private readonly record struct UiTransformDrag(
        UIElement Element,
        TransformGizmoMode Mode,
        int Axis,
        int InitialMouseX,
        int InitialMouseY,
        float CenterX,
        float CenterY,
        float InitialPointerAngle,
        UIVector2 InitialPosition,
        UIVector2 InitialScale,
        int InitialRotation);
    private readonly record struct GizmoDrag(
        Construct Construct,
        ITransformableConstruct Transform,
        TransformGizmoMode Mode,
        int AxisIndex,
        int InitialMouseX,
        int InitialMouseY,
        ScreenPoint ScreenOrigin,
        ScreenPoint ScreenEnd,
        float Size,
        float InitialPointerAngle,
        Vector3 InitialPosition,
        Vector3 InitialRotation,
        Vector3 InitialScale);
    private readonly record struct UiRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
    }
    private enum UiFocus { None, Search, Property, Console }
    private enum PanelEdge { Explorer, Properties }
    private enum NewConstructKind { Entity, Shape2D, TextBox2D, EmptyConstruct }

    private static class Palette
    {
        public static readonly Color32 Toolbar = new(25, 30, 35);
        public static readonly Color32 Panel = new(32, 36, 40);
        public static readonly Color32 PanelHeader = new(40, 45, 50);
        public static readonly Color32 Group = new(38, 43, 47);
        public static readonly Color32 Border = new(155, 143, 105);
        public static readonly Color32 Field = new(24, 28, 31);
        public static readonly Color32 FieldActive = new(35, 48, 54);
        public static readonly Color32 TextSelection = new(68, 96, 116);
        public static readonly Color32 ButtonActive = new(38, 52, 56);
        public static readonly Color32 Accent = new(132, 206, 184);
        public static readonly Color32 Text = new(224, 225, 214);
        public static readonly Color32 Muted = new(145, 153, 151);
        public static readonly Color32 Value = new(227, 181, 133);
        public static readonly Color32 Warning = new(244, 194, 119);
        public static readonly Color32 Error = new(244, 117, 117);
        public static readonly Color32 Graph = new(132, 206, 184);
    }
}

internal static class UiCanvas
{
    internal const int CharacterAdvance = 12;

    public static void FillRect(int[] pixels, int width, int height, int x, int y, int rectWidth, int rectHeight, Color32 color)
    {
        if (rectWidth <= 0 || rectHeight <= 0)
        {
            return;
        }

        var left = System.Math.Clamp(x, 0, width);
        var top = System.Math.Clamp(y, 0, height);
        var right = System.Math.Clamp(x + rectWidth, 0, width);
        var bottom = System.Math.Clamp(y + rectHeight, 0, height);
        var pixel = ToPixel(color);
        if (color.A == byte.MaxValue)
        {
            for (var row = top; row < bottom; row++)
            {
                Array.Fill(pixels, pixel, row * width + left, right - left);
            }

            return;
        }

        for (var row = top; row < bottom; row++)
        {
            for (var column = left; column < right; column++)
            {
                pixels[row * width + column] = Blend(pixel, pixels[row * width + column]);
            }
        }
    }

    public static void DrawRectangle(int[] pixels, int width, int height, int x, int y, int rectWidth, int rectHeight, Color32 color)
    {
        if (rectWidth <= 0 || rectHeight <= 0)
        {
            return;
        }

        DrawLine(pixels, width, height, x, y, x + rectWidth - 1, y, color);
        DrawLine(pixels, width, height, x + rectWidth - 1, y, x + rectWidth - 1, y + rectHeight - 1, color);
        DrawLine(pixels, width, height, x + rectWidth - 1, y + rectHeight - 1, x, y + rectHeight - 1, color);
        DrawLine(pixels, width, height, x, y + rectHeight - 1, x, y, color);
    }

    public static void DrawLine(int[] pixels, int width, int height, int x0, int y0, int x1, int y1, Color32 color)
    {
        var dx = System.Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -System.Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        var pixel = ToPixel(color);
        while (true)
        {
            if ((uint)x0 < (uint)width && (uint)y0 < (uint)height)
            {
                pixels[y0 * width + x0] = pixel;
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }
            var doubledError = 2 * error;
            if (doubledError >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (doubledError <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    public static void DrawText(int[] pixels, int width, int height, int x, int y, string text, Color32 color)
    {
        DrawText(new SoftwareUiDrawTarget(pixels, width, height), x, y, text, color);
    }

    public static void DrawText(IUiDrawTarget target, int x, int y, string text, Color32 color)
    {
        DrawText(target, x, y, text, color, 1f);
    }

    public static void DrawText(IUiDrawTarget target, int x, int y, string text, Color32 color, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Text scale must be finite and positive.");
        }

        if (target is UiDrawList drawList)
        {
            drawList.DrawGlyphs(x, y, text, color, scale);
            return;
        }

        var cursor = (float)x;
        var glyphs = UiFont.GetGlyphs();
        foreach (var character in text)
        {
            if (glyphs.TryGetValue(char.ToUpperInvariant(character), out var glyph))
            {
                for (var row = 0; row < glyph.Height; row++)
                {
                    for (var column = 0; column < glyph.Width; column++)
                    {
                        var coverage = glyph.Coverage[row * glyph.Width + column];
                        if (coverage == 0)
                        {
                            continue;
                        }

                        var left = (int)MathF.Floor(cursor + column * scale);
                        var right = (int)MathF.Ceiling(cursor + (column + 1) * scale);
                        var top = y + (int)MathF.Floor(row * scale);
                        var bottom = y + (int)MathF.Ceiling((row + 1) * scale);
                        target.FillRect(
                            left,
                            top - UiFont.VerticalOffset,
                            System.Math.Max(1, right - left),
                            System.Math.Max(1, bottom - top),
                            color with { A = (byte)((color.A * coverage + 127) / byte.MaxValue) });
                    }
                }
            }
            cursor += CharacterAdvance * scale;
            if (cursor >= target.Width)
            {
                break;
            }
        }
    }

    public static void FillRect(IUiDrawTarget target, int x, int y, int rectWidth, int rectHeight, Color32 color) =>
        target.FillRect(x, y, rectWidth, rectHeight, color);

    public static void FillRect(IUiDrawTarget target, int width, int height, int x, int y, int rectWidth, int rectHeight, Color32 color) =>
        target.FillRect(x, y, rectWidth, rectHeight, color);

    public static void DrawRectangle(IUiDrawTarget target, int x, int y, int rectWidth, int rectHeight, Color32 color) =>
        target.DrawRectangle(x, y, rectWidth, rectHeight, color);

    public static void DrawRectangle(IUiDrawTarget target, int width, int height, int x, int y, int rectWidth, int rectHeight, Color32 color) =>
        target.DrawRectangle(x, y, rectWidth, rectHeight, color);

    public static void DrawLine(IUiDrawTarget target, int x0, int y0, int x1, int y1, Color32 color) =>
        target.DrawLine(x0, y0, x1, y1, color);

    public static void DrawLine(IUiDrawTarget target, int width, int height, int x0, int y0, int x1, int y1, Color32 color) =>
        target.DrawLine(x0, y0, x1, y1, color);

    public static void DrawText(IUiDrawTarget target, int width, int height, int x, int y, string text, Color32 color) =>
        DrawText(target, x, y, text, color);

    public static string Truncate(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
        {
            return value;
        }
        if (maxCharacters <= 0)
        {
            return string.Empty;
        }
        if (maxCharacters <= 3)
        {
            return value[..maxCharacters];
        }

        return value[..(maxCharacters - 3)] + "...";
    }

    private static int ToPixel(Color32 color) => color.A << 24 | color.R << 16 | color.G << 8 | color.B;

    private static int Blend(int source, int destination)
    {
        var alpha = (source >>> 24) & 0xFF;
        var inverseAlpha = 255 - alpha;
        var red = (((source >> 16) & 0xFF) * alpha + ((destination >> 16) & 0xFF) * inverseAlpha + 127) / 255;
        var green = (((source >> 8) & 0xFF) * alpha + ((destination >> 8) & 0xFF) * inverseAlpha + 127) / 255;
        var blue = ((source & 0xFF) * alpha + (destination & 0xFF) * inverseAlpha + 127) / 255;
        var destinationAlpha = (destination >>> 24) & 0xFF;
        var outputAlpha = alpha + (destinationAlpha * inverseAlpha + 127) / 255;
        return outputAlpha << 24 | red << 16 | green << 8 | blue;
    }

    internal static int BlendPixel(int source, int destination) => Blend(source, destination);
}

internal interface IUiDrawTarget
{
    int Width { get; }
    int Height { get; }
    void PushClipRect(int x, int y, int width, int height);
    void PopClipRect();
    void PushRotation(float centerX, float centerY, float radians);
    void PopRotation();
    void FillRect(int x, int y, int width, int height, Color32 color);
    void DrawRectangle(int x, int y, int width, int height, Color32 color);
    void DrawLine(int x0, int y0, int x1, int y1, Color32 color);
    void DrawText(int x, int y, string text, Color32 color);
    void DrawText(int x, int y, string text, Color32 color, float scale);
}

internal readonly record struct UiRotation(float CenterX, float CenterY, float Cos, float Sin);
internal readonly record struct UiPoint(float X, float Y);

internal sealed class SoftwareUiDrawTarget(int[] pixels, int width, int height) : IUiDrawTarget
{
    private readonly List<UiClip> _clips = [];
    private readonly List<UiRotation> _rotations = [];
    public int Width { get; } = width;
    public int Height { get; } = height;

    public void PushClipRect(int x, int y, int rectWidth, int rectHeight) =>
        _clips.Add(CurrentClip.Intersect(new UiClip(x, y, rectWidth, rectHeight)));

    public void PopClipRect()
    {
        if (_clips.Count == 0)
        {
            throw new InvalidOperationException("There is no UI clip region to pop.");
        }
        _clips.RemoveAt(_clips.Count - 1);
    }

    public void PushRotation(float centerX, float centerY, float radians) =>
        _rotations.Add(new UiRotation(centerX, centerY, MathF.Cos(radians), MathF.Sin(radians)));

    public void PopRotation()
    {
        if (_rotations.Count == 0)
        {
            throw new InvalidOperationException("There is no UI rotation to pop.");
        }
        _rotations.RemoveAt(_rotations.Count - 1);
    }

    public void FillRect(int x, int y, int rectWidth, int rectHeight, Color32 color)
    {
        var clipped = CurrentClip.Intersect(new UiClip(x, y, rectWidth, rectHeight));
        if (_rotations.Count == 0)
        {
            UiCanvas.FillRect(pixels, Width, Height, clipped.X, clipped.Y, clipped.Width, clipped.Height, color);
            return;
        }

        var topLeft = Transform(clipped.X, clipped.Y);
        var topRight = Transform(clipped.Right, clipped.Y);
        var bottomRight = Transform(clipped.Right, clipped.Bottom);
        var bottomLeft = Transform(clipped.X, clipped.Bottom);
        FillPolygon([topLeft, topRight, bottomRight, bottomLeft], color);
    }

    public void DrawRectangle(int x, int y, int rectWidth, int rectHeight, Color32 color)
    {
        var clipped = CurrentClip.Intersect(new UiClip(x, y, rectWidth, rectHeight));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        DrawLine(clipped.X, clipped.Y, clipped.Right - 1, clipped.Y, color);
        DrawLine(clipped.X, clipped.Bottom - 1, clipped.Right - 1, clipped.Bottom - 1, color);
        DrawLine(clipped.X, clipped.Y + 1, clipped.X, clipped.Bottom - 2, color);
        DrawLine(clipped.Right - 1, clipped.Y + 1, clipped.Right - 1, clipped.Bottom - 2, color);
    }

    public void DrawLine(int x0, int y0, int x1, int y1, Color32 color)
    {
        if (_rotations.Count > 0)
        {
            var start = Transform(x0 + 0.5f, y0 + 0.5f);
            var end = Transform(x1 + 0.5f, y1 + 0.5f);
            x0 = (int)MathF.Round(start.X);
            y0 = (int)MathF.Round(start.Y);
            x1 = (int)MathF.Round(end.X);
            y1 = (int)MathF.Round(end.Y);
        }
        UiCanvas.DrawLine(pixels, Width, Height, x0, y0, x1, y1, color);
    }

    public void DrawText(int x, int y, string text, Color32 color) =>
        UiCanvas.DrawText(this, x, y, text, color);
    public void DrawText(int x, int y, string text, Color32 color, float scale) =>
        UiCanvas.DrawText(this, x, y, text, color, scale);

    private UiPoint Transform(float x, float y)
    {
        for (var index = _rotations.Count - 1; index >= 0; index--)
        {
            var rotation = _rotations[index];
            var dx = x - rotation.CenterX;
            var dy = y - rotation.CenterY;
            x = rotation.CenterX + dx * rotation.Cos - dy * rotation.Sin;
            y = rotation.CenterY + dx * rotation.Sin + dy * rotation.Cos;
        }
        return new UiPoint(x, y);
    }

    private void FillPolygon(UiPoint[] points, Color32 color)
    {
        var minX = System.Math.Max(0, (int)MathF.Floor(points.Min(point => point.X)));
        var maxX = System.Math.Min(Width - 1, (int)MathF.Ceiling(points.Max(point => point.X)));
        var minY = System.Math.Max(0, (int)MathF.Floor(points.Min(point => point.Y)));
        var maxY = System.Math.Min(Height - 1, (int)MathF.Ceiling(points.Max(point => point.Y)));
        var pixel = color.A << 24 | color.R << 16 | color.G << 8 | color.B;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (!PointInPolygon(x + 0.5f, y + 0.5f, points))
                {
                    continue;
                }

                var index = y * Width + x;
                pixels[index] = color.A == byte.MaxValue
                    ? pixel
                    : UiCanvas.BlendPixel(pixel, pixels[index]);
            }
        }
    }

    private static bool PointInPolygon(float x, float y, IReadOnlyList<UiPoint> points)
    {
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var a = points[i];
            var b = points[j];
            if ((a.Y > y) != (b.Y > y) &&
                x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private UiClip CurrentClip => _clips.Count == 0
        ? new UiClip(0, 0, Width, Height)
        : _clips[^1].Intersect(new UiClip(0, 0, Width, Height));

    private readonly record struct UiClip(int X, int Y, int Width, int Height)
    {
        public int Right => (int)System.Math.Clamp((long)X + Width, int.MinValue, int.MaxValue);
        public int Bottom => (int)System.Math.Clamp((long)Y + Height, int.MinValue, int.MaxValue);

        public UiClip Intersect(UiClip other)
        {
            var x = System.Math.Max(X, other.X);
            var y = System.Math.Max(Y, other.Y);
            var right = System.Math.Min(Right, other.Right);
            var bottom = System.Math.Min(Bottom, other.Bottom);
            return new UiClip(x, y, System.Math.Max(0, right - x), System.Math.Max(0, bottom - y));
        }
    }
}

internal readonly record struct UiVertex(
    float X,
    float Y,
    float Z,
    float R,
    float G,
    float B,
    float A,
    float U = 0,
    float V = 0,
    float Glyph = 0);

internal sealed class UiDrawList : IUiDrawTarget
{
    private readonly List<UiVertex> _vertices = [];
    private readonly List<UiClip> _clips = [];
    private readonly List<UiRotation> _rotations = [];

    public UiDrawList(int width, int height) => Prepare(width, height);

    public int Width { get; private set; }
    public int Height { get; private set; }
    public IReadOnlyList<UiVertex> Vertices => _vertices;

    public void Prepare(int width, int height)
    {
        Width = width;
        Height = height;
        _vertices.Clear();
        _clips.Clear();
        _rotations.Clear();
    }

    public void PushClipRect(int x, int y, int rectWidth, int rectHeight) =>
        _clips.Add(CurrentClip.Intersect(new UiClip(x, y, rectWidth, rectHeight)));

    public void PopClipRect()
    {
        if (_clips.Count == 0)
        {
            throw new InvalidOperationException("There is no UI clip region to pop.");
        }
        _clips.RemoveAt(_clips.Count - 1);
    }

    public void PushRotation(float centerX, float centerY, float radians) =>
        _rotations.Add(new UiRotation(centerX, centerY, MathF.Cos(radians), MathF.Sin(radians)));

    public void PopRotation()
    {
        if (_rotations.Count == 0)
        {
            throw new InvalidOperationException("There is no UI rotation to pop.");
        }
        _rotations.RemoveAt(_rotations.Count - 1);
    }

    public void FillRect(int x, int y, int rectWidth, int rectHeight, Color32 color)
    {
        if (rectWidth <= 0 || rectHeight <= 0)
        {
            return;
        }

        var clip = CurrentClip;
        var left = System.Math.Max(x, clip.X);
        var top = System.Math.Max(y, clip.Y);
        var right = System.Math.Min((int)System.Math.Clamp((long)x + rectWidth, int.MinValue, int.MaxValue), clip.Right);
        var bottom = System.Math.Min((int)System.Math.Clamp((long)y + rectHeight, int.MinValue, int.MaxValue), clip.Bottom);
        if (left >= right || top >= bottom)
        {
            return;
        }

        AddQuad(left, top, right, bottom, color);
    }

    public void DrawRectangle(int x, int y, int rectWidth, int rectHeight, Color32 color)
    {
        if (rectWidth <= 0 || rectHeight <= 0)
        {
            return;
        }

        if (rectWidth == 1 || rectHeight == 1)
        {
            FillRect(x, y, rectWidth, rectHeight, color);
            return;
        }

        FillRect(x, y, rectWidth, 1, color);
        FillRect(x, y + rectHeight - 1, rectWidth, 1, color);
        FillRect(x, y + 1, 1, rectHeight - 2, color);
        FillRect(x + rectWidth - 1, y + 1, 1, rectHeight - 2, color);
    }

    public void DrawLine(int x0, int y0, int x1, int y1, Color32 color)
    {
        if (y0 == y1)
        {
            FillRect(System.Math.Min(x0, x1), y0, System.Math.Abs(x1 - x0) + 1, 1, color);
            return;
        }

        if (x0 == x1)
        {
            FillRect(x0, System.Math.Min(y0, y1), 1, System.Math.Abs(y1 - y0) + 1, color);
            return;
        }

        var dx = x1 - x0;
        var dy = y1 - y0;
        var inverseLength = 0.5f / MathF.Sqrt(dx * dx + dy * dy);
        var offsetX = -dy * inverseLength;
        var offsetY = dx * inverseLength;
        AddQuad(
            x0 + offsetX, y0 + offsetY,
            x1 + offsetX, y1 + offsetY,
            x1 - offsetX, y1 - offsetY,
            x0 - offsetX, y0 - offsetY,
            color);
    }

    public void DrawText(int x, int y, string text, Color32 color) =>
        DrawGlyphs(x, y, text, color);

    public void DrawText(int x, int y, string text, Color32 color, float scale) =>
        DrawGlyphs(x, y, text, color, scale);

    public void DrawGlyphs(int x, int y, string text, Color32 color, float scale = 1)
    {
        var cursor = (float)x;
        foreach (var character in text)
        {
            if (UiFont.TryGetGlyphUv(char.ToUpperInvariant(character),
                    out var u0, out var v0, out var u1, out var v1))
            {
                AddGlyphQuad(
                    cursor,
                    y - UiFont.VerticalOffset,
                    cursor + UiCanvas.CharacterAdvance * scale,
                    y - UiFont.VerticalOffset + 20 * scale,
                    u0,
                    v0,
                    u1,
                    v1,
                    color);
            }

            cursor += UiCanvas.CharacterAdvance * scale;
            if (cursor >= Width)
            {
                break;
            }
        }
    }

    private void AddQuad(float left, float top, float right, float bottom, Color32 color)
    {
        AddQuad(
            left, top,
            right, top,
            right, bottom,
            left, bottom,
            color);
    }

    private void AddQuad(
        float x0,
        float y0,
        float x1,
        float y1,
        float x2,
        float y2,
        float x3,
        float y3,
        Color32 color)
    {
        (x0, y0) = Transform(x0, y0);
        (x1, y1) = Transform(x1, y1);
        (x2, y2) = Transform(x2, y2);
        (x3, y3) = Transform(x3, y3);
        var vertex = new UiVertex(0, 0, 0, color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        var first = vertex with { X = x0, Y = y0 };
        var second = vertex with { X = x1, Y = y1 };
        var third = vertex with { X = x2, Y = y2 };
        var fourth = vertex with { X = x3, Y = y3 };
        _vertices.Add(first);
        _vertices.Add(second);
        _vertices.Add(third);
        _vertices.Add(first);
        _vertices.Add(third);
        _vertices.Add(fourth);
    }

    private void AddGlyphQuad(
        float left,
        float top,
        float right,
        float bottom,
        float u0,
        float v0,
        float u1,
        float v1,
        Color32 color)
    {
        var originalWidth = right - left;
        var originalHeight = bottom - top;
        var clip = CurrentClip;
        var clippedLeft = System.Math.Max(left, clip.X);
        var clippedTop = System.Math.Max(top, clip.Y);
        var clippedRight = System.Math.Min(right, clip.Right);
        var clippedBottom = System.Math.Min(bottom, clip.Bottom);
        if (clippedLeft >= clippedRight || clippedTop >= clippedBottom)
        {
            return;
        }

        var uWidth = u1 - u0;
        var vHeight = v1 - v0;
        var clippedU0 = u0 + (clippedLeft - left) / originalWidth * uWidth;
        var clippedV0 = v0 + (clippedTop - top) / originalHeight * vHeight;
        var clippedU1 = u1 - (right - clippedRight) / originalWidth * uWidth;
        var clippedV1 = v1 - (bottom - clippedBottom) / originalHeight * vHeight;
        var topLeft = Transform(left, top);
        var topRight = Transform(right, top);
        var bottomRight = Transform(right, bottom);
        var bottomLeft = Transform(left, bottom);
        left = clippedLeft;
        top = clippedTop;
        right = clippedRight;
        bottom = clippedBottom;
        u0 = clippedU0;
        v0 = clippedV0;
        u1 = clippedU1;
        v1 = clippedV1;
        var vertex = new UiVertex(
            0, 0, 0,
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f,
            Glyph: 1);
        var first = vertex with { X = topLeft.X, Y = topLeft.Y, U = u0, V = v0 };
        var second = vertex with { X = topRight.X, Y = topRight.Y, U = u1, V = v0 };
        var third = vertex with { X = bottomRight.X, Y = bottomRight.Y, U = u1, V = v1 };
        var fourth = vertex with { X = bottomLeft.X, Y = bottomLeft.Y, U = u0, V = v1 };
        _vertices.Add(first);
        _vertices.Add(second);
        _vertices.Add(third);
        _vertices.Add(first);
        _vertices.Add(third);
        _vertices.Add(fourth);
    }

    private UiClip CurrentClip => _clips.Count == 0
        ? new UiClip(0, 0, Width, Height)
        : _clips[^1].Intersect(new UiClip(0, 0, Width, Height));

    private (float X, float Y) Transform(float x, float y)
    {
        for (var index = _rotations.Count - 1; index >= 0; index--)
        {
            var rotation = _rotations[index];
            var dx = x - rotation.CenterX;
            var dy = y - rotation.CenterY;
            x = rotation.CenterX + dx * rotation.Cos - dy * rotation.Sin;
            y = rotation.CenterY + dx * rotation.Sin + dy * rotation.Cos;
        }
        return (x, y);
    }

    private readonly record struct UiClip(int X, int Y, int Width, int Height)
    {
        public int Right => (int)System.Math.Clamp((long)X + Width, int.MinValue, int.MaxValue);
        public int Bottom => (int)System.Math.Clamp((long)Y + Height, int.MinValue, int.MaxValue);

        public UiClip Intersect(UiClip other)
        {
            var x = System.Math.Max(X, other.X);
            var y = System.Math.Max(Y, other.Y);
            var right = System.Math.Min(Right, other.Right);
            var bottom = System.Math.Min(Bottom, other.Bottom);
            return new UiClip(x, y, System.Math.Max(0, right - x), System.Math.Max(0, bottom - y));
        }
    }
}
