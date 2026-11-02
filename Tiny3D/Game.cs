using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Tiny3D.Graphics;
using Tiny3D.Input;
using Tiny3D.Platform;
using Tiny3D.Scene;
using Tiny3D.Tools;
using GameScene = Tiny3D.Scene.Scene;

namespace Tiny3D;

/// <summary>
/// Base class for a Tiny3D application. Derive from this type to define the scene,
/// gameplay loop, and runtime/editor integration for a project.
/// </summary>
public abstract class Game
{
    private const double FixedStepSeconds = 1.0 / 60.0;
    private WindowsWindow? _window;
    private bool _exitRequested;
    private bool _initialized;
    private readonly ConcurrentQueue<(string Path, Action<string> Reload)> _pendingAssetReloads = new();
    private readonly List<AssetWatcher> _assetWatchers = [];

    protected Game(
        int width = 1920,
        int height = 1080,
        string title = "Tiny3D Game",
        EngineSettings? settings = null)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be positive.");
        }

        Width = width;
        Height = height;
        Title = title;
        Settings = (settings ?? EngineSettings.LoadDefault()).Validate();
        Console.SetVariable("fps_max", Settings.FramerateCap.ToString(CultureInfo.InvariantCulture));
        CameraNode = new CameraConstruct(Camera);
        Scene.Add(CameraNode);
        Editor = new EngineUi(
            Scene, Profiler, Log, Console, DebugDraw, UndoHistory, width, height, Settings.FontResolution);
    }

    public int Width { get; }
    public int Height { get; }
    public string Title { get; }
    public EngineSettings Settings { get; }
    public GameScene Scene { get; } = new();
    public Lighting Lighting => Scene.Lighting;
    public Camera Camera { get; } = new();
    public CameraConstruct CameraNode { get; }
    public RuntimeProfiler Profiler { get; } = new();
    public EngineLog Log { get; } = new();
    public DebugConsole Console { get; } = new();
    public DebugDraw DebugDraw { get; } = new();
    public UndoHistory UndoHistory { get; } = new();
    public EngineUi Editor { get; }
    private readonly InputState _headlessInput = new();
    protected InputState Input => _window?.Input ?? _headlessInput;

    /// <summary>Starts the Windows windowed game loop and Direct3D renderer.</summary>
    public void Run()
    {
        using var window = new WindowsWindow(Title, Width, Height);
        _window = window;
        try
        {
            RunWindow(window);
        }
        finally
        {
            _window = null;
            foreach (var watcher in _assetWatchers)
            {
                watcher.Dispose();
            }
            _assetWatchers.Clear();
        }
    }

    private void RunWindow(WindowsWindow window)
    {
        window.GetClientSize(out var initialClientWidth, out var initialClientHeight);
        using var renderer = new Direct3D11Renderer(
            window.Handle,
            Width,
            Height,
            initialClientWidth > 0 ? initialClientWidth : Width,
            initialClientHeight > 0 ? initialClientHeight : Height,
            Settings.MemoryAllocation,
            Settings.SSAAStrength);
        renderer.SetPickContext(Scene, Camera, Width, Height);
        var clock = Stopwatch.StartNew();
        var previousTime = clock.Elapsed.TotalSeconds;
        var accumulator = 0.0;
        var collectionCounts = new int[3];
        var collectionDeltas = new int[3];
        var systemTimes = new Dictionary<string, double>(StringComparer.Ordinal);
        InitializeOnce();

        while (!_exitRequested && window.PumpEvents())
        {
            DrainAssetReloads();
            var frameStart = clock.Elapsed.TotalSeconds;
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            collectionCounts[0] = GC.CollectionCount(0);
            collectionCounts[1] = GC.CollectionCount(1);
            collectionCounts[2] = GC.CollectionCount(2);
            systemTimes.Clear();
            var currentTime = clock.Elapsed.TotalSeconds;
            var frameDelta = System.Math.Min(currentTime - previousTime, 0.25);
            accumulator += frameDelta;
            previousTime = currentTime;
            window.GetClientSize(out var clientWidth, out var clientHeight);
            if (clientWidth > 0 && clientHeight > 0)
            {
                GetRenderSize(clientWidth, clientHeight, out var renderWidth, out var renderHeight);
                renderer.SetSize(renderWidth, renderHeight, clientWidth, clientHeight);
                renderer.SetPickContext(Scene, Camera, renderWidth, renderHeight);
                Editor.SetSize(renderWidth, renderHeight);
                window.SetInputSize(renderWidth, renderHeight, clientWidth, clientHeight);
                Scene.UI.Update(window.Input, renderWidth, renderHeight);
            }
            else
            {
                Scene.UI.Update(window.Input, Width, Height);
            }
            Editor.Update(window.Input, renderer, Camera);

            while (accumulator >= FixedStepSeconds)
            {
                var fixedStart = clock.Elapsed.TotalSeconds;
                Scene.FixedUpdate();
                AddSystemTime(systemTimes, "Fixed Update", clock.Elapsed.TotalSeconds - fixedStart);
                var updateStart = clock.Elapsed.TotalSeconds;
                Update((float)FixedStepSeconds);
                AddSystemTime(systemTimes, "Game Update", clock.Elapsed.TotalSeconds - updateStart);
                window.Input.EndUpdate();
                accumulator -= FixedStepSeconds;
            }
            window.Input.CompleteUiUpdate();

            var sceneUpdateStart = clock.Elapsed.TotalSeconds;
            Scene.Update((float)frameDelta);
            CameraNode.SyncCamera();
            AddSystemTime(systemTimes, "Scene Update", clock.Elapsed.TotalSeconds - sceneUpdateStart);
            var renderStart = clock.Elapsed.TotalSeconds;
            if (clientWidth > 0 && clientHeight > 0)
            {
                renderer.Render(
                    Scene,
                    Camera,
                    Editor.Explorer.Selected,
                    Editor.Explorer.Hovered,
                    DebugDraw,
                    Editor.BuildDrawList(),
                    Settings.DitherFade ? 1f : 0f,
                    Editor.GizmoMode);
            }
            AddSystemTime(systemTimes, "Render", clock.Elapsed.TotalSeconds - renderStart);
            collectionDeltas[0] = GC.CollectionCount(0) - collectionCounts[0];
            collectionDeltas[1] = GC.CollectionCount(1) - collectionCounts[1];
            collectionDeltas[2] = GC.CollectionCount(2) - collectionCounts[2];
            var remainingFrameTime = GetFrameLimitSeconds() - (clock.Elapsed.TotalSeconds - frameStart);
            if (remainingFrameTime > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(remainingFrameTime));
            }
            Profiler.RecordFrame(
                (clock.Elapsed.TotalSeconds - frameStart) * 1000,
                GC.GetAllocatedBytesForCurrentThread() - allocationStart,
                collectionDeltas,
                renderer.LastDrawCallCount,
                systemTimes);
        }
    }

    /// <summary>Runs game and scene updates without creating a window or graphics device.</summary>
    /// <param name="frameCount">Number of frames to simulate.</param>
    /// <param name="deltaSeconds">Positive time step used for each simulated frame.</param>
    public void RunHeadless(int frameCount, float deltaSeconds = (float)FixedStepSeconds)
    {
        if (frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        if (deltaSeconds <= 0 || !float.IsFinite(deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        InitializeOnce();
        var collectionCounts = new int[3];
        var collectionDeltas = new int[3];
        var systemTimes = new Dictionary<string, double>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        for (var frame = 0; frame < frameCount && !_exitRequested; frame++)
        {
            DrainAssetReloads();
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            collectionCounts[0] = GC.CollectionCount(0);
            collectionCounts[1] = GC.CollectionCount(1);
            collectionCounts[2] = GC.CollectionCount(2);
            systemTimes.Clear();
            stopwatch.Restart();

            var sceneUpdateStart = stopwatch.Elapsed.TotalSeconds;
            Scene.Update(deltaSeconds);
            AddSystemTime(systemTimes, "Scene Update", stopwatch.Elapsed.TotalSeconds - sceneUpdateStart);

            var fixedUpdateStart = stopwatch.Elapsed.TotalSeconds;
            Scene.FixedUpdate();
            AddSystemTime(systemTimes, "Fixed Update", stopwatch.Elapsed.TotalSeconds - fixedUpdateStart);

            var gameUpdateStart = stopwatch.Elapsed.TotalSeconds;
            Scene.UI.Update(_headlessInput, Width, Height);
            Update(deltaSeconds);
            CameraNode.SyncCamera();
            AddSystemTime(systemTimes, "Game Update", stopwatch.Elapsed.TotalSeconds - gameUpdateStart);
            _headlessInput.EndUpdate();

            collectionDeltas[0] = GC.CollectionCount(0) - collectionCounts[0];
            collectionDeltas[1] = GC.CollectionCount(1) - collectionCounts[1];
            collectionDeltas[2] = GC.CollectionCount(2) - collectionCounts[2];
            Profiler.RecordFrame(
                stopwatch.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocationStart,
                collectionDeltas,
                0,
                systemTimes);
        }
    }

    protected virtual void Initialize()
    {
    }

    protected abstract void Update(float deltaSeconds);

    protected void Exit() => _exitRequested = true;

    public AssetWatcher WatchAssets(string directory, Action<string> reloadAsset)
    {
        ArgumentNullException.ThrowIfNull(reloadAsset);
        var watcher = new AssetWatcher(directory);
        watcher.AssetChanged += path => _pendingAssetReloads.Enqueue((path, reloadAsset));
        watcher.WatcherError += exception =>
            Log.Write(LogLevel.Error, "HotReload", exception.Message);
        _assetWatchers.Add(watcher);
        return watcher;
    }

    private void InitializeOnce()
    {
        if (_initialized)
        {
            throw new InvalidOperationException("A game instance can only be run once.");
        }

        _initialized = true;
        Initialize();
    }

    private static void AddSystemTime(Dictionary<string, double> timings, string name, double elapsedSeconds)
    {
        timings.TryGetValue(name, out var current);
        timings[name] = current + elapsedSeconds * 1000;
    }

    private void GetRenderSize(int clientWidth, int clientHeight, out int renderWidth, out int renderHeight)
    {
        var scale = System.Math.Min(
            1f,
            System.Math.Min(Width / (float)clientWidth, Height / (float)clientHeight));
        renderWidth = System.Math.Max(1, (int)System.MathF.Round(clientWidth * scale));
        renderHeight = System.Math.Max(1, (int)System.MathF.Round(clientHeight * scale));
    }

    private double GetFrameLimitSeconds()
    {
        if (!Console.TryGetVariable("fps_max", out var value) ||
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var framesPerSecond) ||
            framesPerSecond < 0)
        {
            throw new FormatException("The fps_max console variable must be a non-negative integer.");
        }

        return framesPerSecond == 0 ? 0 : 1.0 / framesPerSecond;
    }

    private void DrainAssetReloads()
    {
        while (_pendingAssetReloads.TryDequeue(out var pending))
        {
            try
            {
                pending.Reload(pending.Path);
            }
            catch (Exception exception)
            {
                Log.Write(LogLevel.Error, "HotReload",
                    $"Failed to reload asset '{pending.Path}': {exception.Message}");
            }
        }
    }
}
