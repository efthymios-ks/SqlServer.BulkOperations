using System.Linq.Expressions;
using System.Reflection;
using System.Data.SqlClient.BulkOperations.Exceptions;
using System.Data.SqlClient.BulkOperations.Metadata;

namespace System.Data.SqlClient.BulkOperations.Expressions;

/// <summary>
/// Reads the property selectors the fluent API takes, accepting either a single property
/// (<c>book =&gt; book.Isbn</c>) or an anonymous type of several (<c>book =&gt; new { book.Title, book.Price }</c>).
/// </summary>
internal static class PropertySelectorParser
{
    public static IReadOnlyList<ColumnMetadata> Parse<TEntity>(
        EntityMetadata metadata,
        Expression<Func<TEntity, object?>> selector
    ) => [.. ParsePropertyNames(selector).Select(name => Resolve(metadata, name))];

    public static IReadOnlyList<string> ParsePropertyNames<TEntity>(
        Expression<Func<TEntity, object?>> selector
    )
    {
        ArgumentNullException.ThrowIfNull(selector);

        var body = Unwrap(selector.Body);

        return body is NewExpression newExpression
            ? [.. newExpression.Arguments.Select(GetPropertyName)]
            : [GetPropertyName(body)];
    }

    private static ColumnMetadata Resolve(EntityMetadata metadata, string propertyName)
        => metadata.FindByPropertyName(propertyName)
            ?? throw new BulkConfigurationException(
                $"'{propertyName}' is not a mapped property of '{metadata.ClrType.Name}'."
            );

    private static string GetPropertyName(Expression expression)
        => Unwrap(expression) is MemberExpression { Member: PropertyInfo property }
            ? property.Name
            : throw new BulkConfigurationException(
                $"Expected a property access expression, got '{expression.NodeType}'."
            );

    /// <summary>Strips the boxing conversion the compiler inserts when a value type is returned as object.</summary>
    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression
            {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
            } unary
        )
        {
            expression = unary.Operand;
        }

        return expression;
    }
}
