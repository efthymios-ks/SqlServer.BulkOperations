using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;
using System.Data.SqlClient.BulkOperations.Exceptions;

namespace System.Data.SqlClient.BulkOperations.Metadata;

internal static class EntityMetadataFactory
{
    private const string DefaultSchema = "dbo";

    private const int RowVersionLength = 8;

    /// <summary>EF Core's PrecisionAttribute, matched by name so the library need not reference EF Core.</summary>
    private const string PrecisionAttributeName = "PrecisionAttribute";

    /// <summary>Reference and struct types that map to a single column rather than a nested entity.</summary>
    private static readonly HashSet<Type> _storableReferenceTypes =
    [
        typeof(string),
        typeof(byte[]),
        typeof(char[]),
        typeof(decimal),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(DateOnly),
        typeof(TimeOnly)
    ];

    public static EntityMetadata Build(Type type)
    {
        var tableAttribute = type.GetCustomAttribute<TableAttribute>();

        List<ColumnMetadata> columns =
        [
            .. type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(IsMappable)
                .Select(BuildColumn)
        ];

        if (columns.Count == 0)
        {
            var error = new BulkConfigurationException(
                message: $"No mapped properties found on '{type.FullName}'."
            );

            throw error;
        }

        ApplyKeyConvention(type, columns);

        return new EntityMetadata(
            clrType: type,
            schema: Coalesce(tableAttribute?.Schema, DefaultSchema),
            tableName: Coalesce(tableAttribute?.Name, type.Name),
            columns: columns
        );
    }

    private static bool IsMappable(PropertyInfo property)
        => property.CanRead
            && property.GetIndexParameters().Length == 0
            && property.GetCustomAttribute<NotMappedAttribute>() is null
            && !IsNavigationOrComplex(property.PropertyType);

    private static ColumnMetadata BuildColumn(PropertyInfo property)
    {
        var declaredType = property.PropertyType;
        var nonNullableType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        var columnAttribute = property.GetCustomAttribute<ColumnAttribute>();
        var generated = property.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption;
        var isRowVersion = IsRowVersion(property, columnAttribute);
        var isKey = property.GetCustomAttribute<KeyAttribute>() is not null;

        var sqlDbType = isRowVersion
            ? SqlDbType.Binary
            : SqlTypeMapper.Map(nonNullableType);

        var maxLength = isRowVersion
            ? RowVersionLength
            : property.GetCustomAttribute<MaxLengthAttribute>()?.Length
                ?? property.GetCustomAttribute<StringLengthAttribute>()?.MaximumLength;

        var (precision, scale) = ReadPrecisionScale(property);

        return new ColumnMetadata
        {
            Property = property,
            PropertyName = property.Name,
            ColumnName = Coalesce(columnAttribute?.Name, property.Name),
            Order = columnAttribute?.Order ?? 0,
            SqlDbType = sqlDbType,
            SqlTypeDeclaration = DeclareType(
                columnAttribute: columnAttribute,
                isRowVersion: isRowVersion,
                sqlDbType: sqlDbType,
                maxLength: maxLength,
                precision: precision,
                scale: scale
            ),
            MaxLength = maxLength,
            Precision = precision,
            Scale = scale,
            IsNullable = IsNullable(property, declaredType, nonNullableType, isKey),
            IsKey = isKey,
            IsIdentity = generated is DatabaseGeneratedOption.Identity,
            IsComputed = generated is DatabaseGeneratedOption.Computed,
            IsConcurrencyToken = property.GetCustomAttribute<ConcurrencyCheckAttribute>() is not null || isRowVersion,
            IsRowVersion = isRowVersion,
            Getter = AccessorFactory.BuildGetter(property),
            Setter = AccessorFactory.BuildSetter(property)
        };
    }

    private static bool IsRowVersion(PropertyInfo property, ColumnAttribute? columnAttribute)
        => property.GetCustomAttribute<TimestampAttribute>() is not null
            || string.Equals(columnAttribute?.TypeName, "rowversion", StringComparison.OrdinalIgnoreCase)
            || string.Equals(columnAttribute?.TypeName, "timestamp", StringComparison.OrdinalIgnoreCase);

    private static bool IsNullable(
        PropertyInfo property,
        Type declaredType,
        Type nonNullableType,
        bool isKey
    )
        => property.GetCustomAttribute<RequiredAttribute>() is null
            && !isKey
            && (Nullable.GetUnderlyingType(declaredType) is not null || !nonNullableType.IsValueType);

    private static string DeclareType(
        ColumnAttribute? columnAttribute,
        bool isRowVersion,
        SqlDbType sqlDbType,
        int? maxLength,
        byte? precision,
        byte? scale
    )
    {
        if (isRowVersion)
        {
            // A rowversion arrives as bytes; the staging table only needs somewhere to put them.
            return $"binary({RowVersionLength})";
        }

        return string.IsNullOrEmpty(columnAttribute?.TypeName)
            ? SqlTypeMapper.Declare(sqlDbType, maxLength, precision, scale)
            : columnAttribute.TypeName;
    }

    private static (byte? Precision, byte? Scale) ReadPrecisionScale(PropertyInfo property)
    {
        var attribute = property
            .GetCustomAttributes()
            .FirstOrDefault(candidate => candidate.GetType().Name == PrecisionAttributeName);

        if (attribute is null)
        {
            return (null, null);
        }

        var attributeType = attribute.GetType();

        return (
            Precision: ToByte(attributeType.GetProperty("Precision")?.GetValue(attribute)),
            Scale: ToByte(attributeType.GetProperty("Scale")?.GetValue(attribute))
        );
    }

    private static byte? ToByte(object? value)
        => value is int number and >= 0 and <= byte.MaxValue
            ? (byte)number
            : null;

    /// <summary>
    /// Without a [Key], fall back to the Id / TypeNameId convention and treat an integral one as an
    /// identity column, which is what the matching CREATE TABLE almost always declares.
    /// </summary>
    private static void ApplyKeyConvention(Type type, List<ColumnMetadata> columns)
    {
        if (columns.Any(column => column.IsKey))
        {
            return;
        }

        var index = columns.FindIndex(column =>
            column.PropertyName == "Id" || column.PropertyName == type.Name + "Id");

        if (index < 0)
        {
            return;
        }

        var idColumn = columns[index];
        var isIntegral = idColumn.SqlDbType is SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt;

        columns[index] = idColumn with
        {
            IsNullable = false,
            IsKey = true,
            IsIdentity = idColumn.IsIdentity || isIntegral
        };
    }

    private static bool IsNavigationOrComplex(Type type)
    {
        var nonNullableType = Nullable.GetUnderlyingType(type) ?? type;

        if (nonNullableType.IsEnum || nonNullableType.IsPrimitive)
        {
            return false;
        }

        return !_storableReferenceTypes.Contains(nonNullableType);
    }

    private static string Coalesce(string? candidate, string fallback)
        => string.IsNullOrEmpty(candidate) ? fallback : candidate;
}
