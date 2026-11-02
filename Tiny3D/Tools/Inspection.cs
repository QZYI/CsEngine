using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Tiny3D.Tools;

/// <summary>Marks a public property for runtime editor inspection.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InspectAttribute : Attribute
{
    public InspectAttribute(string? group = null)
    {
        Group = group ?? "Default";
    }

    /// <summary>Inspector group heading for the property.</summary>
    public string Group { get; }
    /// <summary>Gets or sets whether the property is read-only in the inspector.</summary>
    public bool ReadOnly { get; init; }
}

/// <summary>Limits the accepted numeric value of an inspected property.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RangeAttribute : Attribute
{
    public RangeAttribute(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Range bounds must be finite and ordered.");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }
    public double Maximum { get; }
}

/// <summary>Typed metadata and accessors for one inspected property.</summary>
public sealed class InspectableProperty
{
    private readonly Func<object, object?> _getter;
    private readonly Action<object, object?>? _setter;

    internal InspectableProperty(
        string name,
        Type propertyType,
        string group,
        bool readOnly,
        double? minimum,
        double? maximum,
        Func<object, object?> getter,
        Action<object, object?>? setter)
    {
        Name = name;
        PropertyType = propertyType;
        Group = group;
        IsReadOnly = readOnly || setter is null;
        Minimum = minimum;
        Maximum = maximum;
        _getter = getter;
        _setter = IsReadOnly ? null : setter;
    }

    public string Name { get; }
    public Type PropertyType { get; }
    public string Group { get; }
    public bool IsReadOnly { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    /// <summary>Gets the property's value from a target instance.</summary>
    public object? GetValue(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _getter(target);
    }

    internal InspectableProperty Bind(string name, Func<object, object?> nestedTarget) =>
        new(
            name, PropertyType, Group, IsReadOnly, Minimum, Maximum,
            target => _getter(nestedTarget(target)!),
            _setter is null ? null : (target, value) => _setter(nestedTarget(target)!, value));

    internal InspectableProperty AsReadOnly() =>
        new(Name, PropertyType, Group, true, Minimum, Maximum, _getter, _setter);

    /// <summary>Validates and assigns a value on a target instance.</summary>
    public void SetValue(object target, object? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"Property '{Name}' is read-only.");
        }

        if (value is null && PropertyType.IsValueType && Nullable.GetUnderlyingType(PropertyType) is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value is not null && !PropertyType.IsInstanceOfType(value))
        {
            throw new ArgumentException($"Value must be of type {PropertyType.Name}.", nameof(value));
        }

        if (value is IConvertible && (Minimum.HasValue || Maximum.HasValue))
        {
            var numericValue = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            if ((Minimum.HasValue && numericValue < Minimum) ||
                (Maximum.HasValue && numericValue > Maximum))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"Value must be between {Minimum} and {Maximum}.");
            }
        }

        _setter!(target, value);
    }
}

/// <summary>Discovers, caches, or explicitly registers properties shown by the runtime inspector.</summary>
public static class InspectionRegistry
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<InspectableProperty>> Cache = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<InspectableProperty>> Registered = new();

    /// <summary>Registers explicit inspectable properties for a target type.</summary>
    public static void Register<TTarget>(IEnumerable<InspectableProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var entries = properties.ToArray();
        if (entries.Any(property => property is null))
        {
            throw new ArgumentException("Inspectable property registrations cannot contain null entries.", nameof(properties));
        }

        Registered[typeof(TTarget)] = entries;
        Cache.TryRemove(typeof(TTarget), out _);
    }

    /// <summary>Gets cached or discovered properties for a target type.</summary>
    public static IReadOnlyList<InspectableProperty> GetProperties(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        return Cache.GetOrAdd(targetType, BuildProperties);
    }

    public static IReadOnlyList<InspectableProperty> GetProperties<TTarget>() =>
        GetProperties(typeof(TTarget));

    public static InspectableProperty CreateProperty<TTarget, TValue>(
        string name,
        string group,
        Func<TTarget, TValue> getter,
        Action<TTarget, TValue>? setter,
        bool readOnly = false,
        double? minimum = null,
        double? maximum = null)
        where TTarget : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(getter);
        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
        {
            throw new ArgumentException("The minimum cannot exceed the maximum.");
        }

        return new InspectableProperty(
            name, typeof(TValue), group,
            readOnly, minimum, maximum,
            target => getter((TTarget)target),
            setter is null ? null : (target, value) => setter((TTarget)target, (TValue)value!));
    }

    private static IReadOnlyList<InspectableProperty> BuildProperties(Type type)
    {
        if (Registered.TryGetValue(type, out var registered))
        {
            return registered;
        }

        var properties = new List<InspectableProperty>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var inspect = property.GetCustomAttribute<InspectAttribute>();
            if (inspect is null || property.GetIndexParameters().Length != 0 || property.GetMethod is null)
            {
                continue;
            }

            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(object), "value");
            var typedTarget = Expression.Convert(target, type);
            var access = Expression.Property(typedTarget, property);
            var getter = Expression.Lambda<Func<object, object?>>(
                Expression.Convert(access, typeof(object)), target).Compile();
            Action<object, object?>? setter = null;
            if (property.SetMethod is { IsPublic: true } && !inspect.ReadOnly)
            {
                setter = Expression.Lambda<Action<object, object?>>(
                    Expression.Assign(access, Expression.Convert(value, property.PropertyType)),
                    target, value).Compile();
            }

            var range = property.GetCustomAttribute<RangeAttribute>();
            if (range is not null && !IsNumericType(property.PropertyType))
            {
                throw new InvalidOperationException($"[Range] can only be applied to numeric properties: {type.Name}.{property.Name}.");
            }

            properties.Add(new InspectableProperty(
                property.Name, property.PropertyType, inspect.Group, inspect.ReadOnly,
                range?.Minimum, range?.Maximum, getter, setter));
        }

        return properties;
    }

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte or
            TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 or
            TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
    }
}
