using System.Data.SqlClient.BulkOperations.Execution;

namespace System.Data.SqlClient.BulkOperations.Metadata;

internal sealed class EntityMetadata
{
    private readonly Dictionary<string, ColumnMetadata> _byPropertyName;

    internal EntityMetadata(
        Type clrType,
        string schema,
        string tableName,
        IReadOnlyList<ColumnMetadata> columns
    )
    {
        ClrType = clrType;
        Schema = schema;
        TableName = tableName;
        Columns = columns;

        _byPropertyName = columns.ToDictionary(column => column.PropertyName, StringComparer.Ordinal);
        ByPropertyName = _byPropertyName;

        KeyColumns =
        [
            .. columns
                .Where(column => column.IsKey)
                .OrderBy(column => column.Order)
        ];

        IdentityColumn = columns.FirstOrDefault(column => column.IsIdentity);
        ConcurrencyColumn = columns.FirstOrDefault(column => column.IsConcurrencyToken || column.IsRowVersion);
    }

    public Type ClrType { get; }

    public string Schema { get; }

    public string TableName { get; }

    public IReadOnlyList<ColumnMetadata> Columns { get; }

    public IReadOnlyList<ColumnMetadata> KeyColumns { get; }

    public ColumnMetadata? IdentityColumn { get; }

    public ColumnMetadata? ConcurrencyColumn { get; }

    public IReadOnlyDictionary<string, ColumnMetadata> ByPropertyName { get; }

    public string QualifiedName
        => SqlIdentifier.QualifiedName(Schema, TableName);

    public ColumnMetadata? FindByPropertyName(string propertyName)
        => _byPropertyName.GetValueOrDefault(propertyName);
}
