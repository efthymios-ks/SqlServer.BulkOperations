using System.Data;
using System.Reflection;

namespace System.Data.SqlClient.BulkOperations.Metadata;

/// <summary>
/// A record so that derived views of a column — a remapped name, a promoted key — are one
/// <c>with</c> expression rather than a hand-copied property list that rots on every new field.
/// </summary>
internal sealed record ColumnMetadata
{
    public required PropertyInfo Property { get; init; }

    public required string PropertyName { get; init; }

    public required string ColumnName { get; init; }

    public int Order { get; init; }

    public required SqlDbType SqlDbType { get; init; }

    public required string SqlTypeDeclaration { get; init; }

    public int? MaxLength { get; init; }

    public byte? Precision { get; init; }

    public byte? Scale { get; init; }

    public bool IsNullable { get; init; }

    public bool IsKey { get; init; }

    public bool IsIdentity { get; init; }

    public bool IsComputed { get; init; }

    public bool IsConcurrencyToken { get; init; }

    public bool IsRowVersion { get; init; }

    public required Func<object, object?> Getter { get; init; }

    public Action<object, object?>? Setter { get; init; }

    public bool IsStoreGenerated
        => IsIdentity || IsComputed || IsRowVersion;

    /// <summary>True for the character types that need an explicit collation in the staging table.</summary>
    public bool IsCharacterType
        => SqlDbType is SqlDbType.NVarChar or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.Char;
}
