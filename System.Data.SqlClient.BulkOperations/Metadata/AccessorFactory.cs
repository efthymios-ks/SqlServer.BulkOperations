using System.Linq.Expressions;
using System.Reflection;

namespace System.Data.SqlClient.BulkOperations.Metadata;

/// <summary>
/// Compiles property access into delegates once per type: reflection per row would dominate the
/// cost of a bulk copy.
/// </summary>
internal static class AccessorFactory
{
    public static Func<object, object?> BuildGetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var typedInstance = Expression.Convert(instance, property.DeclaringType!);
        var access = Expression.Property(typedInstance, property);
        var boxed = Expression.Convert(access, typeof(object));

        return Expression
            .Lambda<Func<object, object?>>(boxed, instance)
            .Compile();
    }

    /// <summary>Null for a read-only property, which is what blocks output write-back onto it.</summary>
    public static Action<object, object?>? BuildSetter(PropertyInfo property)
    {
        if (!property.CanWrite || property.GetSetMethod(nonPublic: true) is not { } setMethod)
        {
            return null;
        }

        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");
        var typedInstance = Expression.Convert(instance, property.DeclaringType!);
        var typedValue = Expression.Convert(value, property.PropertyType);
        var assign = Expression.Call(typedInstance, setMethod, typedValue);

        return Expression
            .Lambda<Action<object, object?>>(assign, instance, value)
            .Compile();
    }
}
