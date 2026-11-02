using Tiny3D.Scene;
using SceneGraph = Tiny3D.Scene.Scene;

namespace Tiny3D.Tools;

/// <summary>A reversible command stored by <see cref="UndoHistory"/>.</summary>
public interface IUndoableCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

/// <summary>Bounded undo and redo stacks for editor operations.</summary>
public sealed class UndoHistory(int capacity = 100)
{
    private readonly Stack<IUndoableCommand> _undo = [];
    private readonly Stack<IUndoableCommand> _redo = [];

    public int Capacity { get; } = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity));
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
        if (_undo.Count > Capacity)
        {
            var retained = _undo.Take(Capacity).Reverse().ToArray();
            _undo.Clear();
            foreach (var item in retained)
            {
                _undo.Push(item);
            }
        }
    }

    /// <summary>Undoes the most recent command, if available.</summary>
    public bool Undo()
    {
        if (!_undo.TryPeek(out var command))
        {
            return false;
        }

        command.Undo();
        _undo.Pop();
        _redo.Push(command);
        return true;
    }

    /// <summary>Re-executes the most recently undone command, if available.</summary>
    public bool Redo()
    {
        if (!_redo.TryPeek(out var command))
        {
            return false;
        }

        command.Execute();
        _redo.Pop();
        _undo.Push(command);
        return true;
    }
}

/// <summary>Undoable assignment to an inspectable property.</summary>
public sealed class PropertyEditCommand(
    object target,
    InspectableProperty property,
    object? newValue) : IUndoableCommand
{
    private readonly object? _oldValue = property.GetValue(target);

    public string Description => $"Edit {property.Name}";
    public void Execute() => property.SetValue(target, newValue);
    public void Undo() => property.SetValue(target, _oldValue);
}

/// <summary>Undoable insertion of a construct into a scene tree.</summary>
public sealed class AddConstructCommand(
    SceneGraph scene,
    Construct construct,
    Construct? parent = null) : IUndoableCommand
{
    public string Description => $"Add {construct.Name}";

    public void Execute()
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(construct);
        if (parent is null)
        {
            scene.Add(construct);
        }
        else
        {
            parent.AddDescendant(construct);
        }
    }

    public void Undo()
    {
        if (construct.Branch is { } currentParent)
        {
            currentParent.RemoveDescendant(construct);
        }
        else
        {
            scene.Remove(construct);
        }
    }
}
