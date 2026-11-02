using System.Collections.ObjectModel;

namespace Tiny3D.Scene;

public sealed class MetadataStacks
{
    private readonly Dictionary<string, IMetadataStack> _stacks = new(StringComparer.Ordinal);

    public MdStack<T> CreateNewStack<T>(string name)
    {
        ValidateName(name);
        if (_stacks.ContainsKey(name))
        {
            throw new InvalidOperationException($"A metadata stack named '{name}' already exists.");
        }

        var stack = new MdStack<T>();
        _stacks.Add(name, stack);
        return stack;
    }

    public void FreezeStack(string stack)
    {
        GetStack(stack).Freeze();
    }

    public void ClearStack(string stack)
    {
        GetStack(stack).Clear();
    }

    public MdStack<T> GetStack<T>(string name)
    {
        if (GetStack(name) is MdStack<T> stack)
        {
            return stack;
        }

        throw new InvalidOperationException($"Metadata stack '{name}' does not contain values of type {typeof(T).Name}.");
    }

    private IMetadataStack GetStack(string name)
    {
        ValidateName(name);
        return _stacks.TryGetValue(name, out var stack)
            ? stack
            : throw new KeyNotFoundException($"Metadata stack '{name}' does not exist.");
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
    }

}

internal interface IMetadataStack
{
    void Freeze();
    void Clear();
}

public sealed class MdStack<T> : IMetadataStack
{
    private readonly List<T> _values = [];
    private readonly ReadOnlyCollection<T> _valuesView;
    private bool _isFrozen;

    public MdStack()
    {
        _valuesView = _values.AsReadOnly();
    }

    public IReadOnlyList<T> Values => _valuesView;
    public bool IsFrozen => _isFrozen;
    public bool QueryNewEntries { get; set; }
    public Func<T, bool>? OnQuery { get; set; }

    public bool Include(T value)
    {
        ThrowIfFrozen();
        if (_values.Contains(value))
        {
            return false;
        }

        if (QueryNewEntries && (OnQuery is null || !OnQuery(value)))
        {
            return false;
        }

        _values.Add(value);
        return true;
    }

    public bool Exclude(T value)
    {
        ThrowIfFrozen();
        return _values.Remove(value);
    }

    void IMetadataStack.Freeze() => _isFrozen = true;

    void IMetadataStack.Clear()
    {
        ThrowIfFrozen();
        _values.Clear();
    }

    private void ThrowIfFrozen()
    {
        if (_isFrozen)
        {
            throw new InvalidOperationException("A frozen metadata stack cannot be modified.");
        }
    }
}
