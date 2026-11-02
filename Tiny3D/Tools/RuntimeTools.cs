using Tiny3D.Graphics;
using Tiny3D.Math;
using Tiny3D.Scene;
using SceneGraph = Tiny3D.Scene.Scene;

namespace Tiny3D.Tools;

/// <summary>Severity levels accepted by the engine log.</summary>
public enum LogLevel
{
    Trace,
    Information,
    Warning,
    Error
}

/// <summary>A timestamped engine log message.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message);

/// <summary>Thread-safe bounded log for runtime and editor diagnostics.</summary>
public sealed class EngineLog
{
    private readonly Queue<LogEntry> _entries = new();
    private readonly object _gate = new();
    private readonly int _capacity;

    public EngineLog(int capacity = 1000)
    {
        _capacity = capacity > 0
            ? capacity
            : throw new ArgumentOutOfRangeException(nameof(capacity));
    }

    public event Action<LogEntry>? EntryWritten;
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>Appends a log entry and notifies listeners.</summary>
    public void Write(LogLevel level, string category, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(message);
        var entry = new LogEntry(DateTimeOffset.UtcNow, level, category, message);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }

        EntryWritten?.Invoke(entry);
    }
}

/// <summary>Registry and executor for named console commands and string variables.</summary>
public sealed class DebugConsole
{
    private readonly Dictionary<string, Action<IReadOnlyList<string>>> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a command that receives its parsed arguments.</summary>
    public void RegisterCommand(string name, Action<IReadOnlyList<string>> command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(command);
        if (!_commands.TryAdd(name, command))
        {
            throw new InvalidOperationException($"Console command '{name}' is already registered.");
        }
    }

    public void SetVariable(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _variables[name] = value;
    }

    public bool TryGetVariable(string name, out string value) =>
        _variables.TryGetValue(name, out value!);

    /// <summary>Executes a command or reads/sets a registered variable.</summary>
    public string Execute(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var parts = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (_commands.TryGetValue(parts[0], out var command))
        {
            command(parts.Skip(1).ToArray());
            return string.Empty;
        }

        if (parts.Length == 1 && _variables.TryGetValue(parts[0], out var currentValue))
        {
            return $"{parts[0]} = {currentValue}";
        }

        if (parts.Length == 2 && _variables.ContainsKey(parts[0]))
        {
            _variables[parts[0]] = parts[1];
            return $"{parts[0]} = {parts[1]}";
        }

        throw new KeyNotFoundException($"Unknown console command or variable '{parts[0]}'.");
    }
}

/// <summary>Per-frame timing, allocation, garbage-collection, and draw-call measurements.</summary>
public sealed record FrameProfile(
    double FrameMilliseconds,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int DrawCalls,
    IReadOnlyDictionary<string, double> SystemMilliseconds);

/// <summary>Stores a bounded history of runtime frame profiles.</summary>
public sealed class RuntimeProfiler
{
    private readonly Queue<FrameProfile> _frames = new();
    private readonly int _historyCapacity;

    public RuntimeProfiler(int historyCapacity = 300)
    {
        _historyCapacity = historyCapacity > 0
            ? historyCapacity
            : throw new ArgumentOutOfRangeException(nameof(historyCapacity));
    }

    public bool IsVisible { get; set; }
    public IReadOnlyList<FrameProfile> Frames => _frames.ToArray();
    public FrameProfile? LatestFrame => _frames.LastOrDefault();

    internal int CopyRecentFrames(Span<FrameProfile> destination)
    {
        var skippedFrames = System.Math.Max(0, _frames.Count - destination.Length);
        var copiedFrames = 0;
        foreach (var frame in _frames)
        {
            if (skippedFrames > 0)
            {
                skippedFrames--;
                continue;
            }

            destination[copiedFrames++] = frame;
        }

        return copiedFrames;
    }

    /// <summary>Records a completed frame and trims history to the configured capacity.</summary>
    public void RecordFrame(
        double frameMilliseconds,
        long allocatedBytes,
        int[] collectionCounts,
        int drawCalls,
        IReadOnlyDictionary<string, double> systemMilliseconds)
    {
        var profile = new FrameProfile(
            frameMilliseconds,
            allocatedBytes,
            collectionCounts[0], collectionCounts[1], collectionCounts[2],
            drawCalls,
            new Dictionary<string, double>(systemMilliseconds, StringComparer.Ordinal));
        _frames.Enqueue(profile);
        while (_frames.Count > _historyCapacity)
        {
            _frames.Dequeue();
        }
    }
}

/// <summary>A world-space debug line segment.</summary>
public readonly record struct DebugLine(Vector3 Start, Vector3 End, Color32 Color);
/// <summary>A world-space debug text label.</summary>
public readonly record struct DebugText(Vector3 Position, string Text, Color32 Color);

/// <summary>Collects world-space lines, boxes, and text for rendering as debug overlays.</summary>
public sealed class DebugDraw
{
    private readonly List<DebugLine> _lines = [];
    private readonly List<DebugText> _texts = [];

    public IReadOnlyList<DebugLine> Lines => _lines;
    public IReadOnlyList<DebugText> Texts => _texts;

    public void Line(Vector3 start, Vector3 end, Color32 color) =>
        _lines.Add(new DebugLine(start, end, color));

    public void Box(Vector3 minimum, Vector3 maximum, Color32 color)
    {
        var corners = new[]
        {
            new Vector3(minimum.X, minimum.Y, minimum.Z), new Vector3(maximum.X, minimum.Y, minimum.Z),
            new Vector3(maximum.X, maximum.Y, minimum.Z), new Vector3(minimum.X, maximum.Y, minimum.Z),
            new Vector3(minimum.X, minimum.Y, maximum.Z), new Vector3(maximum.X, minimum.Y, maximum.Z),
            new Vector3(maximum.X, maximum.Y, maximum.Z), new Vector3(minimum.X, maximum.Y, maximum.Z)
        };
        var edges = new[] { (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4), (0, 4), (1, 5), (2, 6), (3, 7) };
        foreach (var (start, end) in edges)
        {
            Line(corners[start], corners[end], color);
        }
    }

    public void Text(Vector3 position, string text, Color32 color)
    {
        ArgumentNullException.ThrowIfNull(text);
        _texts.Add(new DebugText(position, text, color));
    }

    public void Clear()
    {
        _lines.Clear();
        _texts.Clear();
    }
}

/// <summary>Reuses detached constructs to reduce repeated construction and disposal overhead.</summary>
public sealed class ConstructPool<TConstruct>
    where TConstruct : Construct
{
    private readonly Stack<TConstruct> _available = [];
    private readonly Func<TConstruct> _factory;
    private readonly Action<TConstruct>? _onRent;
    private readonly Action<TConstruct>? _onReturn;
    private readonly int _capacity;

    public ConstructPool(
        Func<TConstruct> factory,
        Action<TConstruct>? onRent = null,
        Action<TConstruct>? onReturn = null,
        int capacity = 128)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _onRent = onRent;
        _onReturn = onReturn;
        _capacity = capacity >= 0
            ? capacity
            : throw new ArgumentOutOfRangeException(nameof(capacity));
    }

    public int AvailableCount => _available.Count;

    /// <summary>Rents a pooled construct or creates one with the configured factory.</summary>
    public TConstruct Rent()
    {
        var construct = _available.TryPop(out var pooled)
            ? pooled
            : _factory() ?? throw new InvalidOperationException("The construct pool factory returned null.");
        _onRent?.Invoke(construct);
        return construct;
    }

    /// <summary>Returns a detached live construct to the pool, disposing it if the pool is full.</summary>
    public void Return(TConstruct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        if (construct.IsDisposed || construct.OwningScene is not null || construct.Branch is not null)
        {
            throw new InvalidOperationException("A pooled construct must be detached and not disposed.");
        }

        _onReturn?.Invoke(construct);
        if (_available.Count < _capacity)
        {
            _available.Push(construct);
        }
        else
        {
            construct.Dispose();
        }
    }
}

/// <summary>Discovers inspectable construct/pivot properties and applies edits through undo history.</summary>
public sealed class PropertyPanel(UndoHistory history)
{
    public IReadOnlyList<InspectableProperty> GetProperties(Construct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        var properties = InspectionRegistry.GetProperties(construct.GetType())
            .Select(property =>
                construct.IsNameReadOnly && property.Name == nameof(Construct.Name)
                    ? property.AsReadOnly()
                    : property)
            .ToList();
        if (construct is ITransformableConstruct transformable)
        {
            properties.AddRange(
                InspectionRegistry.GetProperties(transformable.Pivot.GetType())
                    .Select(property => property.Bind($"Pivot.{property.Name}", _ => transformable.Pivot)));
        }
        return properties;
    }

    public void SetValue(Construct construct, string propertyName, object? value)
    {
        ArgumentNullException.ThrowIfNull(construct);
        var property = GetProperties(construct).FirstOrDefault(candidate => candidate.Name == propertyName)
            ?? throw new KeyNotFoundException($"Inspectable property '{propertyName}' was not found.");
        history.Execute(new PropertyEditCommand(construct, property, value));
    }
}

/// <summary>Manages scene-tree selection, search, service discovery, and reparenting.</summary>
public sealed class ConstructExplorer(SceneGraph scene)
{
    private readonly List<Construct> _services = [];

    public Construct? Selected { get; private set; }
    public Construct? Hovered { get; private set; }
    public IReadOnlyList<Construct> Services => _services;

    public void AddService(Construct service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.OwningScene is not null && !ReferenceEquals(service.OwningScene, scene))
        {
            throw new InvalidOperationException("The service does not belong to this scene.");
        }

        if (!_services.Contains(service))
        {
            _services.Add(service);
        }
    }

    public bool RemoveService(Construct service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return _services.Remove(service);
    }

    internal bool Contains(Construct? construct) =>
        construct is not null &&
        (ReferenceEquals(construct.OwningScene, scene) || _services.Contains(construct));

    /// <summary>Finds constructs and services whose names contain the query.</summary>
    public IReadOnlyList<Construct> Search(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return scene.Roots
            .Concat(_services)
            .SelectMany(Flatten)
            .Where(construct => construct.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public void Select(Construct? construct)
    {
        EnsureInScene(construct);
        Selected = construct;
    }

    public void SetHovered(Construct? construct)
    {
        EnsureInScene(construct);
        Hovered = construct;
    }

    /// <summary>Moves a construct under another construct or back to the scene root.</summary>
    public void Reparent(Construct construct, Construct? parent)
    {
        EnsureInScene(construct);
        if (parent is null)
        {
            if (_services.Contains(construct))
            {
                RemoveService(construct);
                scene.Add(construct);
                return;
            }

            construct.Branch?.RemoveDescendant(construct);
            scene.Add(construct);
        }
        else
        {
            EnsureInScene(parent);
            if (_services.Contains(construct))
            {
                RemoveService(construct);
            }
            parent.AddDescendant(construct);
        }
    }

    private void EnsureInScene(Construct? construct)
    {
        if (construct is not null && !ReferenceEquals(construct.OwningScene, scene) && !_services.Contains(construct))
        {
            throw new InvalidOperationException("The construct does not belong to this scene.");
        }
    }

    private static IEnumerable<Construct> Flatten(Construct construct)
    {
        yield return construct;
        foreach (var child in construct.Descendants)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>Watches a directory recursively and reports file changes and watcher errors.</summary>
public sealed class AssetWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    public AssetWatcher(string directory, string filter = "*.*")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Asset directory '{directory}' does not exist.");
        }

        _watcher = new FileSystemWatcher(directory, filter)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        _watcher.Changed += (_, args) => AssetChanged?.Invoke(args.FullPath);
        _watcher.Created += (_, args) => AssetChanged?.Invoke(args.FullPath);
        _watcher.Renamed += (_, args) => AssetChanged?.Invoke(args.FullPath);
        _watcher.Error += (_, args) => WatcherError?.Invoke(args.GetException());
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Raised on the file-watcher thread when an asset is created, changed, or renamed.</summary>
    public event Action<string>? AssetChanged;
    /// <summary>Raised when the underlying file watcher encounters an error.</summary>
    public event Action<Exception>? WatcherError;
    public void Dispose() => _watcher.Dispose();
}
