using Tiny3D.Math;
using Tiny3D.Tools;

namespace Tiny3D.Scene;

/// <summary>
/// A metadata-bearing scene-graph container. Transformable and renderable behavior is
/// provided by specialized constructs such as entities and cameras.
/// </summary>
public abstract class Construct : IDisposable
{
    private readonly List<Construct> _descendants = [];
    private Construct[]? _orderedDescendants;
    private bool _ready;
    private bool _disposed;
    private bool _queuedForFree;
    private int _updatePriority;
    private long _transformRevision;

    public Construct? Branch { get; private set; }
    public IReadOnlyList<Construct> Descendants => _descendants;
    public Scene? OwningScene { get; internal set; }
    [Inspect("Construct")]
    public string Name { get; set; } = string.Empty;
    internal virtual bool IsNameReadOnly => false;
    [Inspect("Construct")]
    public bool Visible { get; set; } = true;
    public int UpdatePriority
    {
        get => _updatePriority;
        set
        {
            if (_updatePriority == value)
            {
                return;
            }

            _updatePriority = value;
            if (Branch is { } branch)
            {
                branch.InvalidateOrderedDescendants();
            }
            else
            {
                OwningScene?.InvalidateRootOrder();
            }
        }
    }
    public MetadataStacks Metadata { get; } = new();
    public bool IsQueuedForFree => _queuedForFree;
    public bool IsDisposed => _disposed;

    /// <summary>Parents a construct under this node and attaches it to this node's scene, if any.</summary>
    public void AddDescendant(Construct descendant)
    {
        ArgumentNullException.ThrowIfNull(descendant);
        ThrowIfDisposed();
        descendant.ThrowIfDisposed();

        if (ReferenceEquals(descendant, this) || IsAncestorOf(descendant, this))
        {
            throw new InvalidOperationException("A construct cannot be parented to itself or one of its descendants.");
        }

        if (descendant.OwningScene is not null && OwningScene is not null &&
            !ReferenceEquals(descendant.OwningScene, OwningScene))
        {
            throw new InvalidOperationException("A construct already belongs to another scene.");
        }

        if (ReferenceEquals(descendant.Branch, this))
        {
            return;
        }

        var sameScene = OwningScene is not null && ReferenceEquals(descendant.OwningScene, OwningScene);
        if (sameScene)
        {
            descendant.Branch?._descendants.Remove(descendant);
            descendant.Branch?.InvalidateOrderedDescendants();
            OwningScene!.RemoveRoot(descendant);
            descendant.Branch = this;
            _descendants.Add(descendant);
            InvalidateOrderedDescendants();
            descendant.MarkTransformChanged();
            OwningScene.InvalidateEntityCache();
            return;
        }

        if (descendant.Branch is not null)
        {
            descendant.Branch.RemoveDescendant(descendant);
        }
        else
        {
            descendant.OwningScene?.Remove(descendant);
        }

        descendant.Branch = this;
        _descendants.Add(descendant);
        InvalidateOrderedDescendants();
        descendant.MarkTransformChanged();

        if (OwningScene is not null)
        {
            OwningScene.Attach(descendant);
        }
    }

    public bool RemoveDescendant(Construct descendant)
    {
        ArgumentNullException.ThrowIfNull(descendant);
        if (!ReferenceEquals(descendant.Branch, this) || !_descendants.Remove(descendant))
        {
            return false;
        }

        InvalidateOrderedDescendants();
        if (OwningScene is not null)
        {
            OwningScene.Detach(descendant);
        }

        descendant.Branch = null;
        descendant.MarkTransformChanged();
        return true;
    }

    public void Rebranch(Construct? newBranch)
    {
        if (newBranch is null)
        {
            if (Branch is not null)
            {
                Branch.RemoveDescendant(this);
            }
            else
            {
                OwningScene?.Remove(this);
            }

            return;
        }

        newBranch.AddDescendant(this);
    }

    /// <summary>Queues this construct for disposal after the current scene iteration.</summary>
    public void QueueFree()
    {
        ThrowIfDisposed();
        if (_queuedForFree)
        {
            return;
        }

        _queuedForFree = true;
        if (OwningScene is null)
        {
            Dispose();
        }
        else
        {
            OwningScene.QueueFree(this);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (OwningScene?.IsIterating == true)
        {
            QueueFree();
            return;
        }

        if (Branch is not null)
        {
            Branch.RemoveDescendant(this);
        }
        else
        {
            OwningScene?.Remove(this);
        }

        DisposeTree();
        GC.SuppressFinalize(this);
    }

    internal Vector3 TransformPoint(Vector3 point)
    {
        var transformed = this is ITransformableConstruct transformable
            ? transformable.Pivot.Apply(point)
            : point;
        return Branch is null ? transformed : Branch.TransformPoint(transformed);
    }

    internal bool TryInverseTransformPoint(Vector3 point, out Vector3 localPoint)
    {
        if (Branch is not null)
        {
            if (!Branch.TryInverseTransformPoint(point, out point))
            {
                localPoint = Vector3.Zero;
                return false;
            }
        }

        if (this is not ITransformableConstruct transformable)
        {
            localPoint = point;
            return true;
        }

        var pivot = transformable.Pivot;
        point -= pivot.Position;
        var cosZ = MathF.Cos(pivot.Rotation.Z);
        var sinZ = MathF.Sin(pivot.Rotation.Z);
        point = new Vector3(
            point.X * cosZ + point.Y * sinZ,
            -point.X * sinZ + point.Y * cosZ,
            point.Z);

        var cosY = MathF.Cos(pivot.Rotation.Y);
        var sinY = MathF.Sin(pivot.Rotation.Y);
        point = new Vector3(
            point.X * cosY - point.Z * sinY,
            point.Y,
            point.X * sinY + point.Z * cosY);

        var cosX = MathF.Cos(pivot.Rotation.X);
        var sinX = MathF.Sin(pivot.Rotation.X);
        point = new Vector3(
            point.X,
            point.Y * cosX + point.Z * sinX,
            -point.Y * sinX + point.Z * cosX);

        if (MathF.Abs(pivot.Scale.X) < 0.000001f ||
            MathF.Abs(pivot.Scale.Y) < 0.000001f ||
            MathF.Abs(pivot.Scale.Z) < 0.000001f)
        {
            localPoint = Vector3.Zero;
            return false;
        }

        localPoint = new Vector3(
            point.X / pivot.Scale.X,
            point.Y / pivot.Scale.Y,
            point.Z / pivot.Scale.Z);
        return true;
    }

    internal long TransformRevision => _transformRevision;

    /// <summary>Creates a detached copy of this construct and its descendants.</summary>
    public Construct DuplicateTree()
    {
        ThrowIfDisposed();
        var duplicate = CreateDuplicate();
        if (ReferenceEquals(duplicate, this) || duplicate.IsDisposed ||
            duplicate.OwningScene is not null || duplicate.Branch is not null)
        {
            throw new InvalidOperationException("CreateDuplicate must return a new, detached construct.");
        }

        duplicate.Name = Name;
        duplicate.Visible = Visible;
        duplicate.UpdatePriority = UpdatePriority;
        if (this is ITransformableConstruct sourceTransform &&
            duplicate is ITransformableConstruct duplicateTransform)
        {
            duplicateTransform.Pivot.Position = sourceTransform.Pivot.Position;
            duplicateTransform.Pivot.Rotation = sourceTransform.Pivot.Rotation;
            duplicateTransform.Pivot.Scale = sourceTransform.Pivot.Scale;
        }
        else if (this is ITransformableConstruct || duplicate is ITransformableConstruct)
        {
            throw new InvalidOperationException("A duplicate must preserve whether its construct is transformable.");
        }
        try
        {
            foreach (var child in _descendants)
            {
                duplicate.AddDescendant(child.DuplicateTree());
            }

            return duplicate;
        }
        catch
        {
            duplicate.Dispose();
            throw;
        }
    }

    /// <summary>Creates the type-specific detached copy used by <see cref="DuplicateTree"/>.</summary>
    protected virtual Construct CreateDuplicate() =>
        throw new NotSupportedException($"Construct type '{GetType().Name}' does not support duplication.");

    protected void MarkTransformChanged()
    {
        _transformRevision++;
        foreach (var child in _descendants)
        {
            child.MarkTransformChanged();
        }
    }

    internal bool IsVisibleInTree =>
        Visible && (Branch?.IsVisibleInTree ?? true);

    internal void UpdateTree(float deltaSeconds, bool fixedUpdate)
    {
        if (_disposed || _queuedForFree)
        {
            return;
        }

        if (fixedUpdate)
        {
            FixedUpdate();
        }
        else
        {
            Update(deltaSeconds);
        }

        foreach (var child in OrderedDescendants())
        {
            if (ReferenceEquals(child.Branch, this) && ReferenceEquals(child.OwningScene, OwningScene))
            {
                child.UpdateTree(deltaSeconds, fixedUpdate);
            }
        }
    }

    internal void EnterTree()
    {
        foreach (var child in _descendants)
        {
            child.OwningScene = OwningScene;
        }

        OnEnterBranch();
        if (!_ready)
        {
            _ready = true;
            Ready();
        }

        foreach (var child in OrderedDescendants())
        {
            child.EnterTree();
        }
    }

    internal void ExitTree()
    {
        foreach (var child in OrderedDescendants())
        {
            if (ReferenceEquals(child.Branch, this))
            {
                child.ExitTree();
            }
        }

        OnExitBranch();
    }

    internal void DisposeTree()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var child in _descendants.ToArray())
        {
            child.DisposeTree();
            child.Branch = null;
        }

        InvalidateOrderedDescendants();
        _descendants.Clear();
        OnDispose();
        OwningScene = null;
        Branch = null;
    }

    protected virtual void OnEnterBranch() { }
    protected virtual void Ready() { }
    protected virtual void Update(float deltaSeconds) { }
    protected virtual void FixedUpdate() { }
    protected virtual void OnExitBranch() { }
    protected virtual void OnDispose() { }

    internal void InvalidateOrderedDescendants() => _orderedDescendants = null;

    private Construct[] OrderedDescendants() =>
        _orderedDescendants ??= _descendants.OrderBy(descendant => descendant.UpdatePriority).ToArray();

    private static bool IsAncestorOf(Construct potentialAncestor, Construct construct)
    {
        for (var current = construct.Branch; current is not null; current = current.Branch)
        {
            if (ReferenceEquals(current, potentialAncestor))
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
