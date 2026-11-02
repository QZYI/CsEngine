using System.Runtime.InteropServices;
using Tiny3D.Input;

namespace Tiny3D.Platform;

internal sealed class WindowsWindow : IDisposable
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint PmRemove = 0x0001;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmMiddleButtonDown = 0x0207;
    private const uint WmMiddleButtonUp = 0x0208;
    private const uint WmMouseWheel = 0x020A;

    private static readonly WindowProcedureDelegate WindowProcedure = WindowProc;
    private static WindowsWindow? _activeWindow;

    private readonly string _className = $"Tiny3DWindow{Guid.NewGuid():N}";
    private int _inputWidth;
    private int _inputHeight;
    private int _clientWidth;
    private int _clientHeight;
    private IntPtr _handle;
    private bool _running = true;

    public WindowsWindow(string title, int width, int height)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Tiny3D's built-in window host currently requires Windows.");
        }

        Input = new InputState();
        _activeWindow = this;
        _inputWidth = width;
        _inputHeight = height;

        var instance = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = WindowProcedure,
            Instance = instance,
            ClassName = _className
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"Could not register the game window (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        var style = WsOverlappedWindow;
        var bounds = new Rect { Right = width, Bottom = height };
        if (!AdjustWindowRect(ref bounds, style, false))
        {
            throw new InvalidOperationException("Could not calculate the game window size.");
        }

        _handle = CreateWindowEx(
            0, _className, title, style, 100, 100,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Could not create the game window (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        ShowWindow(_handle, 5);
        GetClientSize(out var clientWidth, out var clientHeight);
        _clientWidth = clientWidth;
        _clientHeight = clientHeight;
    }

    public InputState Input { get; }
    internal IntPtr Handle => _handle;

    public void SetInputSize(int width, int height, int clientWidth, int clientHeight)
    {
        if (width <= 0 || height <= 0 || clientWidth <= 0 || clientHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Input dimensions must be positive.");
        }

        _inputWidth = width;
        _inputHeight = height;
        _clientWidth = clientWidth;
        _clientHeight = clientHeight;
    }

    public void GetClientSize(out int width, out int height)
    {
        if (!GetClientRect(_handle, out var clientArea))
        {
            throw new InvalidOperationException($"Could not determine the game window's client size (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        width = clientArea.Right - clientArea.Left;
        height = clientArea.Bottom - clientArea.Top;
    }

    public bool PumpEvents()
    {
        while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PmRemove))
        {
            if (message.MessageId == 0x0012)
            {
                _running = false;
                break;
            }

            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        return _running;
    }

    private static IntPtr WindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        var window = _activeWindow;
        if (window is null)
        {
            return DefWindowProc(handle, message, wParam, lParam);
        }

        switch (message)
        {
            case WmClose:
                window._running = false;
                DestroyWindow(handle);
                return IntPtr.Zero;
            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
            case WmPaint:
                window.Paint(handle);
                return IntPtr.Zero;
            case WmEraseBackground:
                return new IntPtr(1);
            case WmKeyDown:
                window.Input.SetKeyDown(wParam.ToInt32());
                return IntPtr.Zero;
            case WmKeyUp:
                window.Input.SetKeyUp(wParam.ToInt32());
                return IntPtr.Zero;
            case WmChar:
                window.Input.AddTextInput((char)wParam.ToInt32());
                return IntPtr.Zero;
            case WmMouseMove:
                window.SetMousePosition(lParam);
                return IntPtr.Zero;
            case WmLeftButtonDown:
                window.SetMousePosition(lParam);
                window.Input.SetLeftMouseButtonDown(true);
                SetCapture(handle);
                return IntPtr.Zero;
            case WmLeftButtonUp:
                window.SetMousePosition(lParam);
                window.Input.SetLeftMouseButtonDown(false);
                window.ReleaseMouseCaptureIfNeeded();
                return IntPtr.Zero;
            case WmRightButtonDown:
                window.SetMousePosition(lParam);
                window.Input.SetRightMouseButtonDown(true);
                SetCapture(handle);
                return IntPtr.Zero;
            case WmRightButtonUp:
                window.SetMousePosition(lParam);
                window.Input.SetRightMouseButtonDown(false);
                window.ReleaseMouseCaptureIfNeeded();
                return IntPtr.Zero;
            case WmMiddleButtonDown:
                window.SetMousePosition(lParam);
                window.Input.SetMiddleMouseButtonDown(true);
                SetCapture(handle);
                return IntPtr.Zero;
            case WmMiddleButtonUp:
                window.SetMousePosition(lParam);
                window.Input.SetMiddleMouseButtonDown(false);
                window.ReleaseMouseCaptureIfNeeded();
                return IntPtr.Zero;
            case WmMouseWheel:
                window.Input.AddMouseWheelDelta(unchecked((short)(wParam.ToInt64() >> 16)));
                return IntPtr.Zero;
            default:
                return DefWindowProc(handle, message, wParam, lParam);
        }
    }

    private void ReleaseMouseCaptureIfNeeded()
    {
        if (!Input.IsLeftMouseButtonDown &&
            !Input.IsRightMouseButtonDown &&
            !Input.IsMiddleMouseButtonDown)
        {
            ReleaseCapture();
        }
    }

    private void SetMousePosition(IntPtr lParam)
    {
        var packedPosition = lParam.ToInt32();
        var clientX = unchecked((short)packedPosition);
        var clientY = unchecked((short)(packedPosition >> 16));
        if (_clientWidth <= 0 || _clientHeight <= 0)
        {
            Input.SetMousePosition(-1, -1);
            return;
        }

        Input.SetMousePosition(
            (int)(clientX * (_inputWidth / (float)_clientWidth)),
            (int)(clientY * (_inputHeight / (float)_clientHeight)));
    }

    private void Paint(IntPtr handle)
    {
        var deviceContext = BeginPaint(handle, out var paint);
        if (deviceContext == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Could not begin painting the game window (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        if (!EndPaint(handle, ref paint))
        {
            throw new InvalidOperationException($"Could not finish painting the game window (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }

        if (_activeWindow == this)
        {
            _activeWindow = null;
        }
    }

    private delegate IntPtr WindowProcedureDelegate(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WindowProcedureDelegate Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint MessageId;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr DeviceContext;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Erase;
        public Rect PaintArea;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Restore;
        [MarshalAs(UnmanagedType.Bool)]
        public bool IncrementalUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRect(ref Rect rectangle, uint style, [MarshalAs(UnmanagedType.Bool)] bool hasMenu);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr handle, out Rect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr BeginPaint(IntPtr handle, out PaintStruct paint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr handle, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, IntPtr handle, uint minimum, uint maximum, uint removeMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

}
