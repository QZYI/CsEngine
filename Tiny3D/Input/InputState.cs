namespace Tiny3D.Input;

/// <summary>Read-only keyboard and mouse state exposed to game updates.</summary>
public sealed class InputState
{
    private readonly HashSet<Key> _down = [];
    private readonly HashSet<Key> _pressed = [];
    private readonly HashSet<Key> _released = [];
    private readonly HashSet<Key> _consumedDown = [];
    private readonly List<char> _textInput = [];
    private int _mouseDeltaX;
    private int _mouseDeltaY;
    private bool _rightMouseButtonDown;
    private bool _middleMouseButtonDown;
    private bool _rightMouseConsumed;
    private bool _middleMouseConsumed;

    /// <summary>Returns whether a key is currently down and has not been consumed by the editor.</summary>
    public bool IsKeyDown(Key key) => _down.Contains(key) && !_consumedDown.Contains(key);
    /// <summary>Returns whether a key was pressed during the current input frame.</summary>
    public bool WasKeyPressed(Key key) => _pressed.Contains(key);
    /// <summary>Returns whether a key was released during the current input frame.</summary>
    public bool WasKeyReleased(Key key) => _released.Contains(key);
    public int MouseX { get; private set; }
    public int MouseY { get; private set; }
    public bool IsLeftMouseButtonDown { get; private set; }
    public bool WasLeftMouseButtonPressed { get; private set; }
    public bool WasLeftMouseButtonReleased { get; private set; }
    public bool IsRightMouseButtonDown => _rightMouseButtonDown && !_rightMouseConsumed;
    public bool WasRightMouseButtonPressed { get; private set; }
    public bool IsMiddleMouseButtonDown => _middleMouseButtonDown && !_middleMouseConsumed;
    public int MouseDeltaX => _mouseDeltaX;
    public int MouseDeltaY => _mouseDeltaY;
    public int MouseWheelDelta { get; private set; }

    internal void EndUpdate()
    {
        _pressed.Clear();
        _released.Clear();
        WasLeftMouseButtonPressed = false;
        WasLeftMouseButtonReleased = false;
        WasRightMouseButtonPressed = false;
        _mouseDeltaX = 0;
        _mouseDeltaY = 0;
        MouseWheelDelta = 0;
        _textInput.Clear();
        _rightMouseConsumed = false;
        _middleMouseConsumed = false;
    }

    internal IReadOnlyList<char> TextInput => _textInput;
    internal IEnumerable<Key> PressedKeys => _pressed;
    internal IEnumerable<Key> ReleasedKeys => _released;

    internal void ConsumeKeyPress(Key key) => _pressed.Remove(key);
    internal void ConsumeKeyDown(Key key) => _consumedDown.Add(key);
    internal void ConsumeCameraInput()
    {
        ConsumeCameraKey(Key.W);
        ConsumeCameraKey(Key.A);
        ConsumeCameraKey(Key.S);
        ConsumeCameraKey(Key.D);
        ConsumeCameraKey(Key.Q);
        ConsumeCameraKey(Key.E);
        ConsumeCameraKey(Key.Up);
        ConsumeCameraKey(Key.Down);
        ConsumeCameraKey(Key.Left);
        ConsumeCameraKey(Key.Right);
        _rightMouseConsumed = true;
        _middleMouseConsumed = true;
    }

    private void ConsumeCameraKey(Key key)
    {
        if (_down.Contains(key))
        {
            _consumedDown.Add(key);
        }
    }
    internal void CompleteUiUpdate()
    {
        WasLeftMouseButtonPressed = false;
        WasLeftMouseButtonReleased = false;
        WasRightMouseButtonPressed = false;
        _textInput.Clear();
        _rightMouseConsumed = false;
        _middleMouseConsumed = false;
    }

    internal void SetKeyDown(int virtualKey)
    {
        var key = (Key)virtualKey;
        if (Enum.IsDefined(key) && _down.Add(key))
        {
            _pressed.Add(key);
        }
    }

    internal void SetKeyUp(int virtualKey)
    {
        var key = (Key)virtualKey;
        if (Enum.IsDefined(key) && _down.Remove(key))
        {
            _released.Add(key);
            _consumedDown.Remove(key);
        }
    }

    internal void SetMousePosition(int x, int y)
    {
        if (MouseX >= 0 && MouseY >= 0)
        {
            _mouseDeltaX += x - MouseX;
            _mouseDeltaY += y - MouseY;
        }

        MouseX = x;
        MouseY = y;
    }

    internal void AddMouseWheelDelta(int delta) => MouseWheelDelta += delta;

    internal void SetLeftMouseButtonDown(bool isDown)
    {
        if (isDown && !IsLeftMouseButtonDown)
        {
            WasLeftMouseButtonPressed = true;
        }
        else if (!isDown && IsLeftMouseButtonDown)
        {
            WasLeftMouseButtonReleased = true;
        }

        IsLeftMouseButtonDown = isDown;
    }

    internal void SetRightMouseButtonDown(bool isDown)
    {
        if (isDown && !_rightMouseButtonDown)
        {
            WasRightMouseButtonPressed = true;
            _mouseDeltaX = 0;
            _mouseDeltaY = 0;
        }

        _rightMouseButtonDown = isDown;
    }

    internal void SetMiddleMouseButtonDown(bool isDown)
    {
        if (isDown && !_middleMouseButtonDown)
        {
            _mouseDeltaX = 0;
            _mouseDeltaY = 0;
        }

        _middleMouseButtonDown = isDown;
    }

    internal void AddTextInput(char value)
    {
        if (!char.IsControl(value))
        {
            _textInput.Add(value);
        }
    }
}
