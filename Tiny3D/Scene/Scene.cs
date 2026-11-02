using Tiny3D.Graphics;
using Tiny3D.UI;

namespace Tiny3D.Scene;

/// <summary>
/// Represents the root scene graph and manages construct lifecycle, root ordering, and
/// scene-wide updates for a Tiny3D application.
/// </summary>
public sealed class Scene : IDisposable
{
    private readonly List<Construct> _roots = [];
    private readonly HashSet<Construct> _queuedForFree = [];
    private IReadOnlyList<Entity>? _entities;
    private Construct[]? _orderedRoots;
    private int _iterationDepth;

    public Scene() => UI = new UiApi(this);

    public IReadOnlyList<Construct> Roots => _roots;
    public IReadOnlyList<Entity> Entities => _entities ??= Traverse().OfType<Entity>().ToArray();
    public Lighting Lighting { get; } = new();
    /// <summary>Screen-space UI elements attached to this scene.</summary>
    public UiApi UI { get; }

    public void Add(Entity entity)
        => Add((Construct)entity);

    /// <summary>Adds a construct as a scene root.</summary>
    public void Add(Construct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        ObjectDisposedException.ThrowIf(construct.IsDisposed, construct);
        if (construct.OwningScene is not null && !ReferenceEquals(construct.OwningScene, this))
        {
            throw new InvalidOperationException("A construct already belongs to another scene.");
        }

        if (construct.Branch is not null)
        {
            construct.Branch.RemoveDescendant(construct);
        }

        if (_roots.Contains(construct))
        {
            return;
        }

        _roots.Add(construct);
        InvalidateRootOrder();
        InvalidateEntityCache();
        Attach(construct);
    }

    public bool Remove(Construct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        if (!_roots.Remove(construct))
        {
            return false;
        }

        InvalidateRootOrder();
        Detach(construct);
        InvalidateEntityCache();
        return true;
    }

    /// <summary>Runs one variable-step update over the scene tree.</summary>
    public void Update(float deltaSeconds)
    {
        ValidateDelta(deltaSeconds);
        _iterationDepth++;
        try
        {
            foreach (var root in OrderedRoots)
            {
                if (root.Branch is null && ReferenceEquals(root.OwningScene, this))
                {
                    root.UpdateTree(deltaSeconds, fixedUpdate: false);
                }
            }
        }
        finally
        {
            _iterationDepth--;
        }

        if (!IsIterating)
        {
            FlushQueuedFree();
        }
    }

    /// <summary>Runs one fixed-step update over the scene tree.</summary>
    public void FixedUpdate()
    {
        _iterationDepth++;
        try
        {
            foreach (var root in OrderedRoots)
            {
                if (root.Branch is null && ReferenceEquals(root.OwningScene, this))
                {
                    root.UpdateTree(0, fixedUpdate: true);
                }
            }
        }
        finally
        {
            _iterationDepth--;
        }

        if (!IsIterating)
        {
            FlushQueuedFree();
        }
    }

    internal void Attach(Construct construct)
    {
        InvalidateEntityCache();
        construct.OwningScene = this;
        construct.EnterTree();
    }

    internal void Detach(Construct construct)
    {
        InvalidateEntityCache();
        construct.ExitTree();
        DetachSceneReferences(construct);
    }

    internal void RemoveRoot(Construct construct)
    {
        if (_roots.Remove(construct))
        {
            InvalidateRootOrder();
            InvalidateEntityCache();
        }
    }

    internal void InvalidateEntityCache() => _entities = null;
    internal void InvalidateRootOrder() => _orderedRoots = null;
    internal bool IsIterating => _iterationDepth > 0;

    internal void QueueFree(Construct construct) => _queuedForFree.Add(construct);

    private Construct[] OrderedRoots =>
        _orderedRoots ??= _roots.OrderBy(item => item.UpdatePriority).ToArray();

    public void Dispose()
    {
        if (IsIterating)
        {
            throw new InvalidOperationException("A scene cannot be disposed during an update.");
        }

        foreach (var root in _roots.ToArray())
        {
            root.ExitTree();
            DetachSceneReferences(root);
            root.DisposeTree();
        }

        _roots.Clear();
        _queuedForFree.Clear();
        InvalidateRootOrder();
        InvalidateEntityCache();
    }

    private IEnumerable<Construct> Traverse()
    {
        foreach (var root in _roots)
        {
            foreach (var construct in Traverse(root))
            {
                yield return construct;
            }
        }
    }

    private static IEnumerable<Construct> Traverse(Construct construct)
    {
        yield return construct;
        foreach (var child in construct.Descendants.OrderBy(item => item.UpdatePriority))
        {
            foreach (var descendant in Traverse(child))
            {
                yield return descendant;
            }
        }
    }

    private void FlushQueuedFree()
    {
        foreach (var construct in _queuedForFree.ToArray())
        {
            if (construct.Branch is not null)
            {
                construct.Branch.RemoveDescendant(construct);
            }
            else
            {
                construct.OwningScene?.Remove(construct);
            }

            construct.DisposeTree();
        }

        _queuedForFree.Clear();
    }

    private static void ValidateDelta(float deltaSeconds)
    {
        if (deltaSeconds < 0 || !float.IsFinite(deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }
    }

    private static void DetachSceneReferences(Construct construct)
    {
        construct.OwningScene = null;
        foreach (var child in construct.Descendants)
        {
            DetachSceneReferences(child);
        }
    }
}
